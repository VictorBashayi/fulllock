package com.f1controller.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.ui.settings.OutlinePill
import com.f1controller.ui.settings.PrimaryPill
import com.f1controller.ui.theme.Condensed
import com.f1controller.ui.theme.Palette

private class Step(val title: String, val line: String, val draw: DrawScope.() -> Unit)

private val steps = listOf(
    Step("Pair", "Scan the code on the FullLock PC window") { drawPair() },
    Step("Hold", "Hold the phone like a wheel, then set the centre") { drawHold() },
    Step("Drive", "Pedals at the edges, buttons under your thumbs") { drawDrive() },
)

/** Three-step "How it works": shown once on first launch and from Settings > About. */
@Composable
fun Walkthrough(onDone: () -> Unit) {
    var i by remember { mutableIntStateOf(0) }
    // The NavHost is gone while this shows, so Back would otherwise leave the app
    androidx.activity.compose.BackHandler(onBack = onDone)
    val step = steps[i]
    Row(
        Modifier
            .fillMaxSize()
            .background(Brush.radialGradient(listOf(Palette.BgCenter, Palette.BgEdge)))
            .systemBarsPadding()
            .padding(32.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(40.dp)
    ) {
        Canvas(Modifier.weight(1f).fillMaxHeight(0.8f)) { step.draw(this) }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("${i + 1} of ${steps.size}", fontSize = 13.sp, color = Palette.TextDim)
            Text(step.title, fontFamily = Condensed, fontSize = 28.sp, color = Palette.Text)
            Text(step.line, fontSize = 16.sp, color = Palette.TextDim)
            Spacer(Modifier.height(12.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                if (i < steps.lastIndex) OutlinePill("Skip", onDone)
                PrimaryPill(if (i < steps.lastIndex) "Next" else "Done", onClick = { if (i < steps.lastIndex) i++ else onDone() })
            }
        }
    }
}

private fun DrawScope.drawPair() {
    val c = center
    val stroke = Stroke(4f)
    // PC screen with a code on it, phone pointing at it
    drawRoundRect(Palette.Text, Offset(c.x - 20f, c.y - 110f), Size(200f, 130f), CornerRadius(10f), style = stroke)
    for (r in 0..3) for (k in 0..3) if ((r + k) % 2 == 0)
        drawRect(Palette.Cyan, Offset(c.x + 50f + k * 15f, c.y - 80f + r * 15f), Size(13f, 13f))
    drawRoundRect(Palette.Text, Offset(c.x - 190f, c.y - 20f), Size(70f, 130f), CornerRadius(12f), style = stroke)
    drawLine(Palette.Cyan, Offset(c.x - 110f, c.y + 20f), Offset(c.x - 30f, c.y - 30f), 3f, pathEffect = PathEffect.dashPathEffect(floatArrayOf(10f, 10f)))
}

private fun DrawScope.drawHold() {
    val c = center
    drawArc(Palette.PanelBorder, 200f, 140f, false, Offset(c.x - 170f, c.y - 120f), Size(340f, 340f), style = Stroke(4f, pathEffect = PathEffect.dashPathEffect(floatArrayOf(4f, 14f))))
    rotate(-14f, c) {
        drawRoundRect(Palette.Text, Offset(c.x - 130f, c.y - 40f), Size(260f, 120f), CornerRadius(18f), style = Stroke(4f))
        drawCircle(Palette.Cyan, 8f, Offset(c.x, c.y - 70f))
    }
}

private fun DrawScope.drawDrive() {
    val c = center
    drawRoundRect(Palette.PanelBorder, Offset(c.x - 200f, c.y - 90f), Size(400f, 180f), CornerRadius(18f), style = Stroke(3f))
    drawRoundRect(Palette.Red, Offset(c.x - 185f, c.y - 20f), Size(40f, 95f), CornerRadius(10f))
    drawRoundRect(Palette.Cyan, Offset(c.x + 145f, c.y - 20f), Size(40f, 95f), CornerRadius(10f))
    for ((dx, dy, col) in listOf(Triple(0f, -32f, Palette.Yellow), Triple(-32f, 0f, Palette.Blue), Triple(32f, 0f, Palette.Red), Triple(0f, 32f, Palette.Green)))
        drawCircle(col, 13f, Offset(c.x + 80f + dx, c.y + 10f + dy), style = Stroke(3f))
    drawRoundRect(Palette.Text, Offset(c.x - 110f, c.y - 5f), Size(60f, 40f), CornerRadius(8f), style = Stroke(3f))
}
