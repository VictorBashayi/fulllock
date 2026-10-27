package com.f1controller.engineer

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class SpeechQueueTest {

    @Test
    fun criticalInterruptsLowerPriorities() {
        assertTrue(shouldInterrupt(3, listOf(1, 2)))
    }

    @Test
    fun criticalQueuesBehindAnotherCritical() {
        // Several damage calls at once: all must be heard, none cut off
        assertFalse(shouldInterrupt(3, listOf(3)))
        assertFalse(shouldInterrupt(3, listOf(1, 3)))
    }

    @Test
    fun nonCriticalNeverInterrupts() {
        assertFalse(shouldInterrupt(2, listOf(1)))
        assertFalse(shouldInterrupt(2, emptyList()))
    }
}
