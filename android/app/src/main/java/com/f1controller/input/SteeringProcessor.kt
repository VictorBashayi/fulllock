package com.f1controller.input

import com.f1controller.sensor.MadgwickFilter
import kotlin.math.abs
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

    enum class SteeringAxis { ROLL, YAW }

    val madgwick = MadgwickFilter()

    /** Which Euler angle to use as steering input. */
    @Volatile var steeringAxis: SteeringAxis = SteeringAxis.ROLL

    /** Half the lock-to-lock range in degrees. At this angle, output saturates at ±32767. */
    @Volatile var lockAngleDeg: Float = 90f

    /** Deadzone in degrees — inputs smaller than this map to zero. */
    @Volatile var deadzoneDeg: Float = 2f

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
        val rawAngleRad = when (steeringAxis) {
            SteeringAxis.ROLL -> madgwick.getRollRad()
            SteeringAxis.YAW  -> madgwick.getYawRad()
        }

        // Step 3: Subtract center offset
        val centeredRad = rawAngleRad - centerOffsetRad
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
        val steer = (clamped * Short.MAX_VALUE).toInt().toShort()

        currentSteer = steer
        currentAngleDeg = centeredDeg

        return steer
    }

    /** Capture the current orientation as the new zero-center. */
    fun calibrateCenter() {
        centerOffsetRad = when (steeringAxis) {
            SteeringAxis.ROLL -> madgwick.getRollRad()
            SteeringAxis.YAW  -> madgwick.getYawRad()
        }
    }

    /** Reset filter and calibration. */
    fun reset() {
        madgwick.reset()
        centerOffsetRad = 0f
        currentSteer = 0
        currentAngleDeg = 0f
    }
}
