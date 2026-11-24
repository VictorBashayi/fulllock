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
    fun startPageDefaultPaired() {
        assertEquals(SettingsPage.STEERING, startPage(true, null))
    }
}
