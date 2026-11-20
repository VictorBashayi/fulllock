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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import com.f1controller.network.TrackNames
import com.f1controller.network.TrackPb
import com.f1controller.network.UdpTransport
import kotlin.math.abs

/*
 * Race dashboard pieces shown around and in place of the steering wheel:
 * the info strip under BACK/START, and the PB and Timing centre views.
 * They only read UdpTransport.RaceState (sent by the receiver for F1 2021).
 */

private val Purple = Color(0xFFA855F7)
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

/** Time Trial PBs: the current track with sector splits on top, every other track below. */
@Composable
internal fun PbView(currentTrack: Int, pbs: List<TrackPb>) {
    Card(Modifier.width(240.dp)) {
        Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Label("PERSONAL BESTS · TIME TRIAL")
            if (pbs.isEmpty()) {
                Text("No Time Trial PBs yet", fontFamily = Condensed, fontSize = 13.sp, color = Palette.TextDim)
                return@Column
            }
            val current = pbs.firstOrNull { it.trackId == currentTrack }
            if (current != null) {
                Text(TrackNames.short(current.trackId).uppercase(), fontFamily = Condensed, fontSize = 15.sp,
                    color = Palette.Cyan, maxLines = 1)
                PbLapRow("DRY", current.dryMs, current.dryS1, current.dryS2)
                PbLapRow("WET", current.wetMs, current.wetS1, current.wetS2)
                Box(Modifier.fillMaxWidth().height(1.dp).background(Palette.PanelBorder))
            }
            Column(
                Modifier.heightIn(max = 160.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(3.dp)
            ) {
                for (t in pbs) if (t.trackId != currentTrack) {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Text(TrackNames.short(t.trackId), fontFamily = Mono, fontSize = 11.sp,
                            color = Palette.Text, maxLines = 1, modifier = Modifier.weight(1f))
                        Text("${formatLap(t.dryMs)}  ${formatLap(t.wetMs)}", fontFamily = Mono, fontSize = 11.sp,
                            color = Palette.TextDim, maxLines = 1)
                    }
                }
            }
        }
    }
}

/** "DRY 1:26.839" over "S1 28.1  S2 31.0  S3 27.7" (sectors "—" when not recorded). */
@Composable
private fun PbLapRow(label: String, ms: Long, s1: Int, s2: Int) {
    Column {
        TimingRow(label, formatLap(ms))
        val sectors = if (ms > 0 && s1 > 0 && s2 > 0)
            "S1 %.1f  S2 %.1f  S3 %.1f".format(s1 / 1000f, s2 / 1000f, (ms - s1 - s2) / 1000f)
        else "S1 —  S2 —  S3 —"
        Text(sectors, fontFamily = Mono, fontSize = 10.sp, color = Palette.TextDim, maxLines = 1)
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
            if (race.trackId >= 0) {
                // Time Trial PBs for this track; cyan for a PB set on the lap just finished
                val fresh = race.currentLapMs < 5000
                fun pbColor(pb: Long) = if (fresh && pb > 0 && pb == race.lastLapMs) Palette.Cyan else Palette.Text
                Column(verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    Label("PB · ${TrackNames.short(race.trackId).uppercase()}")
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        TimingRow("DRY", pbText(race.ttDryMs), pbColor(race.ttDryMs))
                        TimingRow("WET", pbText(race.ttWetMs), pbColor(race.ttWetMs))
                    }
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

private fun pbText(ms: Long) = if (ms > 0) formatLap(ms) else "—"

@Composable
private fun TimingRow(label: String, value: String, color: Color = Palette.Text) {
    Row {
        Text("$label ", fontFamily = Mono, fontSize = 11.sp, color = Palette.TextDim)
        Text(value, fontFamily = Mono, fontSize = 11.sp, color = color)
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
