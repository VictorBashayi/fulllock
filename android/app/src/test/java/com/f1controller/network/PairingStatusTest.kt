package com.f1controller.network

import com.f1controller.network.PairingStatus.*
import org.junit.Assert.assertEquals
import org.junit.Test

class PairingStatusTest {
    private fun s(hasKey: Boolean = true, attempt: Boolean = false, beacon: Long? = null, pong: Long? = null, ever: Boolean = false) =
        pairingStatus(hasKey, attempt, beacon, pong, ever)

    @Test fun noKeyNoBeaconIsLooking() = assertEquals(LOOKING, s(hasKey = false))
    @Test fun noKeyStaleBeaconIsLooking() = assertEquals(LOOKING, s(hasKey = false, beacon = 3001))
    @Test fun noKeyBeaconNeedsPairing() = assertEquals(NEEDS_PAIRING, s(hasKey = false, beacon = 3000))
    @Test fun recentPongIsPaired() = assertEquals(PAIRED, s(pong = 2000))
    @Test fun attemptWithBeaconNoPongIsMismatch() = assertEquals(CODE_MISMATCH, s(attempt = true, beacon = 100, pong = 2001))
    @Test fun everConnectedBeaconNoPongIsChanged() = assertEquals(CODE_CHANGED, s(ever = true, beacon = 100))
    @Test fun everConnectedStalePongIsChanged() = assertEquals(CODE_CHANGED, s(ever = true, beacon = 100, pong = 3001))
    @Test fun everConnectedPongWithinGraceIsLooking() = assertEquals(LOOKING, s(ever = true, beacon = 100, pong = 2500))
    @Test fun keyNoBeaconNoPongIsLooking() = assertEquals(LOOKING, s(ever = true))
    @Test fun pongWinsOverBeacon() = assertEquals(PAIRED, s(attempt = true, ever = true, beacon = 0, pong = 0))
}
