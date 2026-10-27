package com.f1controller.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.nio.ByteBuffer
import java.nio.ByteOrder

class RadioPacketTest {

    private fun afterMarker(bytes: ByteArray): ByteBuffer =
        ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).apply { position(4) }

    /** Same bytes as the receiver's RadioPacketTests.WritesHeaderAndText. */
    @Test
    fun decodesCall() {
        val bytes = byteArrayOf(
            0xFC.toByte(), -1, -1, -1, 0x34, 0x12, 2, 0, 3,
            'B'.code.toByte(), 'o'.code.toByte(), 'x'.code.toByte()
        )
        assertEquals(RadioCall(0x1234, 2, 0, "Box"), decodeRadioCall(afterMarker(bytes)))
    }

    @Test
    fun decodesUtf8Text() {
        val text = "Café".toByteArray(Charsets.UTF_8)
        val bytes = byteArrayOf(0xFC.toByte(), -1, -1, -1, 1, 0, 3, 7, text.size.toByte()) + text
        assertEquals(RadioCall(1, 3, 7, "Café"), decodeRadioCall(afterMarker(bytes)))
    }

    @Test
    fun truncatedPacketIsNull() {
        val bytes = byteArrayOf(0xFC.toByte(), -1, -1, -1, 1, 0, 2, 0, 10, 'a'.code.toByte(), 'b'.code.toByte(), 'c'.code.toByte())
        assertNull(decodeRadioCall(afterMarker(bytes)))
    }

    @Test
    fun headerTooShortIsNull() {
        val bytes = byteArrayOf(0xFC.toByte(), -1, -1, -1, 1, 0, 2)
        assertNull(decodeRadioCall(afterMarker(bytes)))
    }
}
