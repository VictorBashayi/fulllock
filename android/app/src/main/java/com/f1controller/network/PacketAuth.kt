package com.f1controller.network

import java.security.Key
import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.PBEKeySpec
import javax.crypto.spec.SecretKeySpec

/** Mirrors the receiver's PacketAuth.cs: trailer = ctr:u32 LE ‖ HMAC-SHA256(dir ‖ ctx ‖ payload ‖ ctr)[0..8]. */
class PacketAuth(private val key: Key) {
    // Mac isn't thread-safe; one per thread.
    private val mac = object : ThreadLocal<Mac>() {
        override fun initialValue(): Mac = Mac.getInstance("HmacSHA256").also { it.init(key) }
    }

    data class Opened(val payloadLen: Int, val ctr: Long)

    private fun tag(dir: Byte, ctx: ByteArray, data: ByteArray, payloadLen: Int, ctrOffset: Int): ByteArray {
        val m = mac.get()!!
        m.reset()
        m.update(dir)
        m.update(ctx)
        m.update(data, 0, payloadLen)
        m.update(data, ctrOffset, CTR_SIZE)
        return m.doFinal()
    }

    /** Writes payload[0 until len] ‖ ctr ‖ tag into out; returns len + TRAILER_SIZE. */
    fun seal(dir: Byte, ctx: ByteArray, payload: ByteArray, len: Int, ctr: Int, out: ByteArray): Int {
        require(ctx.isEmpty() || ctx.size == CONTEXT_SIZE) { "ctx must be 0 or 16 bytes" }
        require(len in 0..payload.size && out.size >= len + TRAILER_SIZE) { "bad length or out too small" }
        System.arraycopy(payload, 0, out, 0, len)
        for (i in 0 until CTR_SIZE) out[len + i] = (ctr ushr (8 * i)).toByte()
        System.arraycopy(tag(dir, ctx, out, len, len), 0, out, len + CTR_SIZE, TAG_SIZE)
        return len + TRAILER_SIZE
    }

    /** Null if too short or tag mismatch; never throws on malformed input. */
    fun open(dir: Byte, ctx: ByteArray, data: ByteArray, len: Int): Opened? {
        if (len < TRAILER_SIZE || len > data.size || (ctx.isNotEmpty() && ctx.size != CONTEXT_SIZE)) return null
        val n = len - TRAILER_SIZE
        val expected = tag(dir, ctx, data, n, n)
        val got = data.copyOfRange(n + CTR_SIZE, n + TRAILER_SIZE)
        if (!MessageDigest.isEqual(expected.copyOf(TAG_SIZE), got)) return null
        var ctr = 0L
        for (i in 0 until CTR_SIZE) ctr = ctr or ((data[n + i].toLong() and 0xFF) shl (8 * i))
        return Opened(n, ctr)
    }

    companion object {
        const val TAG_SIZE = 8
        const val CTR_SIZE = 4
        const val TRAILER_SIZE = 12
        const val CONTEXT_SIZE = 16
        const val DIR_PHONE_TO_PC: Byte = 1
        const val DIR_PC_TO_PHONE: Byte = 2

        fun deriveKey(normalizedCode: String): ByteArray =
            SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256").generateSecret(
                PBEKeySpec(normalizedCode.toCharArray(), "FullLock-pair-v1".toByteArray(Charsets.US_ASCII), 200_000, 256)
            ).encoded

        fun keyFromRaw(raw: ByteArray): Key = SecretKeySpec(raw, "HmacSHA256")
    }
}
