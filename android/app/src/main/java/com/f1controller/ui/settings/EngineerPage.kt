package com.f1controller.ui.settings

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.sp
import com.f1controller.engineer.ENGINEER_LEVEL_NAMES
import com.f1controller.engineer.EngineerRadio
import com.f1controller.engineer.EngineerSettings
import com.f1controller.network.RadioCall
import com.f1controller.ui.theme.Condensed
import com.f1controller.ui.theme.Palette

@Composable
fun RowScope.EngineerPage(engineerSettings: EngineerSettings, engineerRadio: EngineerRadio) {
    val last by engineerRadio.lastCall.collectAsState()
    val voiceAvailable by engineerRadio.voiceAvailable.collectAsState()

    PreviewCard {
        Text(
            last?.text ?: "No calls yet",
            fontFamily = Condensed, fontSize = 18.sp, textAlign = TextAlign.Center,
            color = if (last != null) Palette.Text else Palette.TextDim,
        )
        OutlinePill("Test call", onClick = {
            engineerRadio.play(RadioCall(-1, 2, 0, "Radio check, how do you hear me?"), force = true)
        })
    }

    SettingsGroup(Modifier.weight(1f).verticalScroll(rememberScrollState())) {
        SettingsRow(
            "How much the engineer talks", "Quiet: only things to act on. Talkative: sector colours too",
            below = { PaletteChips(ENGINEER_LEVEL_NAMES, engineerSettings.level) { engineerSettings.level = it } },
        )
        SettingsRow(
            "Voice",
            if (voiceAvailable) "Spoken calls" else "Text-to-speech isn't ready on this phone",
            lineColor = if (voiceAvailable) Palette.TextDim else Palette.Red,
        ) { PaletteSwitch(engineerSettings.voice) { engineerSettings.voice = it } }
        SettingsRow("Vibration", "Buzz on important calls", divider = false) {
            PaletteSwitch(engineerSettings.vibrate) { engineerSettings.vibrate = it }
        }
    }
}
