package com.f1controller.input

import com.f1controller.sensor.MadgwickFilter
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.atan2
import kotlin.math.max
import kotlin.math.min

/**
 * Phase 2: Steering Processor
 *
 * Pipeline: Raw IMU → Madgwick AHRS → Axis extraction → Center offset →
 *           Deadzone → Lock-to-lock scaling → i16 output
 *
 * Designed to run directly in the sensor callback thread for minimum latency.
 * All state is written atomically or is single-writer (sensor thread).
 */
class SteeringProcessor {

    /**
     * TILT: rotation about the screen normal, measured from the gravity direction
     *       projected onto the screen plane. This is the "hold it like a wheel"
     *       motion and works at any screen pitch. Assumes the fixed landscape
     *       orientation from the manifest (device +X points up).
     * ROLL / YAW: raw Euler angles. ROLL gimbal-locks when the phone is held
     *       upright in landscape; YAW drifts (no magnetometer).
     */
    enum class SteeringAxis { TILT, ROLL, YAW }

    val madgwick = MadgwickFilter()

    /** Which Euler angle to use as steering input. */
    @Volatile var steeringAxis: SteeringAxis = SteeringAxis.TILT

    /** Half the lock-to-lock range in degrees. At this angle, output saturates at ±32767. */
    @Volatile var lockAngleDeg: Float = 90f

    /** Deadzone in degrees — inputs smaller than this map to zero. */
    @Volatile var deadzoneDeg: Float = 2f

    /**
     * Center smoothing, 0..1. Blends a linear response with a cubic one:
     *   out = (1 - k)·x + k·x³
     * Full lock still maps to full lock, but near center the response is
     * flattened (slope 1 - k), so small wobbles on straights steer less.
     * 0 = linear (original behaviour).
     */
    @Volatile var curve: Float = 0.3f

    /** Captured center offset in radians (set by calibration). */
    @Volatile var centerOffsetRad: Float = 0f

    /** Latest computed steering value, updated on every sensor event. */
    @Volatile var currentSteer: Short = 0; private set

    /** Latest steering angle in degrees (for UI display). */
    @Volatile var currentAngleDeg: Float = 0f; private set

    /**
     * Process one IMU sample. Call from the sensor event callback.
     *
     * @return The signed 16-bit steering value.
     */
    fun process(
        gx: Float, gy: Float, gz: Float,
        ax: Float, ay: Float, az: Float,
        timestampNs: Long,
    ): Short {
        // Step 1: Update AHRS orientation
        madgwick.update(gx, gy, gz, ax, ay, az, timestampNs)

        // Step 2: Extract the selected Euler angle (radians)
        val rawAngleRad = rawAngle()

        // Step 3: Subtract center offset (wrapped so crossing ±180° doesn't jump)
        val centeredRad = wrapPi(rawAngleRad - centerOffsetRad)
        val centeredDeg = Math.toDegrees(centeredRad.toDouble()).toFloat()

        // Step 4: Apply deadzone
        val afterDeadzone = if (abs(centeredDeg) < deadzoneDeg) {
            0f
        } else {
            // Remove deadzone width so output starts from zero at the deadzone edge
            val sign = if (centeredDeg > 0f) 1f else -1f
            sign * (abs(centeredDeg) - deadzoneDeg)
        }

        // Step 5: Scale to ±32767 within lock angle (minus deadzone eaten)
        val effectiveRange = max(lockAngleDeg - deadzoneDeg, 1f)
        val normalized = afterDeadzone / effectiveRange  // -1 .. +1
        val clamped = max(-1f, min(1f, normalized))

        // Step 6: Response curve
        val shaped = applyCurve(clamped, curve)
        val steer = (shaped * Short.MAX_VALUE).toInt().toShort()

        currentSteer = steer
        currentAngleDeg = centeredDeg

        return steer
    }

    companion object {
        /** Maps x in -1..1 through the center-smoothing curve (see [curve]). */
        fun applyCurve(x: Float, k: Float): Float {
            val kc = k.coerceIn(0f, 1f)
            return (1f - kc) * x + kc * x * x * x
        }
    }

    /** Capture the current orientation as the new zero-center. */
    fun calibrateCenter() {
        centerOffsetRad = rawAngle()
    }

    private fun rawAngle(): Float = when (steeringAxis) {
        SteeringAxis.TILT -> atan2(madgwick.gravityY(), madgwick.gravityX())
        SteeringAxis.ROLL -> madgwick.getRollRad()
        SteeringAxis.YAW  -> madgwick.getYawRad()
    }

    private fun wrapPi(rad: Float): Float {
        var r = rad
        while (r > PI) r -= (2 * PI).toFloat()
        while (r < -PI) r += (2 * PI).toFloat()
        return r
    }

    /** Reset filter and calibration. */
    fun reset() {
        madgwick.reset()
        centerOffsetRad = 0f
        currentSteer = 0
        currentAngleDeg = 0f
    }
}
