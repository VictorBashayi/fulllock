package com.f1controller.network

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlin.concurrent.thread

/**
 * Finds the PC by listening for the receiver's discovery beacon, which it
 * broadcasts about once a second on [DISCOVERY_PORT]:
 *   [0xFB 0xFF 0xFF 0xFF]["F1RX"][controlPort:u16]
 * The PC's IP is wherever the beacon came from. [onFound] runs on the listener
 * thread for every beacon; [shouldSwitchServer] decides whether to act on it.
 */
class DiscoveryListener(private val onFound: (ip: String, port: Int) -> Unit) {

    private val _found = MutableStateFlow<String?>(null)
    /** "ip:port" of the last beacon heard, or null if none yet. */
    val found: StateFlow<String?> = _found

    @Volatile private var socket: DatagramSocket? = null
    @Volatile private var running = false

    fun start() {
        if (running) return
        running = true
        thread(name = "udp-discovery", isDaemon = true) { listenLoop() }
    }

    fun stop() {
        running = false
        socket?.close()
    }

    private fun listenLoop() {
        val buf = ByteArray(64)
        val packet = DatagramPacket(buf, buf.size)
        while (running) {
            try {
                // reuseAddress: a recreated activity can bind before the old socket closes
                DatagramSocket(null).apply {
                    reuseAddress = true
                    broadcast = true
                    bind(InetSocketAddress(DISCOVERY_PORT))
                }.use { s ->
                    socket = s
                    while (running) {
                        packet.length = buf.size
                        s.receive(packet)
                        val port = decodeDiscovery(buf, packet.length) ?: continue
                        val ip = packet.address.hostAddress ?: continue
                        _found.value = "$ip:$port"
                        onFound(ip, port)
                    }
                }
            } catch (_: Exception) {
                // Socket closed by stop(), or bind failed — retry shortly while running
                if (running) Thread.sleep(2000)
            }
        }
    }

    companion object {
        const val DISCOVERY_PORT = 5556
    }
}

private const val DISCOVERY_MARKER = 0xFFFFFFFB.toInt()
private val DISCOVERY_TAG = "F1RX".toByteArray(Charsets.US_ASCII)

/** Returns the beacon's control port, or null if [bytes] isn't a discovery beacon. */
fun decodeDiscovery(bytes: ByteArray, length: Int): Int? {
    if (length < 10) return null
    val bb = ByteBuffer.wrap(bytes, 0, length).order(ByteOrder.LITTLE_ENDIAN)
    if (bb.int != DISCOVERY_MARKER) return null
    for (b in DISCOVERY_TAG) if (bb.get() != b) return null
    return bb.short.toInt() and 0xFFFF
}

/**
 * Move to a beaconing receiver only while there's no working link, so an IP
 * typed in by hand keeps winning as long as it works.
 */
fun shouldSwitchServer(
    connected: Boolean,
    currentIp: String,
    currentPort: Int,
    foundIp: String,
    foundPort: Int,
): Boolean = !connected && (currentIp.trim() != foundIp || currentPort != foundPort)
