package com.f1controller.input

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.sin

class SteeringAngleTest {

    /**
     * Gravity in the device frame for a wheel turn [alphaDeg] (about the axis pointing at the
     * driver) with the screen tilted back by [phiDeg].
     */
    private fun gravity(alphaDeg: Double, phiDeg: Double): FloatArray {
        val a = Math.toRadians(alphaDeg)
        val p = Math.toRadians(phiDeg)
        return floatArrayOf((cos(p) * cos(a)).toFloat(), (-sin(a)).toFloat(), (sin(p) * cos(a)).toFloat())
    }

    private fun deg(rad: Float) = Math.toDegrees(rad.toDouble()).toFloat()

    @Test
    fun horizonIgnoresScreenTilt() {
        for (phi in listOf(0.0, 30.0, 45.0, 60.0)) {
            val g = gravity(20.0, phi)
            assertEquals("phi=$phi", -20f, deg(SteeringProcessor.horizonAngle(g[0], g[1], g[2])), 0.5f)
        }
    }

    @Test
    fun tiltGrowsWithScreenTilt() {
        // The bug Horizon fixes: the same 20° turn reads as ~36° with the screen tilted back 60°
        val g = gravity(20.0, 60.0)
        assertTrue(abs(deg(SteeringProcessor.tiltAngle(g[0], g[1]))) > 35f)
    }

    @Test
    fun horizonMatchesTiltWhenUpright() {
        val g = gravity(30.0, 0.0)
        assertEquals(-30f, deg(SteeringProcessor.horizonAngle(g[0], g[1], g[2])), 0.1f)
        assertEquals(-30f, deg(SteeringProcessor.tiltAngle(g[0], g[1])), 0.1f)
    }

    @Test
    fun horizonIsZeroWhenFlatOrUnknown() {
        assertEquals(0f, SteeringProcessor.horizonAngle(0f, 0f, 1f), 0f)
        val unknown = SteeringProcessor.horizonAngle(0f, 0f, 0f)
        assertFalse(unknown.isNaN())
        assertEquals(0f, unknown, 0f)
    }

    @Test
    fun horizonIsTheDefault() {
        assertEquals(SteeringProcessor.SteeringAxis.HORIZON, SteeringProcessor().steeringAxis)
    }
}
