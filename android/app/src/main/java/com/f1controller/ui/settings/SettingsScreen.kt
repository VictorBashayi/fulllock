package com.f1controller.ui.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.ui.theme.Condensed
import com.f1controller.ui.theme.Palette

/** Two-pane Settings: page list on the left, the chosen page on the right. */
@Composable
fun SettingsScreen(
    page: SettingsPage,
    onPage: (SettingsPage) -> Unit,
    pageContent: @Composable RowScope.(SettingsPage) -> Unit,
) {
    Row(
        Modifier
            .fillMaxSize()
            .background(Brush.radialGradient(listOf(Palette.BgCenter, Palette.BgEdge)))
            .systemBarsPadding()
    ) {
        Column(
            Modifier.width(190.dp).fillMaxHeight().background(Palette.BgEdge.copy(alpha = 0.55f)).padding(10.dp),
            verticalArrangement = Arrangement.spacedBy(2.dp)
        ) {
            Text("Settings", fontFamily = Condensed, fontSize = 20.sp, color = Palette.Text,
                modifier = Modifier.padding(start = 10.dp, top = 6.dp, bottom = 8.dp))
            SettingsPage.entries.forEach { p -> RailItem(p, p == page) { onPage(p) } }
        }
        Row(Modifier.weight(1f).fillMaxHeight().padding(14.dp), horizontalArrangement = Arrangement.spacedBy(14.dp)) {
            pageContent(page)
        }
    }
}

@Composable
private fun RailItem(page: SettingsPage, selected: Boolean, onClick: () -> Unit) {
    val shape = RoundedCornerShape(50)
    Row(
        Modifier
            .fillMaxWidth()
            .heightIn(min = 44.dp)
            .then(if (selected) Modifier.background(Palette.Panel, shape).border(1.dp, Palette.PanelBorder, shape) else Modifier)
            .clickable(onClick = onClick)
            .padding(horizontal = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp)
    ) {
        Box(
            Modifier.size(26.dp).background(if (selected) Palette.Cyan.copy(alpha = 0.15f) else Palette.Panel, CircleShape),
            contentAlignment = Alignment.Center
        ) {
            Icon(page.icon, contentDescription = null, tint = if (selected) Palette.Cyan else Palette.TextDim, modifier = Modifier.size(15.dp))
        }
        Text(
            page.label, fontSize = 15.sp,
            color = if (selected) Palette.Text else Palette.TextDim,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal,
        )
    }
}
