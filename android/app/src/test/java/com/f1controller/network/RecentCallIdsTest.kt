package com.f1controller.network

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class RecentCallIdsTest {

    @Test
    fun repeatsWithinTheWindowAreDropped() {
        val ids = RecentCallIds(windowMs = 2000)
        assertFalse(ids.isRepeat(1, nowMs = 1000))
        assertTrue(ids.isRepeat(1, nowMs = 1030))
        assertTrue(ids.isRepeat(1, nowMs = 1060))
        assertFalse(ids.isRepeat(2, nowMs = 1060))
    }

    @Test
    fun sameIdLaterIsANewCall() {
        // The receiver restarted and its IDs began again at 1
        val ids = RecentCallIds(windowMs = 2000)
        assertFalse(ids.isRepeat(1, nowMs = 1000))
        assertFalse(ids.isRepeat(1, nowMs = 60_000))
    }
}
