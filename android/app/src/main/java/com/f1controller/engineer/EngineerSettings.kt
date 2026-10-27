package com.f1controller.engineer

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/** Chattiness levels, indexed by [EngineerSettings.level]. */
val ENGINEER_LEVEL_NAMES = listOf("Off", "Quiet", "Balanced", "Talkative")

/**
 * How the race engineer reaches the driver. Compose state so Settings and the
 * radio react immediately.
 */
class EngineerSettings {
    var level by mutableIntStateOf(2)   // Balanced
    var voice by mutableStateOf(true)
    var vibrate by mutableStateOf(true)
}

/**
 * Whether a call of [priority] (0 chatter … 3 critical) plays at chattiness [level]:
 * Off plays nothing, Quiet important and critical, Balanced info and up, Talkative all.
 */
fun shouldPlay(level: Int, priority: Int): Boolean = level > 0 && priority >= 3 - level

/**
 * Whether a new call of [priority] should cut off the speech in progress ([inFlight] =
 * priorities still queued or speaking). Critical calls interrupt lesser ones but queue
 * behind another critical call, so a burst (e.g. several parts damaged at once) is all heard.
 */
fun shouldInterrupt(priority: Int, inFlight: Collection<Int>): Boolean =
    priority >= 3 && inFlight.none { it >= 3 }
