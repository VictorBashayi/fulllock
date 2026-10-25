package com.f1controller.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.network.UdpTransport
import kotlin.math.abs

/*
 * Race dashboard pieces shown around and in place of the steering wheel:
 * the info strip under BACK/START, and the Car and Timing centre views.
 * They only read UdpTransport.RaceState (sent by the receiver for F1 2021).
 */

private val Purple = Color(0xFFA855F7)
private val Cold = Color(0xFF5B9BF0)
private val Mono = FontFamily.Monospace

/** "P4 · LAP 12/28 · Δ +0.214" */
@Composable
internal fun InfoStrip(race: UdpTransport.RaceState) {
    val shape = RoundedCornerShape(16.dp)
    Row(
        modifier = Modifier
            .clip(shape)
            .background(Color(0xFF0D1A22))
            .border(1.dp, Palette.Cyan.copy(alpha = 0.33f), shape)
            .padding(horizontal = 12.dp, vertical = 4.dp),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        val lap = if (race.totalLaps > 0) "LAP ${race.currentLap}/${race.totalLaps}" else "LAP ${race.currentLap}"
        StripText("P${race.position}")
        StripText("·", Palette.TextDim)
        StripText(lap)
        race.deltaMs?.let {
            StripText("·", Palette.TextDim)
            StripText("Δ ${formatDelta(it)}", deltaColor(it))
        }
    }
}

@Composable
private fun StripText(text: String, color: Color = Palette.Text) {
    Text(text, fontFamily = Mono, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, color = color, maxLines = 1)
}

/** Tyre temperature/wear per corner, compound and age, fuel margin, ERS. */
@Composable
internal fun CarView(race: UdpTransport.RaceState?, game: UdpTransport.GameTelemetry?) {
    if (race == null) return Waiting()
    Column(
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Card {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(horizontalArrangement = Arrangement.spacedBy(22.dp)) {
                    Tyre(race.tyreTemp[0], race.tyreWear[0], tyreOnRight = true)
                    Tyre(race.tyreTemp[1], race.tyreWear[1], tyreOnRight = false)
                }
                Row(horizontalArrangement = Arrangement.spacedBy(22.dp)) {
                    Tyre(race.tyreTemp[2], race.tyreWear[2], tyreOnRight = true)
                    Tyre(race.tyreTemp[3], race.tyreWear[3], tyreOnRight = false)
                }
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(14.dp), verticalAlignment = Alignment.Bottom) {
            val (letter, color) = compound(race.compound)
            Stat("TYRE") {
                Text(letter, fontFamily = Condensed, fontSize = 17.sp, color = color)
                Text(" · ${race.tyreAgeLaps} laps", fontFamily = Condensed, fontSize = 17.sp, color = Palette.Text)
            }
            Stat("FUEL") {
                val fuel = race.fuelLaps
                Text("%+.1f laps".format(fuel), fontFamily = Condensed, fontSize = 17.sp,
                    color = if (fuel < 0f) Palette.Red else Palette.Green)
            }
            game?.let { g ->
                Stat("ERS") {
                    Text("${g.ersPct}%", fontFamily = Condensed, fontSize = 17.sp, color = Palette.Yellow)
                }
            }
        }
    }
}

@Composable
private fun Tyre(tempC: Int, wearPct: Int, tyreOnRight: Boolean) {
    val color = tempColor(tempC)
    val readout = @Composable {
        Column(horizontalAlignment = if (tyreOnRight) Alignment.End else Alignment.Start) {
            Text("$tempC°", fontFamily = Mono, fontSize = 12.sp, color = color)
            Text("$wearPct%", fontFamily = Mono, fontSize = 12.sp, color = Palette.Text)
        }
    }
    val tyre = @Composable {
        Box(
            Modifier
                .size(width = 22.dp, height = 34.dp)
                .clip(RoundedCornerShape(6.dp))
                .background(color.copy(alpha = 0.15f))
                .border(2.dp, color, RoundedCornerShape(6.dp))
        )
    }
    // Readouts sit on the outside, tyres on the inside, like a car seen from above
    Row(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalAlignment = Alignment.CenterVertically) {
        if (tyreOnRight) { readout(); tyre() } else { tyre(); readout() }
    }
}

/** Current lap, delta to best, sector bars, last/best, gaps to the cars around. */
@Composable
internal fun TimingView(race: UdpTransport.RaceState?) {
    if (race == null) return Waiting()
    Card(Modifier.width(240.dp)) {
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                Column {
                    Label(if (race.lapInvalid) "LAP (INVALID)" else "CURRENT LAP")
                    Text(formatLap(race.currentLapMs), fontFamily = Mono, fontSize = 18.sp,
                        color = if (race.lapInvalid) Palette.Red else Palette.Text)
                }
                Column(horizontalAlignment = Alignment.End) {
                    Label("VS BEST")
                    val d = race.deltaMs
                    Text(d?.let { formatDelta(it) } ?: "—", fontFamily = Mono, fontSize = 18.sp,
                        color = d?.let { deltaColor(it) } ?: Palette.TextDim)
                }
            }
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                SectorBar("S1", race.s1Ms, race.s1Status, Modifier.weight(1f))
                SectorBar("S2", race.s2Ms, race.s2Status, Modifier.weight(1f))
                SectorBar("S3", 0, 0, Modifier.weight(1f))
            }
            Row(Modifier.fillMaxWidth()) {
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    TimingRow("LAST", formatLap(race.lastLapMs))
                    TimingRow("AHEAD", race.gapAheadMs?.let { "+" + formatGap(it) } ?: "—")
                }
                Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    TimingRow("BEST", formatLap(race.bestLapMs))
                    TimingRow("BEHIND", race.gapBehindMs?.let { "−" + formatGap(it) } ?: "—")
                }
            }
        }
    }
}

@Composable
private fun SectorBar(name: String, ms: Int, status: Int, modifier: Modifier) {
    val color = when (status) {
        3 -> Purple
        2 -> Palette.Green
        1 -> Palette.Yellow
        else -> Palette.PanelBorder
    }
    Column(modifier, verticalArrangement = Arrangement.spacedBy(3.dp)) {
        Box(Modifier.fillMaxWidth().height(6.dp).clip(CircleShape).background(color))
        Text(if (ms > 0) "$name %.3f".format(ms / 1000f) else "$name —",
            fontFamily = Mono, fontSize = 10.sp, color = Palette.TextDim, maxLines = 1)
    }
}

@Composable
private fun TimingRow(label: String, value: String) {
    Row {
        Text("$label ", fontFamily = Mono, fontSize = 11.sp, color = Palette.TextDim)
        Text(value, fontFamily = Mono, fontSize = 11.sp, color = Palette.Text)
    }
}

@Composable
internal fun PageDots(page: Int, count: Int) {
    Row(Modifier.padding(vertical = 6.dp), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        repeat(count) { i ->
            Box(Modifier.size(7.dp).clip(CircleShape).background(if (i == page) Palette.Cyan else Palette.PanelBorder))
        }
    }
}

@Composable
private fun Card(modifier: Modifier = Modifier, content: @Composable () -> Unit) {
    val shape = RoundedCornerShape(14.dp)
    Box(
        modifier
            .clip(shape)
            .background(Color(0xFF0D161D))
            .border(1.dp, Palette.PanelBorder, shape)
            .padding(horizontal = 14.dp, vertical = 10.dp)
    ) { content() }
}

@Composable
private fun Stat(label: String, value: @Composable RowScope.() -> Unit) {
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Label(label)
        Row(content = value)
    }
}

@Composable
private fun Label(text: String) {
    Text(text, fontFamily = Condensed, fontSize = 11.sp, letterSpacing = 2.sp, color = Palette.TextDim, maxLines = 1)
}

@Composable
private fun Waiting() {
    Text("Waiting for lap data…", fontFamily = Condensed, fontSize = 14.sp, letterSpacing = 1.sp, color = Palette.TextDim)
}

// ---- Formatting ----

/** 91902 → "1:31.902"; 0 → "—" */
private fun formatLap(ms: Long): String {
    if (ms <= 0) return "—"
    val min = ms / 60000
    val sec = (ms % 60000) / 1000.0
    return if (min > 0) "%d:%06.3f".format(min, sec) else "%.3f".format(sec)
}

/** 214 → "+0.214" */
private fun formatDelta(ms: Int) = (if (ms < 0) "−" else "+") + "%.3f".format(abs(ms) / 1000f)

/** 1234 → "1.2" */
private fun formatGap(ms: Int) = "%.1f".format(ms / 1000f)

private fun deltaColor(ms: Int) = if (ms <= 0) Palette.Green else Palette.Red

/** Surface temperature bands, roughly where F1 2021 tyres work. */
private fun tempColor(c: Int) = when {
    c < 80 -> Cold
    c <= 105 -> Palette.Green
    c <= 115 -> Palette.Yellow
    else -> Palette.Red
}

/** Visual compound → letter + colour. */
private fun compound(visual: Int): Pair<String, Color> = when (visual) {
    16 -> "S" to Palette.Red
    17 -> "M" to Palette.Yellow
    18 -> "H" to Palette.Text
    7 -> "I" to Palette.Green
    8 -> "W" to Cold
    else -> "?" to Palette.TextDim
}
