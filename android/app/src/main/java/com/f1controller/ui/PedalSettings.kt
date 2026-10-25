package com.f1controller.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/**
 * How each pedal responds to touch. Compose state so the controller
 * screen redraws as soon as it's changed in Settings.
 *
 * Slider: drag height = pressure (0-100%).
 * Button: press = 100%, release = 0%.
 */
class PedalSettings {
    var brakeAsButton by mutableStateOf(false)
    var throttleAsButton by mutableStateOf(false)
}
