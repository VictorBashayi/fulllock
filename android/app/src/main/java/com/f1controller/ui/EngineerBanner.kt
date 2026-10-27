package com.f1controller.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.network.RadioCall

/** An engineer call, shown where the info strip normally sits. Colour bar = urgency. */
@Composable
internal fun EngineerBanner(call: RadioCall) {
    val accent = when {
        call.priority >= 3 -> Palette.Red
        call.priority == 2 -> Palette.Yellow
        else -> Palette.Cyan
    }
    val shape = RoundedCornerShape(10.dp)
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(shape)
            .background(Palette.Panel)
            .border(1.dp, accent.copy(alpha = 0.6f), shape)
            .height(IntrinsicSize.Min),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Box(Modifier.width(4.dp).fillMaxHeight().background(accent))
        Column(Modifier.padding(horizontal = 10.dp, vertical = 4.dp)) {
            Text("ENGINEER", fontFamily = Condensed, fontSize = 10.sp, letterSpacing = 2.sp, color = accent)
            Text(
                call.text,
                fontFamily = Condensed,
                fontSize = 15.sp,
                lineHeight = 17.sp,
                color = Palette.Text,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}
