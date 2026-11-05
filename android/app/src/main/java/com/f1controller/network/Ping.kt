package com.f1controller.network

import java.nio.ByteBuffer

/** [0xFF 0xFF 0xFF 0xFF][seq:u32][timestamp_us:u64][engineer level:u8]; the receiver echoes the first 16 bytes. */
const val PING_SIZE = 17

private const val PING_MARKER = 0xFFFFFFFF.toInt()

/**
 * Writes a ping into [buf] (little-endian, cleared first). [level] is the engineer
 * chattiness (0 Off … 3 Talkative); the receiver uses it to pace the lap report.
 */
fun writePing(buf: ByteBuffer, seq: Int, timestampUs: Long, level: Int) {
    buf.clear()
    buf.putInt(PING_MARKER)
    buf.putInt(seq)
    buf.putLong(timestampUs)
    buf.put(level.coerceIn(0, 3).toByte())
}

/** v2 ping payload: [FFFFFFFF][seq:u32][ts_us:u64][level:u8][phoneNonce:8]; sealed by PacketAuth before sending. */
const val PING_PAYLOAD_SIZE = 25

fun writePing(buf: ByteBuffer, seq: Int, timestampUs: Long, level: Int, nonce: ByteArray) {
    require(nonce.size == 8) { "nonce must be 8 bytes" }
    buf.clear()
    buf.putInt(PING_MARKER)
    buf.putInt(seq)
    buf.putLong(timestampUs)
    buf.put(level.coerceIn(0, 3).toByte())
    buf.put(nonce)
}
