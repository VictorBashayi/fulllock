package com.f1controller.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class DiscoveryPacketTest {

    /** v2 beacon. */
    private val beacon = byteArrayOf(
        0xFB.toByte(), -1, -1, -1,
        'F'.code.toByte(), 'L'.code.toByte(), 'R'.code.toByte(), 'X'.code.toByte(),
        0xB3.toByte(), 0x15, 2
    )

    @Test
    fun oldV1BeaconIsNull() {
        val old = byteArrayOf(0xFB.toByte(), -1, -1, -1, 'F'.code.toByte(), '1'.code.toByte(),
            'R'.code.toByte(), 'X'.code.toByte(), 0xB3.toByte(), 0x15)
        assertNull(decodeDiscovery(old, old.size))
    }

    @Test
    fun wrongVersionIsNull() {
        val v1 = beacon.copyOf().also { it[10] = 1 }
        assertNull(decodeDiscovery(v1, v1.size))
    }

    @Test
    fun decodesControlPort() {
        assertEquals(5555, decodeDiscovery(beacon, beacon.size))
    }

    @Test
    fun wrongTagIsNull() {
        val other = beacon.copyOf().also { it[4] = 'X'.code.toByte() }
        assertNull(decodeDiscovery(other, other.size))
    }

    @Test
    fun shortPacketIsNull() {
        assertNull(decodeDiscovery(beacon, 10))
    }

    @Test
    fun switchesOnlyWhenNotConnectedAndServerDiffers() {
        assertTrue(shouldSwitchServer(false, "192.168.247.224", 5555, "192.168.206.224", 5555))
        assertTrue(shouldSwitchServer(false, "192.168.206.224", 5555, "192.168.206.224", 6000))
        assertFalse(shouldSwitchServer(false, " 192.168.206.224 ", 5555, "192.168.206.224", 5555))
        assertFalse(shouldSwitchServer(true, "192.168.247.224", 5555, "192.168.206.224", 5555))
    }
}
