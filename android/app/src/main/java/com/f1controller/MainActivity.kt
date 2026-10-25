package com.f1controller

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.HandlerThread
import android.os.Process
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.navigation.NavHostController
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.currentBackStackEntryAsState
import androidx.navigation.compose.rememberNavController
import com.f1controller.input.SteeringProcessor
import com.f1controller.network.UdpTransport
import com.f1controller.sensor.SensorLogger
import com.f1controller.ui.*
import com.f1controller.ui.theme.F1ControllerTheme

/**
 * Main entry point for F1 Phone Controller.
 *
 * Manages lifecycle of:
 *   - SensorLogger (Phase 0) — records raw IMU to CSV
 *   - UdpTransport (Phase 1) — sends control packets at 250 Hz
 *   - SteeringProcessor (Phase 2) — Madgwick AHRS → steering value
 *   - WifiLock — keeps Wi-Fi in low-latency mode during use
 *
 * The sensor callback feeds both the logger (when recording) and the
 * steering processor simultaneously. The steering processor output is
 * fed to UdpTransport on every IMU sample for minimum latency.
 */
class MainActivity : ComponentActivity(), SensorEventListener {

    private lateinit var sensorManager: SensorManager
    private lateinit var wifiManager: WifiManager

    private lateinit var sensorLogger: SensorLogger
    private val transport = UdpTransport()
    private val steeringProcessor = SteeringProcessor()

    // Sensor callbacks run here instead of the UI thread, so Compose frames
    // can't delay steering updates.
    private val sensorThread = HandlerThread("imu", Process.THREAD_PRIORITY_URGENT_DISPLAY)
        .apply { start() }
    private val sensorHandler = Handler(sensorThread.looper)

    private var wifiLock: WifiManager.WifiLock? = null
    private var gyroSensor: Sensor? = null
    private var accelSensor: Sensor? = null

    // Shared sensor state for the steering processor
    @Volatile private var latestGyro = FloatArray(3)
    @Volatile private var latestAccel = FloatArray(3)
    @Volatile private var latestGyroTs = 0L
    @Volatile private var hasAccel = false
    @Volatile private var sensorListenerActive = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Keep screen on while app is active
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

        // Hide system bars for full-screen controller experience.
        // window.insetsController is null before the decor view exists (pre-setContent);
        // accessing window.decorView here creates it, and WindowCompat covers API < 30.
        WindowCompat.getInsetsController(window, window.decorView).apply {
            hide(WindowInsetsCompat.Type.systemBars())
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }

        sensorManager = getSystemService(Context.SENSOR_SERVICE) as SensorManager
        wifiManager = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager

        sensorLogger = SensorLogger(sensorManager, sensorHandler)
        loadSettings()

        gyroSensor = sensorManager.getDefaultSensor(Sensor.TYPE_GYROSCOPE)
        accelSensor = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)

        // Acquire Wi-Fi low-latency lock
        acquireWifiLock()

        setContent {
            F1ControllerTheme {
                F1ControllerApp(
                    sensorLogger = sensorLogger,
                    transport = transport,
                    steeringProcessor = steeringProcessor,
                )
            }
        }
    }

    override fun onResume() {
        super.onResume()
        // Register sensor listener for steering (always active when app is visible)
        sensorManager.registerListener(this, gyroSensor, SensorManager.SENSOR_DELAY_FASTEST, sensorHandler)
        sensorManager.registerListener(this, accelSensor, SensorManager.SENSOR_DELAY_FASTEST, sensorHandler)
        sensorListenerActive = true
    }

    override fun onPause() {
        super.onPause()
        // Unregister steering sensor listener (the logger has its own registration)
        sensorManager.unregisterListener(this)
        sensorListenerActive = false
        // Steering is frozen while paused; send neutral input instead of the last value
        transport.steer = 0
        transport.throttle = 0
        transport.brake = 0
        transport.buttons = 0
        saveSettings()
    }

    override fun onDestroy() {
        super.onDestroy()
        sensorLogger.stopRecording()
        transport.disconnect()
        releaseWifiLock()
        sensorThread.quitSafely()
    }

    // ---- Settings persistence ----

    private fun loadSettings() {
        val prefs = getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        transport.serverIp = prefs.getString("server_ip", transport.serverIp) ?: transport.serverIp
        transport.serverPort = prefs.getInt("server_port", transport.serverPort)
        steeringProcessor.madgwick.beta = prefs.getFloat("beta", steeringProcessor.madgwick.beta)
        steeringProcessor.lockAngleDeg = prefs.getFloat("lock_angle", steeringProcessor.lockAngleDeg)
        steeringProcessor.deadzoneDeg = prefs.getFloat("deadzone", steeringProcessor.deadzoneDeg)
        steeringProcessor.centerOffsetRad = prefs.getFloat("center_offset", steeringProcessor.centerOffsetRad)
        prefs.getString("axis", null)?.let { name ->
            SteeringProcessor.SteeringAxis.entries.firstOrNull { it.name == name }
                ?.let { steeringProcessor.steeringAxis = it }
        }
    }

    private fun saveSettings() {
        getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit()
            .putString("server_ip", transport.serverIp)
            .putInt("server_port", transport.serverPort)
            .putFloat("beta", steeringProcessor.madgwick.beta)
            .putFloat("lock_angle", steeringProcessor.lockAngleDeg)
            .putFloat("deadzone", steeringProcessor.deadzoneDeg)
            .putFloat("center_offset", steeringProcessor.centerOffsetRad)
            .putString("axis", steeringProcessor.steeringAxis.name)
            .apply()
    }

    // ---- SensorEventListener for steering (always-on, separate from logger) ----

    override fun onSensorChanged(event: SensorEvent) {
        when (event.sensor.type) {
            Sensor.TYPE_GYROSCOPE -> {
                latestGyro = event.values.copyOf()
                latestGyroTs = event.timestamp

                // The filter seeds itself from the first accel reading; wait for one
                if (!hasAccel) return

                // Process steering on the gyro event (it's the higher-priority axis)
                val steer = steeringProcessor.process(
                    latestGyro[0], latestGyro[1], latestGyro[2],
                    latestAccel[0], latestAccel[1], latestAccel[2],
                    latestGyroTs
                )
                // Feed to transport immediately (minimum latency)
                transport.steer = steer
            }
            Sensor.TYPE_ACCELEROMETER -> {
                latestAccel = event.values.copyOf()
                hasAccel = true
            }
        }
    }

    override fun onAccuracyChanged(sensor: Sensor, accuracy: Int) = Unit

    // ---- Wi-Fi Lock ----

    private fun acquireWifiLock() {
        wifiLock = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            wifiManager.createWifiLock(WifiManager.WIFI_MODE_FULL_LOW_LATENCY, "F1Controller:LowLatency")
        } else {
            @Suppress("DEPRECATION")
            wifiManager.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "F1Controller:HighPerf")
        }
        wifiLock?.acquire()
    }

    private fun releaseWifiLock() {
        wifiLock?.let {
            if (it.isHeld) it.release()
        }
        wifiLock = null
    }

    private companion object {
        const val PREFS_NAME = "f1controller"
    }
}

// ---- Navigation ----

enum class NavTab(val route: String, val label: String, val icon: ImageVector) {
    CONTROLLER("controller", "Controller", Icons.Default.SportsEsports),
    LOGGER("logger", "IMU Logger", Icons.Default.DataUsage),
    SETTINGS("settings", "Settings", Icons.Default.Settings),
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun F1ControllerApp(
    sensorLogger: SensorLogger,
    transport: UdpTransport,
    steeringProcessor: SteeringProcessor,
) {
    val navController = rememberNavController()
    val currentBackStack by navController.currentBackStackEntryAsState()
    val currentRoute = currentBackStack?.destination?.route

    Scaffold(
        bottomBar = {
            NavigationBar(
                containerColor = MaterialTheme.colorScheme.surface,
                tonalElevation = 0.dp
            ) {
                NavTab.entries.forEach { tab ->
                    NavigationBarItem(
                        selected = currentRoute == tab.route,
                        onClick = {
                            if (currentRoute != tab.route) {
                                navController.navigate(tab.route) {
                                    popUpTo(navController.graph.startDestinationId) { saveState = true }
                                    launchSingleTop = true
                                    restoreState = true
                                }
                            }
                        },
                        icon = { Icon(tab.icon, contentDescription = tab.label) },
                        label = { Text(tab.label, fontSize = 11.sp) },
                    )
                }
            }
        }
    ) { padding ->
        NavHost(
            navController = navController,
            startDestination = NavTab.CONTROLLER.route,
            modifier = Modifier.padding(padding)
        ) {
            composable(NavTab.CONTROLLER.route) {
                ControllerScreen(
                    transport = transport,
                    onSteeringAngleDisplay = { steeringProcessor.currentAngleDeg }
                )
            }
            composable(NavTab.LOGGER.route) {
                LoggerScreen(sensorLogger = sensorLogger)
            }
            composable(NavTab.SETTINGS.route) {
                SettingsScreen(
                    transport = transport,
                    steeringProcessor = steeringProcessor,
                )
            }
        }
    }
}
