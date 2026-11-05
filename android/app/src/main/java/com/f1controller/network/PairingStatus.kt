package com.f1controller.network

enum class PairingStatus { LOOKING, NEEDS_PAIRING, CODE_MISMATCH, CODE_CHANGED, PAIRED }

/** Ages in ms since the last beacon / valid pong, null = never. Rules: spec §4 "Phone". */
fun pairingStatus(
    hasKey: Boolean,
    pairingAttempt: Boolean,
    beaconAgeMs: Long?,
    validPongAgeMs: Long?,
    everConnected: Boolean,
): PairingStatus {
    val beacon = beaconAgeMs != null && beaconAgeMs <= 3000
    return when {
        !hasKey -> if (beacon) PairingStatus.NEEDS_PAIRING else PairingStatus.LOOKING
        validPongAgeMs != null && validPongAgeMs <= 2000 -> PairingStatus.PAIRED
        pairingAttempt && beacon -> PairingStatus.CODE_MISMATCH
        everConnected && beacon && (validPongAgeMs == null || validPongAgeMs > 3000) -> PairingStatus.CODE_CHANGED
        else -> PairingStatus.LOOKING
    }
}
