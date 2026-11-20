package com.f1controller.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.nio.ByteBuffer
import java.nio.ByteOrder

class PbListPacketTest {

    private fun entry(bb: ByteBuffer, id: Int, dry: Int, d1: Int, d2: Int, wet: Int, w1: Int, w2: Int) {
        bb.put(id.toByte()); bb.putInt(dry); bb.putShort(d1.toShort()); bb.putShort(d2.toShort())
        bb.putInt(wet); bb.putShort(w1.toShort()); bb.putShort(w2.toShort())
    }

    /** Same values as the receiver's RacePacketTests.PbListLayout; sectors above 32767 check unsigned reads. */
    @Test
    fun decodesEntries() {
        val bb = ByteBuffer.allocate(5 + 2 * 17).order(ByteOrder.LITTLE_ENDIAN)
        bb.putInt(0xFFFFFFFB.toInt()); bb.put(2)
        entry(bb, 3, 86839, 28100, 41200, 116657, 37900, 41200)
        entry(bb, 5, 72301, 0, 0, 0, 0, 0)
        bb.position(4)
        assertEquals(
            listOf(
                TrackPb(3, 86839, 28100, 41200, 116657, 37900, 41200),
                TrackPb(5, 72301, 0, 0, 0, 0, 0),
            ),
            decodePbList(bb),
        )
    }

    @Test
    fun truncatedListIsNull() {
        val bb = ByteBuffer.allocate(5 + 2 * 17).order(ByteOrder.LITTLE_ENDIAN)
        bb.putInt(0xFFFFFFFB.toInt()); bb.put(5)
        bb.position(4)
        assertNull(decodePbList(bb))
    }

    @Test
    fun emptyList() {
        val bb = ByteBuffer.allocate(5).order(ByteOrder.LITTLE_ENDIAN)
        bb.putInt(0xFFFFFFFB.toInt()); bb.put(0)
        bb.position(4)
        assertEquals(emptyList<TrackPb>(), decodePbList(bb))
    }
}
