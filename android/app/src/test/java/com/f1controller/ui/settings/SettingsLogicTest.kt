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
    fun startPageDefaultPaired() {
        assertEquals(SettingsPage.STEERING, startPage(true, null))
    }
}
