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
