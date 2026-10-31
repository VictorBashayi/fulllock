using System;

namespace F1Controller.Pad;

/// <summary>
/// Madgwick AHRS, ported line for line from the phone's MadgwickFilter.kt.
/// Only difference: <see cref="Update"/> takes the sample interval instead of a timestamp.
/// </summary>
public sealed class MadgwickFilter
{
    private float _q0 = 1f, _q1, _q2, _q3;
    private bool _initialized;

    /// <summary>Filter gain — higher = more accelerometer influence.</summary>
    public float Beta { get; set; } = 0.1f;

    /// <param name="gx">Gyroscope X (rad/s)</param>
    /// <param name="ax">Accelerometer X (any unit, only direction matters)</param>
    /// <param name="dtSeconds">Time since the previous sample</param>
    public void Update(float gx, float gy, float gz, float ax, float ay, float az, float dtSeconds)
    {
        if (!_initialized)
        {
            // Seed quaternion from initial accelerometer reading (assume gravity-only)
            InitFromAccel(ax, ay, az);
            _initialized = true;
            return;
        }

        // Guard against bad dt (too large → first sample after pause; too small → duplicate)
        float dt = dtSeconds;
        if (dt <= 0f || dt > 0.5f) return;

        float q0 = _q0, q1 = _q1, q2 = _q2, q3 = _q3;

        // ---- Rate of change from gyroscope ----
        float qDot0 = 0.5f * (-q1 * gx - q2 * gy - q3 * gz);
        float qDot1 = 0.5f * (q0 * gx + q2 * gz - q3 * gy);
        float qDot2 = 0.5f * (q0 * gy - q1 * gz + q3 * gx);
        float qDot3 = 0.5f * (q0 * gz + q1 * gy - q2 * gx);

        // ---- Gradient-descent corrective step from accelerometer ----
        float aNorm = MathF.Sqrt(ax * ax + ay * ay + az * az);
        if (aNorm > 0.001f)
        {
            float recipNorm = 1f / aNorm;
            float nax = ax * recipNorm, nay = ay * recipNorm, naz = az * recipNorm;

            float _2q0 = 2f * q0, _2q1 = 2f * q1, _2q2 = 2f * q2, _2q3 = 2f * q3;
            float _4q0 = 4f * q0, _4q1 = 4f * q1, _4q2 = 4f * q2;
            float _8q1 = 8f * q1, _8q2 = 8f * q2;
            float q0q0 = q0 * q0, q1q1 = q1 * q1, q2q2 = q2 * q2, q3q3 = q3 * q3;

            // Gradient (Jacobian^T * f)
            float s0 = _4q0 * q2q2 + _2q2 * nax + _4q0 * q1q1 - _2q1 * nay;
            float s1 = _4q1 * q3q3 - _2q3 * nax + 4f * q0q0 * q1 - _2q0 * nay - _4q1 + _8q1 * q1q1 + _8q1 * q2q2 + _4q1 * naz;
            float s2 = 4f * q0q0 * q2 + _2q0 * nax + _4q2 * q3q3 - _2q3 * nay - _4q2 + _8q2 * q1q1 + _8q2 * q2q2 + _4q2 * naz;
            float s3 = 4f * q1q1 * q3 - _2q1 * nax + 4f * q2q2 * q3 - _2q2 * nay;

            aNorm = MathF.Sqrt(s0 * s0 + s1 * s1 + s2 * s2 + s3 * s3);
            if (aNorm > 0.001f)
            {
                float rn = 1f / aNorm;
                s0 *= rn; s1 *= rn; s2 *= rn; s3 *= rn;

                qDot0 -= Beta * s0;
                qDot1 -= Beta * s1;
                qDot2 -= Beta * s2;
                qDot3 -= Beta * s3;
            }
        }

        // ---- Integrate rate of change ----
        q0 += qDot0 * dt;
        q1 += qDot1 * dt;
        q2 += qDot2 * dt;
        q3 += qDot3 * dt;

        float norm = 1f / MathF.Sqrt(q0 * q0 + q1 * q1 + q2 * q2 + q3 * q3);
        _q0 = q0 * norm;
        _q1 = q1 * norm;
        _q2 = q2 * norm;
        _q3 = q3 * norm;
    }

    /// <summary>Seed the quaternion so the predicted gravity equals the measured one.</summary>
    private void InitFromAccel(float ax, float ay, float az)
    {
        float norm = MathF.Sqrt(ax * ax + ay * ay + az * az);
        if (norm < 0.001f) return;
        float nax = ax / norm, nay = ay / norm, naz = az / norm;

        if (naz >= 0f)
        {
            float s = MathF.Sqrt(2f * (1f + naz));
            _q0 = s / 2f; _q1 = nay / s; _q2 = -nax / s; _q3 = 0f;
        }
        else
        {
            float s = MathF.Sqrt(2f * (1f - naz));
            _q0 = nay / s; _q1 = s / 2f; _q2 = 0f; _q3 = nax / s;
        }
    }

    /// <summary>Reset filter to identity orientation.</summary>
    public void Reset()
    {
        _q0 = 1f; _q1 = 0f; _q2 = 0f; _q3 = 0f;
        _initialized = false;
    }

    // ---- Estimated gravity ("up" as the accelerometer sees it) in device frame ----

    public float GravityX() => 2f * (_q1 * _q3 - _q0 * _q2);
    public float GravityY() => 2f * (_q0 * _q1 + _q2 * _q3);
    public float GravityZ() => _q0 * _q0 - _q1 * _q1 - _q2 * _q2 + _q3 * _q3;
}
