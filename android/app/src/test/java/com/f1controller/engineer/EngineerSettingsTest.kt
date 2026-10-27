package com.f1controller.engineer

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class EngineerSettingsTest {

    @Test
    fun levels() {
        assertFalse(shouldPlay(0, 3))                                // Off: nothing
        assertTrue(shouldPlay(1, 2)); assertFalse(shouldPlay(1, 1))  // Quiet: important+
        assertTrue(shouldPlay(2, 1)); assertFalse(shouldPlay(2, 0))  // Balanced: info+
        assertTrue(shouldPlay(3, 0))                                 // Talkative: everything
    }

    @Test
    fun levelNames() {
        assertEquals(listOf("Off", "Quiet", "Balanced", "Talkative"), ENGINEER_LEVEL_NAMES)
    }
}
