package com.f1controller.network

import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.security.SecureRandom
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicReference
import kotlin.concurrent.thread

/**
 * Phase 1: UDP Transport Layer
 *
 * Sends control packets at 250 Hz over UDP and measures round-trip latency
 * via a ping/pong side-channel.
 *
 * Control packet (18 bytes, little-endian):
 *   [seq:u32][timestamp_us:u64][steer:i16][throttle:u8][brake:u8][buttons:u16]
 *
 * Ping packet (25 bytes, see [writePing]):
 *   [0xFF 0xFF 0xFF 0xFF][seq:u32][timestamp_us:u64][level:u8][phoneNonce:8]
 *
 * Pong packet (24 bytes): the first 16 bytes of the ping ‖ pcSession:8.
 *
 * Every datagram carries the PacketAuth trailer (spec §3); anything [PongVerifier]
 * rejects is dropped. Control packets go out only once a valid pong taught us pcSession.
 *
 * Game state packet (16 bytes, receiver → phone, whenever the game sends telemetry):
 *   [0xFE 0xFF 0xFF 0xFF][gear:i8][revLightsPct:u8][drsAllowed:u8][drsOpen:u8]
 *   [speedKph:u16][rpm:u16][ersPct:u8][ersMode:u8][pitLimiter:u8][reserved:u8]
 *
 * Race state packet (62 bytes, 54 from older receivers, receiver → phone, ~10 Hz): marker 0xFFFFFFFD;
 * layout in PacketParser.cs (WriteRaceState) and [onRaceState].
 *
 * Radio packet (9 + text bytes, receiver → phone, one per engineer call, sent 3×):
 *   [0xFC 0xFF 0xFF 0xFF][callId:u16][priority:u8][category:u8][textLen:u8][UTF-8 text ≤ 120]
 *   Repeats share a callId and are dropped here; see [decodeRadioCall].
 *
 * UDP has no connection, so "connected" means a valid pong arrived within the last
 * [LINK_TIMEOUT_NS]. "active" means the send threads are running.
 */
class UdpTransport {

    // ---- Public state ----

    data class TransportState(
        val isActive: Boolean = false,
        val isConnected: Boolean = false,
        val rttAvgMs: Float = 0f,
        val rttP99Ms: Float = 0f,
        val sendRateHz: Float = 0f,
        val packetsSent: Long = 0L,
        val pongReceived: Long = 0L,
        val error: String? = null,
    )

    private val _state = MutableStateFlow(TransportState())
    val state: StateFlow<TransportState> = _state

    /** Player car values from the game. ersMode: 0 none, 1 medium, 2 hotlap, 3 overtake. */
    data class GameTelemetry(
        val gear: Int,          // -1 = R, 0 = N
        val revLightsPct: Int,  // 0-100
        val drsAllowed: Boolean,
        val drsOpen: Boolean,
        val speedKph: Int,
        val rpm: Int,
        val ersPct: Int,        // battery 0-100
        val ersMode: Int,
        val pitLimiter: Boolean,
    )

    /**
     * Race timing and car condition (F1 2021), ~10 Hz. Times in ms; nullable
     * values aren't known yet (no best lap, no car ahead...). Sector status:
     * 0 none, 1 slower, 2 personal best, 3 overall best. Tyres: FL, FR, RL, RR.
     */
    data class RaceState(
        val position: Int,
        val totalLaps: Int,
        val currentLap: Int,
        val sector: Int,          // 0-2
        val lapInvalid: Boolean,
        val compound: Int,        // visual: 16 soft, 17 medium, 18 hard, 7 inter, 8 wet
        val tyreAgeLaps: Int,
        val currentLapMs: Long,
        val lastLapMs: Long,
        val bestLapMs: Long,
        val deltaMs: Int?,
        val s1Ms: Int,
        val s2Ms: Int,
        val s1Status: Int,
        val s2Status: Int,
        val fuelLaps: Float,      // laps of fuel left beyond the race distance
        val gapAheadMs: Int?,
        val gapBehindMs: Int?,
        val tyreWear: IntArray,   // %
        val tyreTemp: IntArray,   // surface °C
        val trackId: Int = -1,    // F1 2021 track id, -1 unknown / old receiver
        val ttDryMs: Long = 0,    // Time Trial personal bests for the track, 0 = none
        val ttWetMs: Long = 0,
    )

    private val _race = MutableStateFlow<RaceState?>(null)
    val race: StateFlow<RaceState?> = _race
    @Volatile private var lastRaceNs = 0L

    private val _radio = MutableSharedFlow<RadioCall>(extraBufferCapacity = 8)
    /** Engineer calls, each delivered once even though the receiver sends it 3 times. */
    val radio: SharedFlow<RadioCall> = _radio
    // Drops the repeats of each call; only touched by the receive thread
    private val recentCallIds = RecentCallIds()

    private val _telemetry = MutableStateFlow<GameTelemetry?>(null)
    /** Latest game telemetry, or null if none arrived in the last [TELEMETRY_TIMEOUT_NS]. */
    val telemetry: StateFlow<GameTelemetry?> = _telemetry
    @Volatile private var lastTelemetryNs = 0L

    // ---- Configuration ----

    @Volatile var serverIp: String = "192.168.1.100"
    @Volatile var serverPort: Int = 5555

    /** Start sending as soon as the app opens (UDP needs no handshake, so this
     *  also "reconnects" by itself whenever the receiver is restarted). */
    @Volatile var autoConnect: Boolean = true

    /** Pairing key; null → [connect] does nothing. Changes take effect on the next connect(). */
    @Volatile var auth: PacketAuth? = null

    /** Engineer chattiness (0 Off … 3 Talkative), sent in every ping so the receiver can pace the lap report. */
    @Volatile var engineerLevel: () -> Int = { 2 }

    // ---- Internals ----

    /**
     * One connect() call. Threads exit as soon as their session is no longer
     * current, so a fast disconnect/connect can't leave duplicate senders.
     */
    private class Session(val socket: DatagramSocket, val auth: PacketAuth) {
        val nonce = ByteArray(8).also { SecureRandom().nextBytes(it) }
        val verifier = PongVerifier(auth, nonce)
        // One counter for pings and controls (the PC drops any ctr ≤ the last it accepted)
        val sendCtr = AtomicInteger(0)
        val sendLock = Any()
        val pingSeq = AtomicInteger(0)
        // Per session so a late pong of an old session can't mark a new candidate address connected
        @Volatile var lastPongNs = 0L
        val startNs = System.nanoTime()
        /** The ip/port this session's socket is connected to; set once after resolve. */
        @Volatile var server: Pair<String, Int>? = null
        // Seqs of the last 16 pings; seqs are consecutive so slot = seq and 15
        private val outstanding = IntArray(16)

        fun pingSent(seq: Int) = synchronized(outstanding) { outstanding[seq and 15] = seq }

        /** True once per recent ping seq, so a replayed pong can't count twice. */
        fun takeOutstanding(seq: Int): Boolean = synchronized(outstanding) {
            if (seq == 0 || outstanding[seq and 15] != seq) return false
            outstanding[seq and 15] = 0
            true
        }
    }

    private val session = AtomicReference<Session?>(null)
    private val seq = AtomicInteger(0)
    // Session swaps and every public-state write take this lock, so a thread of an old
    // session can't write after connect()/disconnect() reset the state.
    private val stateLock = Any()

    private val _connectedServer = MutableStateFlow<Pair<String, Int>?>(null)
    /** ip/port of the current session once it got a valid pong (the only server worth persisting). */
    val connectedServer: StateFlow<Pair<String, Int>?> = _connectedServer

    private inline fun ifCurrent(s: Session, block: () -> Unit) = synchronized(stateLock) { if (isCurrent(s)) block() }

    // Shared control data — written by sensor/UI threads, read by send thread
    @Volatile var steer: Short = 0
    @Volatile var throttle: Int = 0   // 0-255
    @Volatile var brake: Int = 0      // 0-255
    @Volatile var buttons: Int = 0    // XUSB_BUTTON bitmask

    // Ping/pong RTT tracking
    private val rttSamples = FloatArray(100)  // circular buffer
    private var rttHead = 0
    private var rttCount = 0
    @Volatile private var pongCount = 0L
    /** System.nanoTime() of the last accepted pong of the current connect() (0 = never). */
    val lastValidPongNs: Long get() = session.get()?.lastPongNs ?: 0L
    /** System.nanoTime() when the current connect() started (0 = not running). */
    val connectStartNs: Long get() = session.get()?.startNs ?: 0L

    // Send rate tracking
    private val sendTimestamps = LongArray(64)
    private var sendTsHead = 0

    /** Start the send + receive threads. */
    fun connect() {
        val a = auth ?: return
        if (session.get() != null) return

        val socket = DatagramSocket().apply { soTimeout = 50 } // ms — receive polling
        val s = Session(socket, a)
        synchronized(stateLock) {
            if (!session.compareAndSet(null, s)) {
                socket.close()
                return
            }
            resetStats()
            _state.value = TransportState(isActive = true)
            _connectedServer.value = null
        }

        // Resolve on a worker thread (getByName may hit DNS) and fail softly.
        thread(name = "udp-sender", isDaemon = true) {
            val ip = serverIp.trim()
            val port = serverPort
            try {
                // connect() also makes the OS drop datagrams from any other source
                socket.connect(InetAddress.getByName(ip), port)
                s.server = ip to port
            } catch (e: Exception) {
                fail(s, "Invalid server address: $ip")
                return@thread
            }

            thread(name = "udp-ping", isDaemon = true) { pingLoop(s) }
            thread(name = "udp-receiver", isDaemon = true) { receiveLoop(s) }
            sendLoop(s)
        }
    }

    fun disconnect() {
        synchronized(stateLock) {
            val s = session.getAndSet(null) ?: return
            s.socket.close()
            _state.value = TransportState()
            _telemetry.value = null
            _race.value = null
            _connectedServer.value = null
        }
    }

    fun isRunning(): Boolean = session.get() != null

    private fun isCurrent(s: Session) = session.get() === s

    private fun fail(s: Session, message: String) {
        synchronized(stateLock) {
            if (session.compareAndSet(s, null)) {
                s.socket.close()
                _state.value = TransportState(error = message)
                _connectedServer.value = null
            }
        }
    }

    /** Seal + send under one lock so datagrams leave in ctr order. Throws if the socket is closed. */
    private fun sealAndSend(s: Session, ctx: ByteArray, payload: ByteArray, len: Int, packet: DatagramPacket) {
        synchronized(s.sendLock) {
            s.auth.seal(PacketAuth.DIR_PHONE_TO_PC, ctx, payload, len, s.sendCtr.incrementAndGet(), packet.data)
            s.socket.send(packet)
        }
    }

    // Sender: 250 Hz control packets, only once a valid pong has given us pcSession
    private fun sendLoop(s: Session) {
        val buf = ByteBuffer.allocate(CONTROL_SIZE).order(ByteOrder.LITTLE_ENDIAN)
        val packet = DatagramPacket(ByteArray(CONTROL_SIZE + PacketAuth.TRAILER_SIZE), CONTROL_SIZE + PacketAuth.TRAILER_SIZE)
        val intervalNs = 4_000_000L // 4ms = 250 Hz
        var nextNs = System.nanoTime()
        var tick = 0

        while (isCurrent(s)) {
            val startNs = System.nanoTime()
            val pcSession = s.verifier.pcSession

            if (pcSession != null) {
                buf.clear()
                buf.putInt(seq.incrementAndGet())
                buf.putLong(startNs / 1000L)
                buf.putShort(steer)
                buf.put(throttle.toByte())
                buf.put(brake.toByte())
                buf.putShort(buttons.toShort())

                try {
                    sealAndSend(s, pcSession + s.nonce, buf.array(), CONTROL_SIZE, packet)
                    sendTimestamps[sendTsHead and 63] = startNs
                    sendTsHead++
                } catch (_: Exception) {
                    // Socket closed or transient network error — keep trying while current
                }
            }

            // Update UI state every 50 ticks (~200ms)
            if (++tick % 50 == 0) ifCurrent(s) {
                val pongNs = s.lastPongNs
                _state.value = _state.value.copy(
                    isConnected = pongNs != 0L && System.nanoTime() - pongNs < LINK_TIMEOUT_NS,
                    packetsSent = seq.get().toLong(),
                    sendRateHz = computeSendRate(),
                    rttAvgMs = computeRttAvg(),
                    rttP99Ms = computeRttP99(),
                    pongReceived = pongCount,
                )
                // Game paused/closed or telemetry off: stop showing stale values
                if (System.nanoTime() - lastTelemetryNs > TELEMETRY_TIMEOUT_NS) _telemetry.value = null
                if (System.nanoTime() - lastRaceNs > TELEMETRY_TIMEOUT_NS) _race.value = null
            }

            // Fixed-rate schedule: sleep most of the gap, spin the last ~1ms
            // (Thread.sleep routinely overshoots by up to a millisecond).
            nextNs += intervalNs
            val now = System.nanoTime()
            if (nextNs < now - intervalNs) nextNs = now // fell far behind; don't burst
            val toWait = nextNs - now
            if (toWait > 1_500_000L) {
                val sleepNs = toWait - 1_000_000L
                Thread.sleep(sleepNs / 1_000_000L, (sleepNs % 1_000_000L).toInt())
            }
            @Suppress("ControlFlowWithEmptyBody")
            while (System.nanoTime() < nextNs) {}
        }
    }

    // Ping every 100ms for the whole connection: handshake, RTT, and liveness
    private fun pingLoop(s: Session) {
        val payload = ByteBuffer.allocate(PING_PAYLOAD_SIZE).order(ByteOrder.LITTLE_ENDIAN)
        val packet = DatagramPacket(ByteArray(PING_PAYLOAD_SIZE + PacketAuth.TRAILER_SIZE), PING_PAYLOAD_SIZE + PacketAuth.TRAILER_SIZE)
        val noCtx = ByteArray(0)

        while (isCurrent(s)) {
            val seqNum = s.pingSeq.incrementAndGet()
            s.pingSent(seqNum)
            writePing(payload, seqNum, System.nanoTime() / 1000L, engineerLevel(), s.nonce)

            try {
                sealAndSend(s, noCtx, payload.array(), PING_PAYLOAD_SIZE, packet)
            } catch (_: Exception) {}

            try {
                Thread.sleep(100)
            } catch (_: InterruptedException) {
                return
            }
        }
    }

    // Listen for pongs and game state; only what the verifier accepts gets past the check
    private fun receiveLoop(s: Session) {
        val recvBuf = ByteArray(256)
        val recvPacket = DatagramPacket(recvBuf, recvBuf.size)

        while (isCurrent(s)) {
            try {
                recvPacket.length = recvBuf.size
                s.socket.receive(recvPacket)
                val len = recvPacket.length
                if (len < 4) continue

                val marker = ByteBuffer.wrap(recvBuf).order(ByteOrder.LITTLE_ENDIAN).getInt(0)
                val n = if (marker == PING_MARKER) s.verifier.acceptPong(recvBuf, len, s::takeOutstanding)
                    else s.verifier.acceptData(recvBuf, len)
                if (n == null) continue

                val bb = ByteBuffer.wrap(recvBuf, 0, n).order(ByteOrder.LITTLE_ENDIAN)
                bb.int // marker
                ifCurrent(s) {
                    when (marker) {
                        PING_MARKER -> onPong(s, bb)
                        // Radio packets can be shorter than the 16-byte minimum of the others
                        RADIO_MARKER -> decodeRadioCall(bb)?.let(::onRadioCall)
                        GAME_STATE_MARKER -> if (n >= 16) onGameState(bb)
                        RACE_STATE_MARKER -> if (n >= RACE_STATE_SIZE) onRaceState(bb, n)
                    }
                }
            } catch (_: java.net.SocketTimeoutException) {
                // Expected — soTimeout lets us notice disconnect
            } catch (_: Exception) {
                // Socket closed
            }
        }
    }

    /** Caller holds stateLock with [s] current. */
    private fun onPong(s: Session, bb: ByteBuffer) {
        bb.int // skip seq
        val sentUs = bb.long
        val nowNs = System.nanoTime()
        val rttMs = (nowNs / 1000L - sentUs) / 1000f

        synchronized(rttSamples) {
            rttSamples[rttHead % 100] = rttMs
            rttHead++
            if (rttCount < 100) rttCount++
            pongCount++
        }
        s.lastPongNs = nowNs
        _connectedServer.value = s.server
    }

    private fun onRadioCall(call: RadioCall) {
        if (recentCallIds.isRepeat(call.id, System.nanoTime() / 1_000_000L)) return
        _radio.tryEmit(call)
    }

    private fun onGameState(bb: ByteBuffer) {
        _telemetry.value = GameTelemetry(
            gear = bb.get().toInt(),
            revLightsPct = bb.get().toInt() and 0xFF,
            drsAllowed = bb.get().toInt() != 0,
            drsOpen = bb.get().toInt() != 0,
            speedKph = bb.short.toInt() and 0xFFFF,
            rpm = bb.short.toInt() and 0xFFFF,
            ersPct = bb.get().toInt() and 0xFF,
            ersMode = bb.get().toInt() and 0xFF,
            pitLimiter = bb.get().toInt() != 0,
        )
        lastTelemetryNs = System.nanoTime()
    }

    private fun onRaceState(bb: ByteBuffer, n: Int) {
        _race.value = parseRaceState(bb, n)
        lastRaceNs = System.nanoTime()
    }

    private fun resetStats() {
        seq.set(0)
        synchronized(rttSamples) {
            rttHead = 0
            rttCount = 0
            pongCount = 0L
        }
        sendTimestamps.fill(0L)
    }

    private fun computeSendRate(): Float {
        val now = System.nanoTime()
        var count = 0
        for (ts in sendTimestamps) {
            if (ts > 0 && now - ts < 1_000_000_000L) count++
        }
        // Buffer holds 64 timestamps; extrapolate from their span
        if (count < 2) return 0f
        var oldest = Long.MAX_VALUE
        var newest = Long.MIN_VALUE
        for (ts in sendTimestamps) {
            if (ts > 0 && now - ts < 1_000_000_000L) {
                if (ts < oldest) oldest = ts
                if (ts > newest) newest = ts
            }
        }
        val spanNs = newest - oldest
        return if (spanNs > 0) (count - 1) * 1e9f / spanNs else 0f
    }

    private fun computeRttAvg(): Float {
        synchronized(rttSamples) {
            if (rttCount == 0) return 0f
            var sum = 0f
            for (i in 0 until rttCount) sum += rttSamples[i]
            return sum / rttCount
        }
    }

    private fun computeRttP99(): Float {
        synchronized(rttSamples) {
            if (rttCount == 0) return 0f
            val sorted = rttSamples.copyOf(rttCount).apply { sort() }
            val idx = ((rttCount - 1) * 0.99f).toInt()
            return sorted[idx]
        }
    }

    companion object {
        private const val PING_MARKER = 0xFFFFFFFF.toInt()
        private const val GAME_STATE_MARKER = 0xFFFFFFFE.toInt()
        private const val RACE_STATE_MARKER = 0xFFFFFFFD.toInt()
        private const val RADIO_MARKER = 0xFFFFFFFC.toInt()
        private const val RACE_STATE_SIZE = 54
        private const val CONTROL_SIZE = 18
        private const val TELEMETRY_TIMEOUT_NS = 1_500_000_000L
        private const val LINK_TIMEOUT_NS = 1_000_000_000L
    }
}
