package com.f1controller.network

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Base64
import javax.crypto.KeyGenerator

class PairKeyWrapTest {
    private fun aes() = KeyGenerator.getInstance("AES").apply { init(256) }.generateKey()
    private val wrap = aes()
    private val raw = ByteArray(32) { (it * 7).toByte() }

    @Test fun roundTrips() = assertArrayEquals(raw, openPairKey(wrap, sealPairKey(wrap, raw)))

    @Test fun freshIvEachSeal() = assertNotEquals(sealPairKey(wrap, raw), sealPairKey(wrap, raw))

    @Test(expected = Exception::class)
    fun rejectsOtherWrapKey() { openPairKey(aes(), sealPairKey(wrap, raw)) }

    @Test fun rejectsAnyFlippedByte() {
        val b = Base64.getDecoder().decode(sealPairKey(wrap, raw))
        for (i in b.indices) {
            val c = b.copyOf().also { it[i] = (it[i].toInt() xor 1).toByte() }
            assertTrue("byte $i", runCatching { openPairKey(wrap, Base64.getEncoder().encodeToString(c)) }.isFailure)
        }
    }

    @Test(expected = IllegalArgumentException::class)
    fun rejectsTruncated() { openPairKey(wrap, sealPairKey(wrap, raw).dropLast(8)) }

    @Test(expected = IllegalArgumentException::class)
    fun sealRejectsWrongSize() { sealPairKey(wrap, ByteArray(16)) }
}
