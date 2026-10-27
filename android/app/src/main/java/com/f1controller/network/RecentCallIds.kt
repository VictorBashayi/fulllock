package com.f1controller.network

/**
 * Spots the repeats of a radio call: the receiver sends each call 3 times ~30 ms
 * apart with the same ID. Only an ID seen within [windowMs] counts as a repeat, so
 * a restarted receiver (whose IDs start again at 1) is never mistaken for one.
 * Not thread-safe; used from the receive thread only.
 */
class RecentCallIds(private val windowMs: Long = 2000) {
    private val seenAt = HashMap<Int, Long>()

    fun isRepeat(id: Int, nowMs: Long): Boolean {
        seenAt.entries.removeAll { nowMs - it.value > windowMs }
        if (id in seenAt) return true
        seenAt[id] = nowMs
        return false
    }
}
