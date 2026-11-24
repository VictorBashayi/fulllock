package com.f1controller.ui.settings

import androidx.compose.foundation.layout.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.f1controller.ui.PedalSettings
import com.f1controller.ui.VerticalPedal
import com.f1controller.ui.theme.Palette

/** No transport parameter on purpose: touching the preview pedals never reaches the PC. */
@Composable
fun RowScope.PedalsPage(pedalSettings: PedalSettings) {
    var brake by remember { mutableFloatStateOf(0f) }
    var throttle by remember { mutableFloatStateOf(0f) }

    PreviewCard {
        Row(Modifier.fillMaxSize(), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            VerticalPedal("BRAKE", brake, { brake = it }, Palette.Red, pedalSettings.brakeAsButton, Modifier.weight(1f).fillMaxHeight())
            VerticalPedal("THROTTLE", throttle, { throttle = it }, Palette.Cyan, pedalSettings.throttleAsButton, Modifier.weight(1f).fillMaxHeight())
        }
    }

    SettingsGroup(Modifier.weight(1f).align(androidx.compose.ui.Alignment.Top)) {
        val modes = listOf("Slider", "Button")
        SettingsRow("Brake", pedalModeLine(pedalSettings.brakeAsButton)) {
            PaletteChips(modes, if (pedalSettings.brakeAsButton) 1 else 0) { pedalSettings.brakeAsButton = it == 1 }
        }
        SettingsRow("Throttle", pedalModeLine(pedalSettings.throttleAsButton), divider = false) {
            PaletteChips(modes, if (pedalSettings.throttleAsButton) 1 else 0) { pedalSettings.throttleAsButton = it == 1 }
        }
    }
}
