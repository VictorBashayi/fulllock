package com.f1controller.ui.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.ui.PedalSettings
import com.f1controller.ui.theme.Palette

@Composable
fun RowScope.LayoutPage(pedalSettings: PedalSettings) {
    val (left, right) = pedalSlots(pedalSettings.swapPedals)

    PreviewCard {
        // A tiny driving screen: pedals at the edges, wheel and buttons in between
        Row(
            Modifier
                .size(156.dp, 76.dp)
                .background(Brush.radialGradient(listOf(Palette.BgCenter, Palette.BgEdge)), RoundedCornerShape(12.dp))
                .border(1.dp, Palette.PanelBorder, RoundedCornerShape(12.dp))
                .padding(7.dp),
            verticalAlignment = Alignment.Bottom,
            horizontalArrangement = Arrangement.spacedBy(6.dp)
        ) {
            MiniPedal(left)
            Row(Modifier.weight(1f).fillMaxHeight(), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.Center) {
                Box(Modifier.size(14.dp).border(2.dp, Palette.Yellow, CircleShape))
                Spacer(Modifier.width(6.dp))
                Box(Modifier.size(20.dp, 12.dp).border(2.dp, Palette.PanelBorder, RoundedCornerShape(3.dp)))
            }
            MiniPedal(right)
        }
        Text(
            if (pedalSettings.swapPedals) "Throttle left, brake right" else "Brake left, throttle right",
            fontFamily = FontFamily.Monospace, fontSize = 12.sp, color = Palette.TextDim,
            textAlign = androidx.compose.ui.text.style.TextAlign.Center,
        )
    }

    SettingsGroup(Modifier.weight(1f).align(Alignment.Top)) {
        SettingsRow("Swap sides", "Throttle on the left, brake on the right", divider = false) {
            PaletteSwitch(pedalSettings.swapPedals) { pedalSettings.swapPedals = it }
        }
    }
}

@Composable
private fun MiniPedal(pedal: Pedal) {
    Box(
        Modifier
            .size(16.dp, 46.dp)
            .background((if (pedal == Pedal.THROTTLE) Palette.Cyan else Palette.Red).copy(alpha = 0.85f), RoundedCornerShape(5.dp))
    )
}
