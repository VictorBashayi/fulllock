package com.f1controller.ui.theme

import org.junit.Assert.assertEquals
import org.junit.Test

class ThemeTest {
    @Test
    fun schemeUsesPalette() {
        val s = fullLockColorScheme()
        assertEquals(Palette.Cyan, s.primary)
        assertEquals(Palette.BgEdge, s.onPrimary)
        assertEquals(Palette.BgEdge, s.background)
        assertEquals(Palette.Panel, s.surface)
        assertEquals(Palette.PanelBorder, s.outline)
        assertEquals(Palette.Text, s.onSurface)
        assertEquals(Palette.TextDim, s.onSurfaceVariant)
        assertEquals(Palette.Red, s.error)
    }
}
