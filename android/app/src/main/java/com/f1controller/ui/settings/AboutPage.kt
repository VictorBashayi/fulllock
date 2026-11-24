package com.f1controller.ui.settings

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ChevronRight
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.R
import com.f1controller.ui.theme.Condensed
import com.f1controller.ui.theme.Palette

private const val REPO_URL = "https://github.com/VictorBashayi/fulllock"

@Composable
fun RowScope.AboutPage(versionName: String, onHowItWorks: () -> Unit) {
    val context = LocalContext.current

    PreviewCard {
        Image(painterResource(R.drawable.ic_launcher_foreground), contentDescription = null, modifier = Modifier.size(96.dp))
        Text("FullLock", fontFamily = Condensed, fontSize = 22.sp, color = Palette.Text)
        ValuePill("v$versionName")
    }

    SettingsGroup(Modifier.weight(1f).align(Alignment.Top)) {
        SettingsRow("How it works", "Pair, hold, drive in three steps") { Chevron("How it works", onHowItWorks) }
        SettingsRow("Your data", "Inputs stay on your Wi-Fi, signed with your pairing key")
        SettingsRow("Source code", "github.com/VictorBashayi/fulllock") {
            Chevron("Open source code") {
                try { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(REPO_URL))) }
                catch (_: android.content.ActivityNotFoundException) {} // no browser: nothing to open
            }
        }
        SettingsRow("Licence", "MIT, open source", divider = false)
    }
}

@Composable
private fun Chevron(description: String, onClick: () -> Unit) {
    IconButton(onClick = onClick) { Icon(Icons.Default.ChevronRight, description, tint = Palette.TextDim) }
}
