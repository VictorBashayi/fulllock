package com.f1controller.network

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

private fun String.unhex() = ByteArray(length / 2) { substring(it * 2, it * 2 + 2).toInt(16).toByte() }
private fun ByteArray.hex() = joinToString("") { "%02x".format(it) }

class PacketAuthTest {
    companion object {
        val ctx = "01020304050607081112131415161718".unhex()
        val v1 = "010000000000000000000000004080000010".unhex()
        val rawKey = PacketAuth.deriveKey("K7QD9MXP2A")
        val auth = PacketAuth(PacketAuth.keyFromRaw(rawKey))
    }

    private fun seal(dir: Byte, c: ByteArray, p: ByteArray, ctr: Int): ByteArray {
        val d = ByteArray(p.size + PacketAuth.TRAILER_SIZE)
        assertEquals(d.size, auth.seal(dir, c, p, p.size, ctr, d))
        return d
    }

    @Test fun deriveKeyMatchesVector() =
        assertEquals("60a20da146d5592c8a6ad61091a0551ecf63753c657085b111d631a5dbdc99d9", rawKey.hex())

    @Test fun sealMatchesVector1() = assertEquals(
        "010000000000000000000000004080000010" + "07000000" + "f363ac24a73c7d68",
        seal(1, ctx, v1, 7).hex())

    @Test fun sealMatchesVector2() {
        val p = "ffffffff010000000000000000000000021112131415161718"
        assertEquals(p + "01000000" + "5826a84a01af4962", seal(1, ByteArray(0), p.unhex(), 1).hex())
    }

    @Test fun sealMatchesVector3() = assertEquals(
        "feffffff033200000000000000000000" + "05000000" + "1338ee307bedb447",
        seal(2, ctx, "feffffff033200000000000000000000".unhex(), 5).hex())

    @Test(expected = IllegalArgumentException::class)
    fun sealRejectsBadCtx() { seal(1, ByteArray(5), v1, 1) }

    @Test fun openRoundTrips() {
        val s = seal(1, ctx, v1, 7)
        val o = auth.open(1, ctx, s, s.size)
        assertNotNull(o)
        assertEquals(v1.size, o!!.payloadLen)
        assertEquals(7L, o.ctr)
        assertArrayEquals(v1, s.copyOf(o.payloadLen))
    }

    @Test fun openReturnsUnsignedCtr() {
        val s = seal(1, ctx, v1, -1)
        assertEquals(0xFFFFFFFFL, auth.open(1, ctx, s, s.size)!!.ctr)
    }

    @Test fun openRejectsAnySingleBitFlip() {
        val s = seal(1, ctx, v1, 7)
        for (i in 0 until s.size * 8) {
            val c = s.copyOf()
            c[i / 8] = (c[i / 8].toInt() xor (1 shl (i % 8))).toByte()
            assertNull("bit $i", auth.open(1, ctx, c, c.size))
        }
    }

    @Test fun openRejectsWrongDirAndCtx() {
        val s = seal(1, ctx, v1, 7)
        assertNull(auth.open(2, ctx, s, s.size))
        assertNull(auth.open(1, ByteArray(0), s, s.size))
        assertNull(auth.open(1, ByteArray(5), s, s.size))
    }

    @Test fun openRejectsShort() {
        for (n in 0 until PacketAuth.TRAILER_SIZE) assertNull(auth.open(1, ctx, ByteArray(n), n))
        assertFalse(auth.open(1, ctx, ByteArray(20), 99) != null)
    }
}
