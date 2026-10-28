package com.f1controller.network

import org.junit.Assert.assertEquals
import org.junit.Test
import java.nio.ByteBuffer
import java.nio.ByteOrder

class PingTest {

    @Test
    fun writesSeventeenBytesWithLevel() {
        val buf = ByteBuffer.allocate(PING_SIZE).order(ByteOrder.LITTLE_ENDIAN)
        writePing(buf, seq = 5, timestampUs = 1234L, level = 1)

        assertEquals(17, buf.position())
        val b = buf.array()
        assertEquals(listOf<Byte>(-1, -1, -1, -1), b.slice(0..3))
        val read = ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN)
        assertEquals(5, read.getInt(4))
        assertEquals(1234L, read.getLong(8))
        assertEquals(1.toByte(), b[16])
    }

    @Test
    fun clampsLevel() {
        val buf = ByteBuffer.allocate(PING_SIZE).order(ByteOrder.LITTLE_ENDIAN)
        writePing(buf, seq = 1, timestampUs = 0L, level = 9)
        assertEquals(3.toByte(), buf.array()[16])
    }
}
