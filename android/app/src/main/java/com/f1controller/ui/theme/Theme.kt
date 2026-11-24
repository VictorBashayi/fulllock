package com.f1controller.ui.theme

import android.app.Activity
import androidx.compose.material3.ColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.core.view.WindowCompat

fun fullLockColorScheme(): ColorScheme = darkColorScheme(
    primary = Palette.Cyan,
    onPrimary = Palette.BgEdge,
    secondary = Palette.Green,
    onSecondary = Palette.BgEdge,
    tertiary = Palette.Yellow,
    onTertiary = Palette.BgEdge,
    background = Palette.BgEdge,
    onBackground = Palette.Text,
    surface = Palette.Panel,
    onSurface = Palette.Text,
    surfaceVariant = Palette.BgCenter,
    onSurfaceVariant = Palette.TextDim,
    outline = Palette.PanelBorder,
    error = Palette.Red,
)

@Composable
fun F1ControllerTheme(content: @Composable () -> Unit) {
    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as Activity).window
            window.statusBarColor = Palette.BgEdge.toArgb()
            window.navigationBarColor = Palette.BgEdge.toArgb()
            WindowCompat.getInsetsController(window, view).apply {
                isAppearanceLightStatusBars = false
                isAppearanceLightNavigationBars = false
            }
        }
    }

    MaterialTheme(colorScheme = fullLockColorScheme(), typography = Typography(), content = content)
}
