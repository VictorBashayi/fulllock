package com.f1controller.network

enum class PairingStatus { LOOKING, NEEDS_PAIRING, CODE_MISMATCH, CODE_CHANGED, PAIRED }

/**
 * Ages in ms since the last beacon / valid pong / start of the current connect(), null = never/not running.
 * Rules: spec §4 "Phone". A stored key counts as "paired before", so the changed-code message also
 * shows after an app restart; the 3-s grace runs from the last valid pong, else from the connect start.
 */
fun pairingStatus(
    hasKey: Boolean,
    pairingAttempt: Boolean,
    beaconAgeMs: Long?,
    validPongAgeMs: Long?,
    connectAgeMs: Long?,
): PairingStatus {
    val beacon = beaconAgeMs != null && beaconAgeMs <= 3000
    val silentMs = validPongAgeMs ?: connectAgeMs
    return when {
        !hasKey -> if (beacon) PairingStatus.NEEDS_PAIRING else PairingStatus.LOOKING
        validPongAgeMs != null && validPongAgeMs <= 2000 -> PairingStatus.PAIRED
        pairingAttempt && beacon -> PairingStatus.CODE_MISMATCH
        beacon && silentMs != null && silentMs > 3000 -> PairingStatus.CODE_CHANGED
        else -> PairingStatus.LOOKING
    }
}
