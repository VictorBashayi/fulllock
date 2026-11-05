package com.f1controller.network

import org.junit.Assert.assertEquals
import org.junit.Test
import java.nio.ByteBuffer
import java.nio.ByteOrder

class PingTest {

    @Test
    fun v2PingMatchesVector() {
        val buf = ByteBuffer.allocate(PING_PAYLOAD_SIZE).order(ByteOrder.LITTLE_ENDIAN)
        writePing(buf, 1, 0L, 2, ByteArray(8) { (0x11 + it).toByte() })
        assertEquals(25, buf.position())
        assertEquals(
            "ffffffff010000000000000000000000021112131415161718",
            buf.array().joinToString("") { "%02x".format(it) })
    }

    @Test
    fun clampsLevel() {
        val buf = ByteBuffer.allocate(PING_PAYLOAD_SIZE).order(ByteOrder.LITTLE_ENDIAN)
        writePing(buf, 1, 0L, 9, ByteArray(8))
        assertEquals(3.toByte(), buf.array()[16])
    }
}
