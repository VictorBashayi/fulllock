package com.f1controller.network

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger
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
 */
class UdpTransport {

    // ---- Public state ----

    data class TransportState(
        val isConnected: Boolean = false,
        val rttAvgMs: Float = 0f,
        val rttP99Ms: Float = 0f,
        val sendRateHz: Float = 0f,
        val packetsSent: Long = 0L,
        val pongReceived: Long = 0L,
    )

    private val _state = MutableStateFlow(TransportState())
    val state: StateFlow<TransportState> = _state

    // ---- Configuration ----

    @Volatile var serverIp: String = "192.168.1.100"
    @Volatile var serverPort: Int = 5555

    // ---- Internals ----

    private val running = AtomicBoolean(false)
    private val seq = AtomicInteger(0)
    private var socket: DatagramSocket? = null

    // Shared control data — written by sensor thread, read by send thread
    @Volatile var steer: Short = 0
    @Volatile var throttle: Int = 0   // 0-255
    @Volatile var brake: Int = 0      // 0-255
    @Volatile var buttons: Int = 0    // UShort bitmask

    // Ping/pong RTT tracking
    private val pingSeq = AtomicInteger(0)
    private val rttSamples = FloatArray(100)  // circular buffer
    private var rttHead = 0
    private var rttCount = 0
    private var pongCount = 0L

    // Send rate tracking
    private val sendTimestamps = LongArray(64)
    private var sendTsHead = 0

    /** Start the send + receive threads. */
    fun connect() {
        if (running.getAndSet(true)) return

        socket = DatagramSocket().apply {
            soTimeout = 50 // ms — for receive polling
        }

        // Sender thread: 250 Hz control packets
        thread(name = "udp-sender", isDaemon = true) {
            val buf = ByteBuffer.allocate(18).order(ByteOrder.LITTLE_ENDIAN)
            val addr = InetAddress.getByName(serverIp)
            val intervalNs = 4_000_000L // 4ms = 250 Hz

            _state.value = _state.value.copy(isConnected = true)

            while (running.get()) {
                val startNs = System.nanoTime()
                val nowUs = startNs / 1000L
                val seqNum = seq.incrementAndGet()

                buf.clear()
                buf.putInt(seqNum)
                buf.putLong(nowUs)
                buf.putShort(steer)
                buf.put(throttle.toByte())
                buf.put(brake.toByte())
                buf.putShort(buttons.toShort())

                try {
                    val packet = DatagramPacket(buf.array(), 18, addr, serverPort)
                    socket?.send(packet)

                    // Track send rate
                    sendTimestamps[sendTsHead and 63] = startNs
                    sendTsHead++

                    // Update UI state every 50 packets (~200ms)
                    if (seqNum % 50 == 0) {
                        _state.value = _state.value.copy(
                            packetsSent = seqNum.toLong(),
                            sendRateHz = computeSendRate(),
                            rttAvgMs = computeRttAvg(),
                            rttP99Ms = computeRttP99(),
                            pongReceived = pongCount,
                        )
                    }
                } catch (_: Exception) {
                    // Socket closed or network error, keep trying
                }

                // Busy-wait for more accurate timing than Thread.sleep
                val elapsed = System.nanoTime() - startNs
                val toWait = intervalNs - elapsed
                if (toWait > 1_000_000L) {
                    Thread.sleep(toWait / 1_000_000L, (toWait % 1_000_000L).toInt())
                } else if (toWait > 0) {
                    // Spin-wait for sub-millisecond precision
                    @Suppress("ControlFlowWithEmptyBody")
                    while (System.nanoTime() - startNs < intervalNs) {}
                }
            }
        }

        // Ping thread: send ping every 100ms, listen for pong
        thread(name = "udp-ping", isDaemon = true) {
            val pingBuf = ByteBuffer.allocate(16).order(ByteOrder.LITTLE_ENDIAN)
            val addr = InetAddress.getByName(serverIp)

            while (running.get()) {
                val nowUs = System.nanoTime() / 1000L
                val ps = pingSeq.incrementAndGet()

                pingBuf.clear()
                pingBuf.putInt(0xFFFFFFFF.toInt()) // ping marker
                pingBuf.putInt(ps)
                pingBuf.putLong(nowUs)

                try {
                    val packet = DatagramPacket(pingBuf.array(), 16, addr, serverPort)
                    socket?.send(packet)
                } catch (_: Exception) {}

                Thread.sleep(100) // ping every 100ms
            }
        }

        // Receiver thread: listen for pong packets
        thread(name = "udp-receiver", isDaemon = true) {
            val recvBuf = ByteArray(16)
            val recvPacket = DatagramPacket(recvBuf, recvBuf.size)

            while (running.get()) {
                try {
                    socket?.receive(recvPacket)
                    if (recvPacket.length >= 16) {
                        val bb = ByteBuffer.wrap(recvBuf, 0, 16).order(ByteOrder.LITTLE_ENDIAN)
                        val marker = bb.int
                        if (marker == 0xFFFFFFFF.toInt()) {
                            // It's a pong
                            bb.int // skip seq
                            val sentUs = bb.long
                            val nowUs = System.nanoTime() / 1000L
                            val rttMs = (nowUs - sentUs) / 1000f

                            synchronized(rttSamples) {
                                rttSamples[rttHead % 100] = rttMs
                                rttHead++
                                if (rttCount < 100) rttCount++
                                pongCount++
                            }
                        }
                    }
                } catch (_: java.net.SocketTimeoutException) {
                    // Expected — we set soTimeout for polling
                } catch (_: Exception) {
                    // Socket closed
                }
            }
        }
    }

    fun disconnect() {
        running.set(false)
        socket?.close()
        socket = null
        _state.value = TransportState()
    }

    fun isRunning(): Boolean = running.get()

    private fun computeSendRate(): Float {
        val now = System.nanoTime()
        var count = 0
        for (ts in sendTimestamps) {
            if (ts > 0 && now - ts < 1_000_000_000L) count++
        }
        return count.toFloat()
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
}
