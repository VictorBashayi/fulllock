package com.f1controller.network

import org.junit.Assert.assertEquals
import org.junit.Test
import java.nio.ByteBuffer
import java.nio.ByteOrder

class RaceStatePbTest {

    private fun packet(size: Int): ByteBuffer =
        ByteBuffer.allocate(size).order(ByteOrder.LITTLE_ENDIAN)

    private fun afterMarker(bb: ByteBuffer): ByteBuffer = bb.apply { position(4) }

    /** Same layout as the receiver's RacePacketTests.RacePacketCarriesTrackAndPbs. */
    @Test
    fun readsTrackAndPbsFrom62ByteBuffer() {
        val bb = packet(62)
        bb.put(52, 5)
        bb.putInt(54, 78123)
        bb.putInt(58, 91234)
        val race = parseRaceState(afterMarker(bb), 62)
        assertEquals(5, race.trackId)
        assertEquals(78123L, race.ttDryMs)
        assertEquals(91234L, race.ttWetMs)
    }

    @Test
    fun oldReceiverPacketHasNoTrackOrPbs() {
        val race = parseRaceState(afterMarker(packet(54)), 54)
        assertEquals(-1, race.trackId)
        assertEquals(0L, race.ttDryMs)
        assertEquals(0L, race.ttWetMs)
    }

    @Test
    fun unknownTrackByteIsMinusOne() {
        val bb = packet(62)
        bb.put(52, -1)
        assertEquals(-1, parseRaceState(afterMarker(bb), 62).trackId)
    }

    @Test
    fun trackNames() {
        assertEquals("Monaco", TrackNames.short(5))
        assertEquals("", TrackNames.short(-1))
        assertEquals("Track 99", TrackNames.short(99))
    }
}
