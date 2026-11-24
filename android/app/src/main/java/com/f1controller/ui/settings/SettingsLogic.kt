package com.f1controller.ui.settings

import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Headset
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.SettingsInputAntenna
import androidx.compose.material.icons.filled.SportsMotorsports
import androidx.compose.material.icons.filled.SwapHoriz
import androidx.compose.material.icons.filled.Tune
import androidx.compose.material.icons.filled.VerticalAlignBottom
import androidx.compose.ui.graphics.vector.ImageVector

enum class SettingsPage(val label: String, val icon: ImageVector) {
    CONNECTION("Connection", Icons.Default.SettingsInputAntenna),
    STEERING("Steering", Icons.Default.SportsMotorsports),
    PEDALS("Pedals", Icons.Default.VerticalAlignBottom),
    ENGINEER("Engineer", Icons.Default.Headset),
    LAYOUT("Layout", Icons.Default.SwapHoriz),
    ADVANCED("Advanced", Icons.Default.Tune),
    ABOUT("About", Icons.Default.Info),
}

/** Unpaired users land on Connection; otherwise the page they last used this session, else Steering. */
fun startPage(paired: Boolean, last: SettingsPage?): SettingsPage =
    if (!paired) SettingsPage.CONNECTION else last ?: SettingsPage.STEERING

/** The Connection preview's one-line status. */
fun connectionSummary(paired: Boolean, connected: Boolean, rttMs: Float): String = when {
    !paired -> "Not paired"
    !connected -> "Looking for PC…"
    else -> "Connected · ${rttMs.toInt()} ms"
}

enum class Pedal { BRAKE, THROTTLE }

/** Which pedal sits in the (left, right) slot of the driving screen. */
fun pedalSlots(swap: Boolean): Pair<Pedal, Pedal> =
    if (swap) Pedal.THROTTLE to Pedal.BRAKE else Pedal.BRAKE to Pedal.THROTTLE

fun pedalModeLine(asButton: Boolean): String =
    if (asButton) "Button: press for full, let go for none" else "Slider: drag up for more pressure"

/** How much steering the game gets at [angleDeg] of phone turn, as the Steering preview shows it. */
fun steeringPercent(angleDeg: Float, lockDeg: Float, curve: Float): Int {
    val x = (kotlin.math.abs(angleDeg) / lockDeg).coerceIn(0f, 1f)
    return (com.f1controller.input.SteeringProcessor.applyCurve(x, curve) * 100f).toInt()
}

/**
 * First launch only. [seen] is null when the pref was never written: then someone already paired or
 * with saved settings is upgrading and skips it. Once first launch writes false, settings saved on
 * pause can't turn a killed first run into an "upgrade".
 */
fun shouldShowWalkthrough(seen: Boolean?, isPaired: Boolean, hasSavedSettings: Boolean): Boolean =
    seen?.not() ?: (!isPaired && !hasSavedSettings)

/** The pairing sheet opens by itself once, and never on top of the walkthrough. */
fun shouldAutoOpenPairing(
    status: com.f1controller.network.PairingStatus,
    alreadyOpened: Boolean,
    walkthroughShowing: Boolean,
): Boolean = status == com.f1controller.network.PairingStatus.NEEDS_PAIRING && !alreadyOpened && !walkthroughShowing
