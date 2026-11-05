package com.f1controller.network

import android.content.Context
import android.security.keystore.KeyProperties
import android.security.keystore.KeyProtection
import com.f1controller.MainActivity
import java.security.Key
import java.security.KeyStore
import javax.crypto.spec.SecretKeySpec

/** The pairing key lives in AndroidKeyStore (non-exportable); only HMAC sign/verify is possible. */
class PairingStore(private val context: Context) {
    private val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

    val isPaired: Boolean get() = ks.containsAlias(ALIAS)

    fun key(): Key? = ks.getKey(ALIAS, null)

    fun save(rawKey: ByteArray) {
        try {
            ks.setEntry(
                ALIAS,
                KeyStore.SecretKeyEntry(SecretKeySpec(rawKey, "HmacSHA256")),
                KeyProtection.Builder(KeyProperties.PURPOSE_SIGN or KeyProperties.PURPOSE_VERIFY).build(),
            )
        } finally {
            rawKey.fill(0)
        }
    }

    fun forget() {
        ks.deleteEntry(ALIAS)
        context.getSharedPreferences(MainActivity.PREFS_NAME, Context.MODE_PRIVATE).edit()
            .remove("server_ip").apply()
    }

    private companion object {
        const val ALIAS = "fulllock_pair"
    }
}
