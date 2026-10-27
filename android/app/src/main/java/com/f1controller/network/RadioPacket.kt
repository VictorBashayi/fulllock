package com.f1controller.network

import java.nio.ByteBuffer

/**
 * One race-engineer call from the receiver. Priority: 0 chatter, 1 info,
 * 2 important, 3 critical. Category: 0 tyres, 1 fuel, 2 gaps, 3 lap,
 * 4 flags, 5 weather, 6 damage, 7 other.
 */
data class RadioCall(val id: Int, val priority: Int, val category: Int, val text: String)

/**
 * Decodes a radio packet body; [bb] is little-endian and positioned just after
 * the 0xFFFFFFFC marker: [callId:u16][priority:u8][category:u8][textLen:u8][UTF-8 text].
 * Returns null if the packet is cut short.
 */
fun decodeRadioCall(bb: ByteBuffer): RadioCall? {
    if (bb.remaining() < 5) return null
    val id = bb.short.toInt() and 0xFFFF
    val priority = bb.get().toInt() and 0xFF
    val category = bb.get().toInt() and 0xFF
    val len = bb.get().toInt() and 0xFF
    if (bb.remaining() < len) return null
    val text = ByteArray(len).also { bb.get(it) }.toString(Charsets.UTF_8)
    return RadioCall(id, priority, category, text)
}
