package com.f1controller.network

import com.f1controller.network.PairingStatus.*
import org.junit.Assert.assertEquals
import org.junit.Test

class PairingStatusTest {
    private fun s(hasKey: Boolean = true, attempt: Boolean = false, beacon: Long? = null, pong: Long? = null, since: Long? = null) =
        pairingStatus(hasKey, attempt, beacon, pong, since)

    @Test fun noKeyNoBeaconIsLooking() = assertEquals(LOOKING, s(hasKey = false))
    @Test fun noKeyStaleBeaconIsLooking() = assertEquals(LOOKING, s(hasKey = false, beacon = 3001))
    @Test fun noKeyBeaconNeedsPairing() = assertEquals(NEEDS_PAIRING, s(hasKey = false, beacon = 3000))
    @Test fun recentPongIsPaired() = assertEquals(PAIRED, s(pong = 2000))
    @Test fun attemptWithBeaconNoPongIsMismatch() = assertEquals(CODE_MISMATCH, s(attempt = true, beacon = 100, pong = 2001))
    @Test fun storedKeyNoPongAfterGraceIsCodeChanged() = assertEquals(CODE_CHANGED, s(beacon = 100, since = 3001))
    @Test fun storedKeyWithinGraceIsLooking() = assertEquals(LOOKING, s(beacon = 100, since = 3000))
    @Test fun storedKeyNotConnectingIsLooking() = assertEquals(LOOKING, s(beacon = 100))
    @Test fun stalePongIsChanged() = assertEquals(CODE_CHANGED, s(beacon = 100, pong = 3001, since = 60_000))
    @Test fun pongWithinGraceIsLooking() = assertEquals(LOOKING, s(beacon = 100, pong = 2500, since = 60_000))
    @Test fun keyNoBeaconNoPongIsLooking() = assertEquals(LOOKING, s(since = 60_000))
    @Test fun pongWinsOverBeacon() = assertEquals(PAIRED, s(attempt = true, beacon = 0, pong = 0, since = 60_000))
}
