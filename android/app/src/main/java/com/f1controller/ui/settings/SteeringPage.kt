package com.f1controller.ui.settings

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.input.SteeringProcessor
import com.f1controller.ui.SteeringWheel
import com.f1controller.ui.theme.Condensed
import com.f1controller.ui.theme.Palette
import kotlinx.coroutines.delay

@Composable
fun RowScope.SteeringPage(steeringProcessor: SteeringProcessor) {
    var lockAngle by remember { mutableFloatStateOf(steeringProcessor.lockAngleDeg) }
    var curve by remember { mutableFloatStateOf(steeringProcessor.curve) }
    var deadzone by remember { mutableFloatStateOf(steeringProcessor.deadzoneDeg) }
    // The angle isn't Compose state; sample it at ~30 Hz like the driving screen
    val angle by produceState(0f) { while (true) { value = steeringProcessor.currentAngleDeg; delay(33) } }

    PreviewCard {
        SteeringWheel(angle = angle, lockAngleDeg = lockAngle, revLevel = 0f, gearText = "–", modifier = Modifier.size(150.dp, 110.dp))
        Text("${angle.toInt()}°", fontFamily = Condensed, fontSize = 28.sp, color = Palette.Text)
        CurvePlot(curve, x = (kotlin.math.abs(angle) / lockAngle).coerceIn(0f, 1f))
        Text("${steeringPercent(angle, lockAngle, curve)}% steering", fontFamily = FontFamily.Monospace, fontSize = 12.sp, color = Palette.TextDim)
    }

    SettingsGroup(Modifier.weight(1f).verticalScroll(rememberScrollState())) {
        SettingsRow(
            "Lock angle", "How far you turn the phone for full lock",
            below = { PaletteSlider(lockAngle, 30f..180f) { lockAngle = it; steeringProcessor.lockAngleDeg = it } },
        ) { ValuePill("±${lockAngle.toInt()}°") }
        SettingsRow(
            "Centre smoothing", "Calmer on straights, same full lock",
            below = { PaletteSlider(curve, 0f..1f) { curve = it; steeringProcessor.curve = it } },
        ) { ValuePill("${(curve * 100).toInt()}%") }
        SettingsRow(
            "Deadzone", "Small wobbles near centre are ignored",
            below = { PaletteSlider(deadzone, 0f..10f) { deadzone = it; steeringProcessor.deadzoneDeg = it } },
        ) { ValuePill(String.format("%.1f°", deadzone)) }
        SettingsRow("Set centre", "Hold the phone straight, then tap") {
            PrimaryPill("Set", onClick = { steeringProcessor.calibrateCenter() })
        }
        SettingsRow("Reset filter & calibration", "Back to the default filter and centre", divider = false) {
            OutlinePill("Reset", onClick = { steeringProcessor.reset() })
        }
    }
}

/** The response curve over 0..1 with a dashed straight reference and a dot at today's position. */
@Composable
private fun CurvePlot(curve: Float, x: Float) {
    Canvas(Modifier.size(150.dp, 50.dp)) {
        val w = size.width; val h = size.height
        drawLine(Palette.PanelBorder, Offset(0f, h), Offset(w, 0f), 2f, pathEffect = PathEffect.dashPathEffect(floatArrayOf(6f, 6f)))
        val path = Path().apply {
            moveTo(0f, h)
            for (i in 1..40) { val t = i / 40f; lineTo(t * w, h - SteeringProcessor.applyCurve(t, curve) * h) }
        }
        drawPath(path, Palette.Cyan, style = Stroke(5f))
        drawCircle(Palette.Text, 8f, Offset(x * w, h - SteeringProcessor.applyCurve(x, curve) * h))
    }
}
