package com.f1controller.sensor

import kotlin.math.sqrt

/**
 * Madgwick AHRS (Attitude and Heading Reference System) filter.
 *
 * Reference: S. Madgwick, "An efficient orientation filter for inertial and
 * inertial/magnetic sensor arrays", 2010.
 *
 * This implementation uses only gyroscope + accelerometer (no magnetometer)
 * to produce a quaternion orientation estimate. The accelerometer corrects
 * gyroscope drift by providing a gravity reference vector.
 *
 * The key tuning parameter `beta` represents the estimated mean zero-mean
 * gyroscope measurement error expressed as the magnitude of the quaternion
 * derivative. Higher values increase accelerometer trust (more stable at rest,
 * but more susceptible to linear-acceleration disturbance).
 *
 *   - Typical beta for handheld IMU: 0.04 – 0.15
 *   - We default to 0.1 which balances drift correction and responsiveness
 */
class MadgwickFilter {

    /** Current orientation as a unit quaternion [w, x, y, z]. */
    var q0: Float = 1f; private set
    var q1: Float = 0f; private set
    var q2: Float = 0f; private set
    var q3: Float = 0f; private set

    /** Filter gain — higher = more accelerometer influence. */
    @Volatile
    var beta: Float = 0.1f

    /** Last timestamp for computing dt (nanoseconds, monotonic). */
    private var lastTimestampNs: Long = 0L
    private var initialized = false

    /**
     * Feed a new IMU sample into the filter.
     *
     * @param gx Gyroscope X (rad/s)
     * @param gy Gyroscope Y (rad/s)
     * @param gz Gyroscope Z (rad/s)
     * @param ax Accelerometer X (m/s² or g, only direction matters since we normalise)
     * @param ay Accelerometer Y
     * @param az Accelerometer Z
     * @param timestampNs SensorEvent.timestamp in nanoseconds
     */
    fun update(
        gx: Float, gy: Float, gz: Float,
        ax: Float, ay: Float, az: Float,
        timestampNs: Long,
    ) {
        if (!initialized) {
            // Seed quaternion from initial accelerometer reading (assume gravity-only)
            initFromAccel(ax, ay, az)
            lastTimestampNs = timestampNs
            initialized = true
            return
        }

        val dt = (timestampNs - lastTimestampNs) / 1e9f
        lastTimestampNs = timestampNs

        // Guard against bad dt (too large → first sample after pause; too small → duplicate)
        if (dt <= 0f || dt > 0.5f) return

        var q0 = this.q0
        var q1 = this.q1
        var q2 = this.q2
        var q3 = this.q3

        // ---- Rate of change from gyroscope ----
        var qDot0 = 0.5f * (-q1 * gx - q2 * gy - q3 * gz)
        var qDot1 = 0.5f * ( q0 * gx + q2 * gz - q3 * gy)
        var qDot2 = 0.5f * ( q0 * gy - q1 * gz + q3 * gx)
        var qDot3 = 0.5f * ( q0 * gz + q1 * gy - q2 * gx)

        // ---- Gradient-descent corrective step from accelerometer ----
        var aNorm = sqrt(ax * ax + ay * ay + az * az)
        if (aNorm > 0.001f) {
            val recipNorm = 1f / aNorm
            val nax = ax * recipNorm
            val nay = ay * recipNorm
            val naz = az * recipNorm

            // Objective function: f = q* ⊗ [0,0,0,1] ⊗ q - [0, ax, ay, az]
            // Auxiliary variables to avoid repeated arithmetic
            val _2q0 = 2f * q0
            val _2q1 = 2f * q1
            val _2q2 = 2f * q2
            val _2q3 = 2f * q3
            val _4q0 = 4f * q0
            val _4q1 = 4f * q1
            val _4q2 = 4f * q2
            val _8q1 = 8f * q1
            val _8q2 = 8f * q2
            val q0q0 = q0 * q0
            val q1q1 = q1 * q1
            val q2q2 = q2 * q2
            val q3q3 = q3 * q3

            // Gradient (Jacobian^T * f)
            var s0 = _4q0 * q2q2 + _2q2 * nax + _4q0 * q1q1 - _2q1 * nay
            var s1 = _4q1 * q3q3 - _2q3 * nax + 4f * q0q0 * q1 - _2q0 * nay - _4q1 + _8q1 * q1q1 + _8q1 * q2q2 + _4q1 * naz
            var s2 = 4f * q0q0 * q2 + _2q0 * nax + _4q2 * q3q3 - _2q3 * nay - _4q2 + _8q2 * q1q1 + _8q2 * q2q2 + _4q2 * naz
            var s3 = 4f * q1q1 * q3 - _2q1 * nax + 4f * q2q2 * q3 - _2q2 * nay

            // Normalise gradient
            aNorm = sqrt(s0 * s0 + s1 * s1 + s2 * s2 + s3 * s3)
            if (aNorm > 0.001f) {
                val rn = 1f / aNorm
                s0 *= rn; s1 *= rn; s2 *= rn; s3 *= rn

                // Apply feedback
                qDot0 -= beta * s0
                qDot1 -= beta * s1
                qDot2 -= beta * s2
                qDot3 -= beta * s3
            }
        }

        // ---- Integrate rate of change ----
        q0 += qDot0 * dt
        q1 += qDot1 * dt
        q2 += qDot2 * dt
        q3 += qDot3 * dt

        // Normalise quaternion
        val norm = 1f / sqrt(q0 * q0 + q1 * q1 + q2 * q2 + q3 * q3)
        this.q0 = q0 * norm
        this.q1 = q1 * norm
        this.q2 = q2 * norm
        this.q3 = q3 * norm
    }

    /**
     * Seed the quaternion from a gravity reading so the filter starts at the
     * correct orientation instead of converging from identity.
     */
    private fun initFromAccel(ax: Float, ay: Float, az: Float) {
        val norm = sqrt(ax * ax + ay * ay + az * az)
        if (norm < 0.001f) return
        val nax = ax / norm
        val nay = ay / norm
        val naz = az / norm

        // Quaternion whose predicted gravity direction (see gravityX/Y/Z)
        // equals the measured one, so the filter starts converged.
        if (naz >= 0f) {
            val s = sqrt(2f * (1f + naz))
            q0 = s / 2f
            q1 = nay / s
            q2 = -nax / s
            q3 = 0f
        } else {
            val s = sqrt(2f * (1f - naz))
            q0 = nay / s
            q1 = s / 2f
            q2 = 0f
            q3 = nax / s
        }
    }

    /** Reset filter to identity orientation. */
    fun reset() {
        q0 = 1f; q1 = 0f; q2 = 0f; q3 = 0f
        initialized = false
    }

    // ---- Estimated gravity ("up" as the accelerometer sees it) in device frame ----

    fun gravityX(): Float = 2f * (q1 * q3 - q0 * q2)
    fun gravityY(): Float = 2f * (q0 * q1 + q2 * q3)
    fun gravityZ(): Float = q0 * q0 - q1 * q1 - q2 * q2 + q3 * q3

    // ---- Euler angle extraction (intrinsic ZYX / Tait-Bryan) ----

    /** Roll angle in radians (rotation about X-axis). */
    fun getRollRad(): Float {
        val sinr = 2f * (q0 * q1 + q2 * q3)
        val cosr = 1f - 2f * (q1 * q1 + q2 * q2)
        return kotlin.math.atan2(sinr, cosr)
    }

    /** Pitch angle in radians (rotation about Y-axis). */
    fun getPitchRad(): Float {
        val sinp = 2f * (q0 * q2 - q3 * q1)
        return if (kotlin.math.abs(sinp) >= 1f) {
            Math.copySign(Math.PI.toFloat() / 2f, sinp)
        } else {
            kotlin.math.asin(sinp)
        }
    }

    /** Yaw angle in radians (rotation about Z-axis). */
    fun getYawRad(): Float {
        val siny = 2f * (q0 * q3 + q1 * q2)
        val cosy = 1f - 2f * (q2 * q2 + q3 * q3)
        return kotlin.math.atan2(siny, cosy)
    }
}
