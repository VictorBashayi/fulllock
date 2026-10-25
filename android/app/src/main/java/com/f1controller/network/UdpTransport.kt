package com.f1controller.network

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder
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
 * Ping packet (16 bytes):
 *   [0xFF 0xFF 0xFF 0xFF][seq:u32][timestamp_us:u64]
 *
 * Pong packet: same, echoed back by receiver.
 *
 * UDP has no connection, so "connected" means a pong arrived within the last
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

    // ---- Configuration ----

    @Volatile var serverIp: String = "192.168.1.100"
    @Volatile var serverPort: Int = 5555

    // ---- Internals ----

    /**
     * One connect() call. Threads exit as soon as their session is no longer
     * current, so a fast disconnect/connect can't leave duplicate senders.
     */
    private class Session(val socket: DatagramSocket)

    private val session = AtomicReference<Session?>(null)
    private val seq = AtomicInteger(0)

    // Shared control data — written by sensor/UI threads, read by send thread
    @Volatile var steer: Short = 0
    @Volatile var throttle: Int = 0   // 0-255
    @Volatile var brake: Int = 0      // 0-255
    @Volatile var buttons: Int = 0    // XUSB_BUTTON bitmask

    // Ping/pong RTT tracking
    private val pingSeq = AtomicInteger(0)
    private val rttSamples = FloatArray(100)  // circular buffer
    private var rttHead = 0
    private var rttCount = 0
    @Volatile private var pongCount = 0L
    @Volatile private var lastPongNs = 0L

    // Send rate tracking
    private val sendTimestamps = LongArray(64)
    private var sendTsHead = 0

    /** Start the send + receive threads. */
    fun connect() {
        if (session.get() != null) return

        val socket = DatagramSocket().apply { soTimeout = 50 } // ms — receive polling
        val s = Session(socket)
        if (!session.compareAndSet(null, s)) {
            socket.close()
            return
        }

        resetStats()
        _state.value = TransportState(isActive = true)

        // Resolve on a worker thread (getByName may hit DNS) and fail softly.
        thread(name = "udp-sender", isDaemon = true) {
            val addr = try {
                InetAddress.getByName(serverIp.trim())
            } catch (e: Exception) {
                fail(s, "Invalid server address: ${serverIp.trim()}")
                return@thread
            }
            val port = serverPort

            thread(name = "udp-ping", isDaemon = true) { pingLoop(s, addr, port) }
            thread(name = "udp-receiver", isDaemon = true) { receiveLoop(s) }
            sendLoop(s, addr, port)
        }
    }

    fun disconnect() {
        val s = session.getAndSet(null) ?: return
        s.socket.close()
        _state.value = TransportState()
    }

    fun isRunning(): Boolean = session.get() != null

    private fun isCurrent(s: Session) = session.get() === s

    private fun fail(s: Session, message: String) {
        if (session.compareAndSet(s, null)) {
            s.socket.close()
            _state.value = TransportState(error = message)
        }
    }

    // Sender: 250 Hz control packets
    private fun sendLoop(s: Session, addr: InetAddress, port: Int) {
        val buf = ByteBuffer.allocate(18).order(ByteOrder.LITTLE_ENDIAN)
        val packet = DatagramPacket(buf.array(), 18, addr, port)
        val intervalNs = 4_000_000L // 4ms = 250 Hz
        var nextNs = System.nanoTime()

        while (isCurrent(s)) {
            val startNs = System.nanoTime()
            val seqNum = seq.incrementAndGet()

            buf.clear()
            buf.putInt(seqNum)
            buf.putLong(startNs / 1000L)
            buf.putShort(steer)
            buf.put(throttle.toByte())
            buf.put(brake.toByte())
            buf.putShort(buttons.toShort())

            try {
                s.socket.send(packet)
                sendTimestamps[sendTsHead and 63] = startNs
                sendTsHead++
            } catch (_: Exception) {
                // Socket closed or transient network error — keep trying while current
            }

            // Update UI state every 50 packets (~200ms)
            if (seqNum % 50 == 0 && isCurrent(s)) {
                _state.value = _state.value.copy(
                    isConnected = System.nanoTime() - lastPongNs < LINK_TIMEOUT_NS,
                    packetsSent = seqNum.toLong(),
                    sendRateHz = computeSendRate(),
                    rttAvgMs = computeRttAvg(),
                    rttP99Ms = computeRttP99(),
                    pongReceived = pongCount,
                )
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

    // Ping every 100ms
    private fun pingLoop(s: Session, addr: InetAddress, port: Int) {
        val pingBuf = ByteBuffer.allocate(16).order(ByteOrder.LITTLE_ENDIAN)
        val packet = DatagramPacket(pingBuf.array(), 16, addr, port)

        while (isCurrent(s)) {
            pingBuf.clear()
            pingBuf.putInt(PING_MARKER)
            pingBuf.putInt(pingSeq.incrementAndGet())
            pingBuf.putLong(System.nanoTime() / 1000L)

            try {
                s.socket.send(packet)
            } catch (_: Exception) {}

            try {
                Thread.sleep(100)
            } catch (_: InterruptedException) {
                return
            }
        }
    }

    // Listen for pongs
    private fun receiveLoop(s: Session) {
        val recvBuf = ByteArray(16)
        val recvPacket = DatagramPacket(recvBuf, recvBuf.size)

        while (isCurrent(s)) {
            try {
                recvPacket.length = recvBuf.size
                s.socket.receive(recvPacket)
                if (recvPacket.length < 16) continue

                val bb = ByteBuffer.wrap(recvBuf, 0, 16).order(ByteOrder.LITTLE_ENDIAN)
                if (bb.int != PING_MARKER) continue
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
                lastPongNs = nowNs
            } catch (_: java.net.SocketTimeoutException) {
                // Expected — soTimeout lets us notice disconnect
            } catch (_: Exception) {
                // Socket closed
            }
        }
    }

    private fun resetStats() {
        seq.set(0)
        synchronized(rttSamples) {
            rttHead = 0
            rttCount = 0
            pongCount = 0L
        }
        lastPongNs = 0L
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
        private const val LINK_TIMEOUT_NS = 1_000_000_000L
    }
}
