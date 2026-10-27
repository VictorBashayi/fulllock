package com.f1controller.engineer

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class ReconnectGateTest {

    @Test
    fun firstReconnectIsAllowed() {
        assertTrue(ReconnectGate(10_000).tryAcquire(nowMs = 5_000))
    }

    @Test
    fun blocksUntilIntervalHasPassed() {
        val gate = ReconnectGate(10_000)
        assertTrue(gate.tryAcquire(nowMs = 50_000))
        assertFalse(gate.tryAcquire(nowMs = 51_000))
        assertFalse(gate.tryAcquire(nowMs = 59_999))
        assertTrue(gate.tryAcquire(nowMs = 60_000))
    }

    @Test
    fun refusedAttemptDoesNotRestartTheWait() {
        val gate = ReconnectGate(10_000)
        assertTrue(gate.tryAcquire(nowMs = 0))
        assertFalse(gate.tryAcquire(nowMs = 9_000))
        assertTrue(gate.tryAcquire(nowMs = 10_000))
    }
}
