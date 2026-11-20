package com.f1controller.network

import java.nio.ByteBuffer

/** One track's Time Trial PBs; times in ms, 0 = none. S1/S2 0 = not recorded; S3 = lap − S1 − S2. */
data class TrackPb(
    val trackId: Int,
    val dryMs: Long, val dryS1: Int, val dryS2: Int,
    val wetMs: Long, val wetS1: Int, val wetS2: Int,
)

/**
 * Decodes a PB list packet body; [bb] is little-endian and positioned just after the
 * 0xFFFFFFFB marker: [count:u8] then per track [trackId:i8][dryMs:u32][dryS1:u16][dryS2:u16]
 * [wetMs:u32][wetS1:u16][wetS2:u16]. Layout: PacketParser.cs (WritePbList). Null if cut short.
 */
fun decodePbList(bb: ByteBuffer): List<TrackPb>? {
    if (bb.remaining() < 1) return null
    val count = bb.get().toInt() and 0xFF
    if (bb.remaining() < count * 17) return null
    fun u16() = bb.short.toInt() and 0xFFFF
    fun u32() = bb.int.toLong() and 0xFFFFFFFFL
    return List(count) {
        val id = bb.get().toInt()
        TrackPb(id, u32(), u16(), u16(), u32(), u16(), u16())
    }
}
