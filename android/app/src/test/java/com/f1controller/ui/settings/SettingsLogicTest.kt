package com.f1controller.ui.settings

import org.junit.Assert.assertEquals
import org.junit.Test

class SettingsLogicTest {
    @Test
    fun startPageUnpairedIsConnection() {
        assertEquals(SettingsPage.CONNECTION, startPage(false, SettingsPage.ENGINEER))
    }

    @Test
    fun startPageKeepsLastPage() {
        assertEquals(SettingsPage.ADVANCED, startPage(true, SettingsPage.ADVANCED))
    }

    @Test
    fun connectionSummaryStates() {
        assertEquals("Not paired", connectionSummary(paired = false, connected = false, rttMs = 0f))
        assertEquals("Looking for PC…", connectionSummary(paired = true, connected = false, rttMs = 0f))
        assertEquals("Connected · 3 ms", connectionSummary(paired = true, connected = true, rttMs = 3.4f))
    }

    @Test
    fun pedalSlotsDefault() {
        assertEquals(Pedal.BRAKE to Pedal.THROTTLE, pedalSlots(false))
    }

    @Test
    fun pedalSlotsSwapped() {
        assertEquals(Pedal.THROTTLE to Pedal.BRAKE, pedalSlots(true))
    }

    @Test
    fun pedalModeLines() {
        assertEquals("Slider: drag up for more pressure", pedalModeLine(false))
        assertEquals("Button: press for full, let go for none", pedalModeLine(true))
    }

    @Test
    fun steeringPercentLinearAndClamped() {
        assertEquals(50, steeringPercent(angleDeg = -45f, lockDeg = 90f, curve = 0f))
        assertEquals(100, steeringPercent(angleDeg = 200f, lockDeg = 90f, curve = 0f))
    }

    @Test
    fun steeringPercentFollowsCurve() {
        val expected = (com.f1controller.input.SteeringProcessor.applyCurve(0.25f, 0.5f) * 100f).toInt()
        assertEquals(expected, steeringPercent(angleDeg = 22.5f, lockDeg = 90f, curve = 0.5f))
    }

    @Test
    fun walkthroughFreshInstall() {
        assertEquals(true, shouldShowWalkthrough(seen = false, isPaired = false, hasSavedSettings = false))
    }

    @Test
    fun walkthroughOnUpgradeIsSkipped() {
        assertEquals(false, shouldShowWalkthrough(seen = false, isPaired = true, hasSavedSettings = false))
        assertEquals(false, shouldShowWalkthrough(seen = false, isPaired = false, hasSavedSettings = true))
    }

    @Test
    fun walkthroughSeen() {
        assertEquals(false, shouldShowWalkthrough(seen = true, isPaired = false, hasSavedSettings = false))
    }

    @Test
    fun autoPairWaitsForWalkthrough() {
        val np = com.f1controller.network.PairingStatus.NEEDS_PAIRING
        assertEquals(false, shouldAutoOpenPairing(np, alreadyOpened = false, walkthroughShowing = true))
        assertEquals(true, shouldAutoOpenPairing(np, alreadyOpened = false, walkthroughShowing = false))
    }

    @Test
    fun autoPairOnlyOnce() {
        val np = com.f1controller.network.PairingStatus.NEEDS_PAIRING
        assertEquals(false, shouldAutoOpenPairing(np, alreadyOpened = true, walkthroughShowing = false))
    }

    @Test
    fun startPageDefaultPaired() {
        assertEquals(SettingsPage.STEERING, startPage(true, null))
    }
}
