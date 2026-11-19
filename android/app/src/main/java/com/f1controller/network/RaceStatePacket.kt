package com.f1controller.network

import java.nio.ByteBuffer

/**
 * Decodes a race state packet body; [bb] is little-endian and positioned just after the
 * 0xFFFFFFFD marker, [n] is the authenticated packet length. Layout: PacketParser.cs
 * (WriteRaceState). Bytes 52 / 54 / 58 (track id, Time Trial dry and wet PB) exist only when
 * the receiver sent the 62-byte version.
 */
fun parseRaceState(bb: ByteBuffer, n: Int): UdpTransport.RaceState {
    fun u8() = bb.get().toInt() and 0xFF
    val position = u8()
    val totalLaps = u8()
    val currentLap = u8()
    val sector = u8()
    val lapInvalid = u8() != 0
    val compound = u8()
    val tyreAge = u8()
    val flags = u8()
    val currentLapMs = bb.int.toLong() and 0xFFFFFFFFL
    val lastLapMs = bb.int.toLong() and 0xFFFFFFFFL
    val bestLapMs = bb.int.toLong() and 0xFFFFFFFFL
    val delta = bb.int
    val s1 = bb.short.toInt() and 0xFFFF
    val s2 = bb.short.toInt() and 0xFFFF
    val s1Status = u8()
    val s2Status = u8()
    val fuel = bb.short / 100f
    val gapAhead = bb.int
    val gapBehind = bb.int
    val wear = IntArray(4) { u8() }
    val temp = IntArray(4) { u8() }

    return UdpTransport.RaceState(
        position, totalLaps, currentLap, sector, lapInvalid, compound, tyreAge,
        currentLapMs, lastLapMs, bestLapMs,
        deltaMs = if (flags and 1 != 0) delta else null,
        s1Ms = s1, s2Ms = s2, s1Status = s1Status, s2Status = s2Status,
        fuelLaps = fuel,
        gapAheadMs = if (flags and 2 != 0) gapAhead else null,
        gapBehindMs = if (flags and 4 != 0) gapBehind else null,
        tyreWear = wear, tyreTemp = temp,
        trackId = if (n >= 62) bb.get(52).toInt() else -1,
        ttDryMs = if (n >= 62) bb.getInt(54).toLong() and 0xFFFFFFFFL else 0L,
        ttWetMs = if (n >= 62) bb.getInt(58).toLong() and 0xFFFFFFFFL else 0L,
    )
}
