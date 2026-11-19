package com.f1controller.network

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyPermanentlyInvalidatedException
import android.security.keystore.KeyProperties
import com.f1controller.MainActivity
import java.security.Key
import java.security.KeyStore
import java.security.UnrecoverableKeyException
import java.util.Base64
import javax.crypto.AEADBadTagException
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * The 32-byte pairing key is stored in prefs encrypted by a non-exportable AndroidKeyStore
 * AES-GCM key. key() decrypts it once into a software HMAC key, so the 250 Hz seal path
 * never makes a keystore call.
 */
class PairingStore(context: Context) {
    private val prefs = context.getSharedPreferences(MainActivity.PREFS_NAME, Context.MODE_PRIVATE)

    val isPaired: Boolean get() = prefs.contains(PREF_KEY)

    /** Null when unpaired or the key can't be read; cleared too if it is permanently undecryptable (keystore reset, tampering). */
    fun key(): Key? {
        val packed = prefs.getString(PREF_KEY, null) ?: return null
        return try {
            val raw = openPairKey(wrapKey(), packed)
            try { SecretKeySpec(raw, "HmacSHA256") } finally { raw.fill(0) } // SecretKeySpec keeps its own copy
        } catch (e: Exception) {
            // Only a permanently unusable key unpairs; a transient keystore error just returns null for this run
            if (e is AEADBadTagException || e is IllegalArgumentException || e is UnrecoverableKeyException ||
                e is KeyPermanentlyInvalidatedException
            ) prefs.edit().remove(PREF_KEY).apply()
            null
        }
    }

    fun save(rawKey: ByteArray) {
        try {
            prefs.edit().putString(PREF_KEY, sealPairKey(wrapKey(), rawKey)).apply()
        } finally {
            rawKey.fill(0)
        }
    }

    /** The wrap key is kept: without the pref it protects nothing, and save() reuses it. */
    fun forget() {
        prefs.edit().remove(PREF_KEY).remove("server_ip").apply()
    }

    private fun wrapKey(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(WRAP_ALIAS, null) as SecretKey?)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(
                KeyGenParameterSpec.Builder(WRAP_ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setKeySize(256)
                    .build()
            )
        }.generateKey()
    }

    private companion object {
        const val WRAP_ALIAS = "fulllock_pair_wrap"
        const val PREF_KEY = "pair_key"
    }
}

private const val GCM_IV_SIZE = 12
private const val GCM_TAG_BITS = 128
private const val PAIR_KEY_SIZE = 32

/** Base64(IV ‖ AES-GCM(raw)). The cipher picks the IV (AndroidKeyStore requires that). */
internal fun sealPairKey(wrap: Key, raw: ByteArray): String {
    require(raw.size == PAIR_KEY_SIZE) { "pair key must be 32 bytes" }
    val c = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, wrap) }
    val ct = c.doFinal(raw)
    return Base64.getEncoder().encodeToString(c.iv + ct)
}

/** Inverse of [sealPairKey]; throws on wrong length, wrong wrap key or any tampering. Caller zeroes the result. */
internal fun openPairKey(wrap: Key, packed: String): ByteArray {
    val b = Base64.getDecoder().decode(packed)
    require(b.size == GCM_IV_SIZE + PAIR_KEY_SIZE + GCM_TAG_BITS / 8) { "bad stored pair key" }
    val c = Cipher.getInstance("AES/GCM/NoPadding")
    c.init(Cipher.DECRYPT_MODE, wrap, GCMParameterSpec(GCM_TAG_BITS, b, 0, GCM_IV_SIZE))
    return c.doFinal(b, GCM_IV_SIZE, b.size - GCM_IV_SIZE)
}
