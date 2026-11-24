package com.f1controller.ui.settings

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.ui.theme.Condensed
import com.f1controller.ui.theme.Palette

private val CardShape = RoundedCornerShape(18.dp)

/** A titled-less card of rows, separated by thin dividers. */
@Composable
fun SettingsGroup(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Column(
        modifier
            .background(Palette.Panel, CardShape)
            .border(1.dp, Palette.PanelBorder, CardShape)
            .padding(horizontal = 16.dp),
        content = content
    )
}

/** Label, one muted line, control on the right; [below] (a slider) sits under the text. */
@Composable
fun SettingsRow(
    label: String,
    line: String,
    lineColor: Color = Palette.TextDim,
    divider: Boolean = true,
    below: (@Composable () -> Unit)? = null,
    control: @Composable () -> Unit = {},
) {
    Column {
        Row(Modifier.fillMaxWidth().heightIn(min = 56.dp).padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(label, fontFamily = Condensed, fontSize = 16.sp, color = Palette.Text)
                Text(line, fontSize = 13.sp, color = lineColor)
                below?.invoke()
            }
            Spacer(Modifier.width(12.dp))
            control()
        }
        if (divider) HorizontalDivider(color = Palette.PanelBorder, thickness = 1.dp)
    }
}

/** The page's live preview: a fixed-width card on the left of the content pane. */
@Composable
fun PreviewCard(modifier: Modifier = Modifier, content: @Composable ColumnScope.() -> Unit) {
    Column(
        modifier
            .width(190.dp)
            .fillMaxHeight()
            .background(Palette.Panel, CardShape)
            .border(1.dp, Palette.PanelBorder, CardShape)
            .padding(12.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(6.dp, Alignment.CenterVertically),
        content = content
    )
}

@Composable
fun ValuePill(text: String) {
    Text(
        text,
        fontFamily = FontFamily.Monospace, fontSize = 13.sp, color = Palette.Text,
        modifier = Modifier
            .background(Palette.BgEdge, RoundedCornerShape(50))
            .border(1.dp, Palette.PanelBorder, RoundedCornerShape(50))
            .padding(horizontal = 10.dp, vertical = 3.dp)
    )
}

@Composable
fun PrimaryPill(text: String, onClick: () -> Unit, color: Color = Palette.Cyan) {
    Button(
        onClick = onClick,
        shape = RoundedCornerShape(50),
        colors = ButtonDefaults.buttonColors(containerColor = color, contentColor = Palette.BgEdge),
        modifier = Modifier.heightIn(min = 48.dp)
    ) { Text(text, fontFamily = Condensed, letterSpacing = 1.sp) }
}

@Composable
fun OutlinePill(text: String, onClick: () -> Unit) {
    OutlinedButton(
        onClick = onClick,
        shape = RoundedCornerShape(50),
        border = BorderStroke(1.dp, Palette.PanelBorder),
        colors = ButtonDefaults.outlinedButtonColors(contentColor = Palette.Text),
        modifier = Modifier.heightIn(min = 48.dp)
    ) { Text(text, fontFamily = Condensed) }
}

@Composable
fun PaletteSlider(value: Float, range: ClosedFloatingPointRange<Float>, onChange: (Float) -> Unit) {
    Slider(
        value = value, onValueChange = onChange, valueRange = range,
        colors = SliderDefaults.colors(
            thumbColor = Palette.Cyan, activeTrackColor = Palette.Cyan,
            inactiveTrackColor = Palette.PanelBorder,
        )
    )
}

@Composable
fun PaletteSwitch(checked: Boolean, onChange: (Boolean) -> Unit) {
    Switch(
        checked = checked, onCheckedChange = onChange,
        colors = SwitchDefaults.colors(
            checkedThumbColor = Palette.BgEdge, checkedTrackColor = Palette.Cyan,
            uncheckedThumbColor = Palette.TextDim, uncheckedTrackColor = Palette.PanelBorder,
            uncheckedBorderColor = Palette.PanelBorder,
        )
    )
}

@Composable
fun PaletteChips(options: List<String>, selected: Int, onSelect: (Int) -> Unit) {
    Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        options.forEachIndexed { i, label ->
            val on = i == selected
            Text(
                label,
                fontFamily = Condensed, fontSize = 14.sp,
                color = if (on) Palette.BgEdge else Palette.Text,
                fontWeight = if (on) FontWeight.Bold else FontWeight.Normal,
                modifier = Modifier
                    .heightIn(min = 48.dp)
                    .wrapContentHeight(Alignment.CenterVertically)
                    .background(if (on) Palette.Cyan else Palette.BgEdge, RoundedCornerShape(50))
                    .border(1.dp, if (on) Palette.Cyan else Palette.PanelBorder, RoundedCornerShape(50))
                    .clickable { onSelect(i) }
                    .padding(horizontal = 14.dp, vertical = 6.dp)
            )
        }
    }
}
