package com.f1controller.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily

/** The app's one palette: the driving screen draws from it directly, everything else via the Material scheme. */
object Palette {
    val BgCenter = Color(0xFF10232B)
    val BgEdge = Color(0xFF090F14)
    val Panel = Color(0xFF111B24)
    val PanelBorder = Color(0xFF243441)
    val Text = Color(0xFFE6EDF2)
    val TextDim = Color(0xFF8A99A6)
    val Cyan = Color(0xFF22D3EE)
    val Red = Color(0xFFF26B6B)
    val Green = Color(0xFF3DDC84)
    val Yellow = Color(0xFFF2C94C)
    val Blue = Color(0xFF5B7BF0)
}

val Condensed = FontFamily(
    android.graphics.Typeface.create("sans-serif-condensed", android.graphics.Typeface.BOLD)
)
