package com.f1controller.sensor

import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Handler
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import java.io.BufferedWriter
import java.io.File
import java.io.FileWriter
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

/**
 * Phase 0: IMU Characterization Logger
 *
 * Records raw gyroscope and accelerometer data using SensorEvent.timestamp
 * (nanoseconds, monotonic clock) to CSV for offline analysis:
 *   timestamp_ns,gyro_x,gyro_y,gyro_z,accel_x,accel_y,accel_z
 *
 * The sample rate displayed is derived from inter-event deltas so it reflects
 * the sensor HAL's actual delivery rate, not the requested rate.
 */
class SensorLogger(
    private val sensorManager: SensorManager,
    /** Sensor callbacks (and all CSV writes) run on this handler's thread. */
    private val handler: Handler,
) : SensorEventListener {

    data class LoggerState(
        val isRecording: Boolean = false,
        val sampleCount: Long = 0L,
        val gyroRateHz: Float = 0f,
        val accelRateHz: Float = 0f,
        val durationMs: Long = 0L,
        val lastFilePath: String = "",
        val minDelayGyroUs: Int = 0,
        val minDelayAccelUs: Int = 0,
    )

    private val _state = MutableStateFlow(LoggerState())
    val state: StateFlow<LoggerState> = _state

    private val isRecording = AtomicBoolean(false)

    // Latest sensor readings shared with other components
    private val _gyro = MutableStateFlow(FloatArray(3))
    private val _accel = MutableStateFlow(FloatArray(3))
    val gyro: StateFlow<FloatArray> = _gyro
    val accel: StateFlow<FloatArray> = _accel

    // CSV writer — accessed only from the sensor callback thread
    @Volatile private var writer: BufferedWriter? = null
    @Volatile private var startTimeNs: Long = 0L
    @Volatile private var latestGyroTimestampNs: Long = 0L
    @Volatile private var latestAccelTimestampNs: Long = 0L
    private val sampleCount = AtomicLong(0L)

    // Rate estimation: rolling window of last N timestamps per sensor
    private val gyroTimestamps = LongArray(64)
    private var gyroTsHead = 0
    private val accelTimestamps = LongArray(64)
    private var accelTsHead = 0

    private val gyroSensor: Sensor? = sensorManager.getDefaultSensor(Sensor.TYPE_GYROSCOPE)
    private val accelSensor: Sensor? = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)

    // Latest row state to merge gyro+accel into one CSV line
    @Volatile private var pendingGyro: FloatArray? = null
    @Volatile private var pendingAccel: FloatArray? = null
    @Volatile private var pendingTimestampNs: Long = 0L

    init {
        _state.value = LoggerState(
            minDelayGyroUs = gyroSensor?.minDelay ?: -1,
            minDelayAccelUs = accelSensor?.minDelay ?: -1,
        )
    }

    fun startRecording(outputDir: File) {
        if (isRecording.getAndSet(true)) return

        val sdf = SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US)
        val filename = "imu_${sdf.format(Date())}.csv"
        val file = File(outputDir, filename)
        file.parentFile?.mkdirs()

        writer = BufferedWriter(FileWriter(file), 65536)
        writer?.write("timestamp_ns,gyro_x,gyro_y,gyro_z,accel_x,accel_y,accel_z\n")

        startTimeNs = System.nanoTime()
        sampleCount.set(0L)

        sensorManager.registerListener(
            this, gyroSensor, SensorManager.SENSOR_DELAY_FASTEST, handler
        )
        sensorManager.registerListener(
            this, accelSensor, SensorManager.SENSOR_DELAY_FASTEST, handler
        )

        _state.value = _state.value.copy(
            isRecording = true,
            sampleCount = 0L,
            lastFilePath = file.absolutePath,
        )
    }

    fun stopRecording() {
        if (!isRecording.getAndSet(false)) return
        sensorManager.unregisterListener(this)
        // Close on the sensor thread so it can't race an in-flight write.
        val w = writer
        writer = null
        handler.post {
            try {
                w?.flush()
                w?.close()
            } catch (_: Exception) {}
        }
        _state.value = _state.value.copy(isRecording = false)
    }

    override fun onSensorChanged(event: SensorEvent) {
        val ts = event.timestamp // nanoseconds, monotonic
        val values = event.values.copyOf()

        when (event.sensor.type) {
            Sensor.TYPE_GYROSCOPE -> {
                _gyro.value = values
                pendingGyro = values
                pendingTimestampNs = ts
                latestGyroTimestampNs = ts
                updateRate(gyroTimestamps, gyroTsHead++, ts)
                gyroTsHead = gyroTsHead and 63

                // Flush row when both sensors have data for this timestamp window
                flushRowIfReady()
            }
            Sensor.TYPE_ACCELEROMETER -> {
                _accel.value = values
                pendingAccel = values
                latestAccelTimestampNs = ts
                updateRate(accelTimestamps, accelTsHead++, ts)
                accelTsHead = accelTsHead and 63
            }
        }

        // Update UI state at ~10 Hz (every 100 samples)
        val count = sampleCount.incrementAndGet()
        if (count % 100L == 0L) {
            _state.value = _state.value.copy(
                sampleCount = count,
                gyroRateHz = computeRate(gyroTimestamps),
                accelRateHz = computeRate(accelTimestamps),
                durationMs = (System.nanoTime() - startTimeNs) / 1_000_000L,
            )
        }
    }

    /**
     * Write one merged CSV row when a gyro event arrives.
     * Uses the last known accel reading (accelSensor rate is typically equal to gyro).
     */
    private fun flushRowIfReady() {
        if (!isRecording.get()) return
        val g = pendingGyro ?: return
        val a = pendingAccel ?: FloatArray(3) // zeros if accel not yet received
        val ts = pendingTimestampNs

        try {
            writer?.write(
                "$ts,${g[0]},${g[1]},${g[2]},${a[0]},${a[1]},${a[2]}\n"
            )
        } catch (_: java.io.IOException) {
            // Storage full or closed mid-stop; drop the row rather than crash
        }
    }

    /** Store timestamp at circular position and compute the rate estimate. */
    private fun updateRate(buf: LongArray, pos: Int, ts: Long) {
        buf[pos and 63] = ts
    }

    /** Compute Hz from the circular timestamp buffer (avg of last 64 intervals). */
    private fun computeRate(buf: LongArray): Float {
        var minTs = Long.MAX_VALUE
        var maxTs = Long.MIN_VALUE
        var nonZero = 0
        for (ts in buf) {
            if (ts == 0L) continue
            if (ts < minTs) minTs = ts
            if (ts > maxTs) maxTs = ts
            nonZero++
        }
        if (nonZero < 2) return 0f
        val spanNs = maxTs - minTs
        if (spanNs <= 0) return 0f
        return ((nonZero - 1).toFloat() / spanNs.toFloat()) * 1e9f
    }

    override fun onAccuracyChanged(sensor: Sensor, accuracy: Int) = Unit
}
