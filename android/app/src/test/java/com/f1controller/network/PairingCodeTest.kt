package com.f1controller.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class PairingCodeTest {
    @Test fun normalizeAcceptsVariants() {
        assertEquals("K7QD9MXP2A", PairingCode.normalize("k7qd-9mxp-2a"))
        assertEquals("K7QD9MXP2A", PairingCode.normalize(" K7QD 9MXP 2A "))
    }

    @Test fun normalizeMapsLookalikes() {
        assertEquals("K7QD9MXP10", PairingCode.normalize("K7QD9MXPIO"))
        assertEquals("K7QD9MXP11", PairingCode.normalize("K7QD9MXPil"))
    }

    @Test fun normalizeRejects() {
        for (s in listOf(null, "", "K7QD9MXP2", "K7QD9MXP2AA", "K7QD9MXP2U", "K7QD9MXP2!"))
            assertNull(s, PairingCode.normalize(s))
    }

    @Test fun normalizeRejectsNonAscii() {
        assertNull(PairingCode.normalize("K7QD9MXP2ſ"))
        assertNull(PairingCode.normalize("K7QD9MXP2１"))
    }

    @Test fun formatGroups() = assertEquals("K7QD-9MXP-2A", PairingCode.format("K7QD9MXP2A"))

    @Test fun formatPartial() {
        assertEquals("", PairingCode.format(""))
        assertEquals("K7QD", PairingCode.format("K7QD"))
        assertEquals("K7QD-9", PairingCode.format("K7QD9"))
        assertEquals("K7QD-9MXP", PairingCode.format("K7QD9MXP"))
    }

    @Test fun fromQrTextAcceptsOnlyFulllockUri() {
        assertEquals("K7QD9MXP2A", PairingCode.fromQrText("fulllock://pair?code=k7qd-9mxp-2a"))
        assertEquals("K7QD9MXP2A", PairingCode.fromQrText("FullLock://PAIR?code=K7QD9MXP2A"))
        assertNull(PairingCode.fromQrText("https://x/?code=K7QD9MXP2A"))
        assertNull(PairingCode.fromQrText("fulllock://other?code=K7QD9MXP2A"))
        assertNull(PairingCode.fromQrText("fulllock://pair?code=short"))
        assertNull(PairingCode.fromQrText("fulllock://pair"))
    }
}
