package com.f1controller.network

/** Mirrors the receiver's PairingCode.cs: 10 chars from [ALPHABET], shown as XXXX-XXXX-XX. */
object PairingCode {
    const val ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
    const val LENGTH = 10

    /** Uppercases, drops spaces/dashes, maps I/L to 1 and O to 0; null unless exactly [LENGTH] valid chars. */
    fun normalize(input: String?): String? {
        if (input == null) return null
        val sb = StringBuilder(LENGTH)
        for (raw in input) {
            if (raw.code > 127) return null // keep ASCII-only, like the receiver
            val c = if (raw in 'a'..'z') raw - 32 else raw
            if (c == ' ' || c == '-') continue
            val m = when (c) { 'I', 'L' -> '1'; 'O' -> '0'; else -> c }
            if (m !in ALPHABET) return null
            sb.append(m)
        }
        return if (sb.length == LENGTH) sb.toString() else null
    }

    /** Groups of 4-4-2; accepts partial input (dashes only between non-empty groups). */
    fun format(normalized: String): String =
        listOf(0..4, 4..8, 8..10).map { normalized.substring(minOf(it.first, normalized.length), minOf(it.last, normalized.length)) }
            .filter { it.isNotEmpty() }.joinToString("-")

    /** As-you-type: keeps valid chars (case/lookalikes mapped like [normalize]), drops the rest, caps at [LENGTH]. */
    fun clean(input: String): String = buildString {
        for (raw in input) {
            val c = raw.uppercaseChar().let { when (it) { 'I', 'L' -> '1'; 'O' -> '0'; else -> it } }
            if (c in ALPHABET && length < LENGTH) append(c)
        }
    }

    /** Code from "fulllock://pair?code=…" (scheme/host case-insensitive), else null. */
    fun fromQrText(text: String): String? {
        val prefix = "fulllock://pair?"
        if (!text.startsWith(prefix, ignoreCase = true)) return null
        val code = text.substring(prefix.length).substringBefore('#').split('&')
            .firstOrNull { it.startsWith("code=") }?.substring(5)
        return normalize(code)
    }
}
