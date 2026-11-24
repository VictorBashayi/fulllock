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
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.lifecycle.lifecycleScope
import com.google.zxing.client.android.Intents
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
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
import com.f1controller.engineer.EngineerRadio
import com.f1controller.engineer.EngineerSettings
import com.f1controller.input.SteeringProcessor
import com.f1controller.network.DiscoveryListener
import com.f1controller.network.PacketAuth
import com.f1controller.network.PairingCode
import com.f1controller.network.PairingStatus
import com.f1controller.network.PairingStore
import com.f1controller.network.pairingStatus
import com.f1controller.network.UdpTransport
import com.f1controller.network.shouldSwitchServer
import com.f1controller.sensor.SensorLogger
import com.f1controller.ui.*
import com.f1controller.ui.theme.F1ControllerTheme
import com.f1controller.ui.settings.SettingsPage
import com.f1controller.ui.settings.startPage
import com.f1controller.ui.settings.AboutPage
import com.f1controller.ui.settings.shouldAutoOpenPairing
import com.f1controller.ui.settings.shouldShowWalkthrough
import com.f1controller.ui.settings.ConnectionPage
import com.f1controller.ui.settings.AdvancedPage
import com.f1controller.ui.settings.SettingsScreen
import com.f1controller.ui.settings.SteeringPage
import com.f1controller.ui.settings.EngineerPage
import com.f1controller.ui.settings.PedalsPage
import com.f1controller.ui.settings.LayoutPage
import androidx.compose.runtime.saveable.rememberSaveable

/**
 * Main entry point for F1 Phone Controller.
 *
 * Manages lifecycle of:
 *   - SensorLogger (Phase 0) — records raw IMU to CSV
 *   - UdpTransport (Phase 1) — sends control packets at 250 Hz
 *   - SteeringProcessor (Phase 2) — Madgwick AHRS → steering value
 *   - WifiLock — keeps Wi-Fi in low-latency mode during use
 *   - DiscoveryListener — follows the PC to a new IP (e.g. a hotspot reassigning it)
 *
 * The sensor callback feeds both the logger (when recording) and the
 * steering processor simultaneously. The steering processor output is
 * fed to UdpTransport on every IMU sample for minimum latency.
 */
class MainActivity : ComponentActivity(), SensorEventListener {

    private lateinit var sensorManager: SensorManager
    private lateinit var wifiManager: WifiManager

    private lateinit var sensorLogger: SensorLogger
    private lateinit var pairingStore: PairingStore
    private val transport = UdpTransport()
    private val pairingUi = PairingUiState()
    private val steeringProcessor = SteeringProcessor()
    private val pedalSettings = PedalSettings()
    private val engineerSettings = EngineerSettings()
    private lateinit var engineerRadio: EngineerRadio

    // Sensor callbacks run here instead of the UI thread, so Compose frames
    // can't delay steering updates.
    private val sensorThread = HandlerThread("imu", Process.THREAD_PRIORITY_URGENT_DISPLAY)
        .apply { start() }
    private val sensorHandler = Handler(sensorThread.looper)

    private var wifiLock: WifiManager.WifiLock? = null
    // Some phones drop broadcast packets (the discovery beacon) without this
    private var multicastLock: WifiManager.MulticastLock? = null
    private val discovery = DiscoveryListener(::onReceiverFound)
    private var gyroSensor: Sensor? = null
    private var accelSensor: Sensor? = null

    // Shared sensor state for the steering processor
    @Volatile private var latestGyro = FloatArray(3)
    @Volatile private var latestAccel = FloatArray(3)
    @Volatile private var latestGyroTs = 0L
    @Volatile private var hasAccel = false
    @Volatile private var sensorListenerActive = false

    // Pairing status inputs
    @Volatile private var lastBeaconNs = 0L
    private var lastSwitchNs = 0L
    private var attemptUntilMs = 0L          // pairingAttempt stays true until this (3 s after a failed pair)
    private var sheetAutoOpened = false
    private var walkthroughShowing by mutableStateOf(false)

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
        pairingStore = PairingStore(this)
        transport.auth = pairingStore.key()?.let(::PacketAuth) // null on any keystore trouble = unpaired
        decideWalkthrough()
        // Persist only the address a session actually got a valid pong from
        lifecycleScope.launch { transport.connectedServer.collect { it?.let(::saveServer) } }
        transport.engineerLevel = { engineerSettings.level }
        engineerRadio = EngineerRadio(this, engineerSettings, lifecycleScope)
        lifecycleScope.launch { transport.radio.collect { engineerRadio.play(it) } }
        if (transport.autoConnect) transport.connect()
        discovery.start()

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
                    pedalSettings = pedalSettings,
                    engineerSettings = engineerSettings,
                    engineerRadio = engineerRadio,
                    discovery = discovery,
                    pairingUi = pairingUi,
                    computePairingStatus = ::computePairingStatus,
                    onPairSubmit = ::submitPairing,
                    onForgetPc = ::forgetPc,
                    walkthroughShowing = walkthroughShowing,
                    onWalkthroughDone = ::markWalkthroughSeen,
                    onShowWalkthrough = { walkthroughShowing = true },
                    versionName = packageManager.getPackageInfo(packageName, 0).versionName ?: "",
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
        discovery.stop()
        engineerRadio.shutdown()
        releaseWifiLock()
        sensorThread.quitSafely()
    }

    // ---- Receiver discovery ----

    /** Called on the discovery thread for every beacon heard. */
    private fun onReceiverFound(ip: String, port: Int) {
        lastBeaconNs = System.nanoTime()
        val connected = transport.state.value.isConnected
        // Cooldown: a beacon flood from another host must not keep resetting the session before a pong can arrive
        val now = System.nanoTime()
        if (!connected && transport.serverIp != ip && now - lastSwitchNs < SWITCH_COOLDOWN_NS) return
        if (!shouldSwitchServer(connected, transport.serverIp, transport.serverPort, ip, port)) return
        lastSwitchNs = now
        transport.serverIp = ip
        transport.serverPort = port // candidate only; persisted after a valid pong
        // The send threads captured the old address; restart them on the new one
        if (transport.isRunning()) {
            transport.disconnect()
            transport.connect()
        }
    }

    // ---- First launch ----

    private fun decideWalkthrough() {
        val prefs = getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        val seen = if (prefs.contains(PREF_WALKTHROUGH_SEEN)) prefs.getBoolean(PREF_WALKTHROUGH_SEEN, false) else null
        val hasSavedSettings = prefs.contains("lock_angle") || prefs.contains("auto_connect")
        walkthroughShowing = shouldShowWalkthrough(seen, pairingStore.isPaired, hasSavedSettings)
        // Decide once: upgrades are marked seen, a first run is marked unseen until Done/Skip
        if (seen == null) prefs.edit().putBoolean(PREF_WALKTHROUGH_SEEN, !walkthroughShowing).apply()
    }

    private fun markWalkthroughSeen() {
        walkthroughShowing = false
        getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit().putBoolean(PREF_WALKTHROUGH_SEEN, true).apply()
    }

    // ---- Pairing ----

    private fun ageMs(ns: Long): Long? = if (ns == 0L) null else (System.nanoTime() - ns) / 1_000_000

    /** Called from the UI ticker (~250 ms): ages change without any state emission. */
    private fun computePairingStatus(): PairingStatus {
        val attempt = pairingUi.busy || System.currentTimeMillis() < attemptUntilMs
        // During/after a failed attempt there is no stored key, but the mismatch rule needs one
        val s = pairingStatus(transport.auth != null || attempt, attempt, ageMs(lastBeaconNs), ageMs(transport.lastValidPongNs), ageMs(transport.connectStartNs))
        if (shouldAutoOpenPairing(s, sheetAutoOpened, walkthroughShowing)) { sheetAutoOpened = true; pairingUi.sheetOpen = true }
        return s
    }

    private fun submitPairing(code: String) {
        pairingUi.busy = true
        pairingUi.error = null
        lifecycleScope.launch {
            val ok = try { tryPair(code) } finally { pairingUi.busy = false }
            if (ok) pairingUi.sheetOpen = false
            else { attemptUntilMs = System.currentTimeMillis() + 3000; pairingUi.error = ERR_CODE_MISMATCH }
        }
    }

    private fun forgetPc() {
        pairingStore.forget()
        transport.auth = null
        transport.disconnect()
    }

    /** Derives the key, tries it against the PC for up to 2 s, stores it only if a valid pong came back. */
    suspend fun tryPair(normalizedCode: String): Boolean {
        val raw = withContext(Dispatchers.Default) { PacketAuth.deriveKey(normalizedCode) }
        val previous = transport.auth
        transport.auth = PacketAuth(PacketAuth.keyFromRaw(raw))
        transport.disconnect()
        transport.connect()
        var waited = 0
        while (transport.lastValidPongNs == 0L && waited < 2000) { delay(50); waited += 50 }
        if (transport.lastValidPongNs != 0L) {
            try {
                pairingStore.save(raw.copyOf())
                transport.auth = pairingStore.key()?.let(::PacketAuth) ?: transport.auth
            } catch (e: Exception) {
                // keep the in-memory key for this run
                Toast.makeText(this, ERR_SAVE_FAILED, Toast.LENGTH_LONG).show()
            }
            raw.fill(0)
            return true
        }
        raw.fill(0)
        transport.auth = previous
        transport.disconnect()
        if (previous != null) transport.connect()
        return false
    }

    // ---- Settings persistence ----

    private fun loadSettings() {
        val prefs = getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        transport.serverIp = prefs.getString("server_ip", transport.serverIp) ?: transport.serverIp
        transport.serverPort = prefs.getInt("server_port", transport.serverPort)
        transport.autoConnect = prefs.getBoolean("auto_connect", transport.autoConnect)
        steeringProcessor.curve = prefs.getFloat("curve", steeringProcessor.curve)
        pedalSettings.brakeAsButton = prefs.getBoolean("brake_as_button", pedalSettings.brakeAsButton)
        pedalSettings.throttleAsButton = prefs.getBoolean("throttle_as_button", pedalSettings.throttleAsButton)
        pedalSettings.swapPedals = prefs.getBoolean("swap_pedals", pedalSettings.swapPedals)
        engineerSettings.level = prefs.getInt("engineer_level", engineerSettings.level)
        engineerSettings.voice = prefs.getBoolean("engineer_voice", engineerSettings.voice)
        engineerSettings.vibrate = prefs.getBoolean("engineer_vibrate", engineerSettings.vibrate)
        steeringProcessor.madgwick.beta = prefs.getFloat("beta", steeringProcessor.madgwick.beta)
        steeringProcessor.lockAngleDeg = prefs.getFloat("lock_angle", steeringProcessor.lockAngleDeg)
        steeringProcessor.deadzoneDeg = prefs.getFloat("deadzone", steeringProcessor.deadzoneDeg)
        steeringProcessor.centerOffsetRad = prefs.getFloat("center_offset", steeringProcessor.centerOffsetRad)
        prefs.getString("axis", null)?.let { name ->
            SteeringProcessor.SteeringAxis.entries.firstOrNull { it.name == name }
                ?.let { steeringProcessor.steeringAxis = it }
        }
        // TILT was the default before HORIZON existed: move it over once; a later manual TILT sticks
        if (!prefs.getBoolean("axis_horizon_migrated", false) &&
            steeringProcessor.steeringAxis == SteeringProcessor.SteeringAxis.TILT
        ) {
            steeringProcessor.steeringAxis = SteeringProcessor.SteeringAxis.HORIZON
            steeringProcessor.centerOffsetRad = 0f  // measured on the old axis
        }
    }

    /** Only an authenticated server is stored; an unverified candidate or typed IP never overwrites it. */
    private fun saveServer(server: Pair<String, Int>) {
        getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit()
            .putString("server_ip", server.first)
            .putInt("server_port", server.second)
            .apply()
    }

    private fun saveSettings() {
        transport.connectedServer.value?.let(::saveServer)
        getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit()
            .putBoolean("auto_connect", transport.autoConnect)
            .putFloat("curve", steeringProcessor.curve)
            .putBoolean("brake_as_button", pedalSettings.brakeAsButton)
            .putBoolean("throttle_as_button", pedalSettings.throttleAsButton)
            .putBoolean("swap_pedals", pedalSettings.swapPedals)
            .putInt("engineer_level", engineerSettings.level)
            .putBoolean("engineer_voice", engineerSettings.voice)
            .putBoolean("engineer_vibrate", engineerSettings.vibrate)
            .putFloat("beta", steeringProcessor.madgwick.beta)
            .putFloat("lock_angle", steeringProcessor.lockAngleDeg)
            .putFloat("deadzone", steeringProcessor.deadzoneDeg)
            .putFloat("center_offset", steeringProcessor.centerOffsetRad)
            .putString("axis", steeringProcessor.steeringAxis.name)
            .putBoolean("axis_horizon_migrated", true)
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
        multicastLock = wifiManager.createMulticastLock("F1Controller:Discovery").apply {
            setReferenceCounted(false)
            acquire()
        }
    }

    private fun releaseWifiLock() {
        wifiLock?.let {
            if (it.isHeld) it.release()
        }
        wifiLock = null
        multicastLock?.let {
            if (it.isHeld) it.release()
        }
        multicastLock = null
    }

    companion object {
        const val PREFS_NAME = "f1controller"
        private const val PREF_WALKTHROUGH_SEEN = "walkthrough_seen"
        private const val SWITCH_COOLDOWN_NS = 3_000_000_000L
    }
}

// ---- Navigation ----

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun F1ControllerApp(
    sensorLogger: SensorLogger,
    transport: UdpTransport,
    steeringProcessor: SteeringProcessor,
    pedalSettings: PedalSettings,
    engineerSettings: EngineerSettings,
    engineerRadio: EngineerRadio,
    discovery: DiscoveryListener,
    pairingUi: PairingUiState,
    computePairingStatus: () -> PairingStatus,
    onPairSubmit: (String) -> Unit,
    onForgetPc: () -> Unit,
    walkthroughShowing: Boolean,
    onWalkthroughDone: () -> Unit,
    onShowWalkthrough: () -> Unit,
    versionName: String,
) {
    if (walkthroughShowing) {
        // Drawn instead of the app; the pairing sheet waits for it (shouldAutoOpenPairing)
        Walkthrough(onDone = onWalkthroughDone)
        return
    }
    LaunchedEffect(Unit) {
        while (true) { pairingUi.status = computePairingStatus(); delay(250) }
    }
    val scanner = rememberLauncherForActivityResult(ScanContract()) { result ->
        val text = result.contents ?: return@rememberLauncherForActivityResult // cancelled / permission denied
        val code = PairingCode.fromQrText(text)
        if (code == null) pairingUi.error = ERR_BAD_QR else onPairSubmit(code)
    }
    if (pairingUi.sheetOpen) PairingSheet(
        status = pairingUi.status,
        onSubmit = onPairSubmit,
        onScan = {
            scanner.launch(
                ScanOptions().setDesiredBarcodeFormats(ScanOptions.QR_CODE).setBeepEnabled(false)
                    .addExtra(Intents.Scan.SCAN_TYPE, Intents.Scan.MIXED_SCAN) // PC QR is light-on-dark
                    .setPrompt("Scan the code on the FullLock PC window").setOrientationLocked(false)
            )
        },
        busy = pairingUi.busy,
        error = pairingUi.error,
        onDismiss = { pairingUi.sheetOpen = false },
    )
    val openSheet = { pairingUi.error = null; pairingUi.sheetOpen = true }
    val navController = rememberNavController()
    val radioCall by engineerRadio.current.collectAsState()
    // Hoisted above the NavHost so a trip to the IMU logger comes back to the same page
    var settingsPage by rememberSaveable { mutableStateOf<SettingsPage?>(null) }
    val paired = transport.auth != null // the status says LOOKING, not NEEDS_PAIRING, while the PC is away

    NavHost(navController = navController, startDestination = ROUTE_CONTROLLER) {
        composable(ROUTE_CONTROLLER) {
            ControllerScreen(
                transport = transport,
                onSteeringAngleDisplay = { steeringProcessor.currentAngleDeg },
                lockAngleDeg = { steeringProcessor.lockAngleDeg },
                pedalSettings = pedalSettings,
                radioCall = radioCall,
                onOpenSettings = {
                    settingsPage = startPage(paired, settingsPage)
                    navController.navigate(ROUTE_SETTINGS) { launchSingleTop = true }
                },
                pairingStatus = pairingUi.status,
                onRepair = openSheet,
            )
        }
        composable(ROUTE_LOGGER) {
            LoggerScreen(sensorLogger = sensorLogger)
        }
        composable(ROUTE_SETTINGS) {
            val page = settingsPage ?: startPage(paired, null)
            SettingsScreen(
                page = page,
                onPage = { settingsPage = it },
                pageContent = { p ->
                    when (p) {
                        SettingsPage.CONNECTION -> ConnectionPage(transport, discovery, pairingUi.status, openSheet, onForgetPc)
                        SettingsPage.ADVANCED -> AdvancedPage(transport, discovery, steeringProcessor) {
                            navController.navigate(ROUTE_LOGGER)
                        }
                        SettingsPage.PEDALS -> PedalsPage(pedalSettings)
                        SettingsPage.LAYOUT -> LayoutPage(pedalSettings)
                        SettingsPage.STEERING -> SteeringPage(steeringProcessor)
                        SettingsPage.ENGINEER -> EngineerPage(engineerSettings, engineerRadio)
                        SettingsPage.ABOUT -> AboutPage(versionName, onShowWalkthrough)
                    }
                },
            )
        }
    }
}

private const val ROUTE_CONTROLLER = "controller"
private const val ROUTE_SETTINGS = "settings"
private const val ROUTE_LOGGER = "logger"
