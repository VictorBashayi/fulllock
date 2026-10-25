package com.f1controller.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.input.SteeringProcessor
import com.f1controller.network.UdpTransport

/**
 * Settings Screen — configures connection, Madgwick AHRS, and steering parameters.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun SettingsScreen(
    transport: UdpTransport,
    steeringProcessor: SteeringProcessor,
) {
    var serverIp by remember { mutableStateOf(transport.serverIp) }
    var serverPort by remember { mutableStateOf(transport.serverPort.toString()) }
    var beta by remember { mutableFloatStateOf(steeringProcessor.madgwick.beta) }
    var axis by remember { mutableStateOf(steeringProcessor.steeringAxis) }
    var lockAngle by remember { mutableFloatStateOf(steeringProcessor.lockAngleDeg) }
    var deadzone by remember { mutableFloatStateOf(steeringProcessor.deadzoneDeg) }
    var curve by remember { mutableFloatStateOf(steeringProcessor.curve) }
    var autoConnect by remember { mutableStateOf(transport.autoConnect) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp)
    ) {
        Text(
            text = "Settings",
            color = MaterialTheme.colorScheme.onBackground,
            fontSize = 20.sp,
            fontWeight = FontWeight.Bold
        )

        // ---- Connection Settings ----
        SectionCard("Connection") {
            OutlinedTextField(
                value = serverIp,
                onValueChange = {
                    serverIp = it
                    transport.serverIp = it
                },
                label = { Text("Server IP") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            Spacer(modifier = Modifier.height(8.dp))
            OutlinedTextField(
                value = serverPort,
                onValueChange = {
                    serverPort = it
                    it.toIntOrNull()?.let { port -> transport.serverPort = port }
                },
                label = { Text("Server Port") },
                modifier = Modifier.fillMaxWidth(),
                singleLine = true,
            )
            Spacer(modifier = Modifier.height(8.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Switch(
                    checked = autoConnect,
                    onCheckedChange = {
                        autoConnect = it
                        transport.autoConnect = it
                    }
                )
                Spacer(modifier = Modifier.width(10.dp))
                Text("Connect automatically when the app opens", fontSize = 13.sp,
                    color = MaterialTheme.colorScheme.onSurface)
            }
            Spacer(modifier = Modifier.height(12.dp))

            val tState by transport.state.collectAsState()
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(
                    onClick = {
                        if (transport.isRunning()) {
                            transport.disconnect()
                        } else {
                            transport.connect()
                        }
                    },
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (tState.isActive)
                            MaterialTheme.colorScheme.error
                        else
                            MaterialTheme.colorScheme.secondary
                    ),
                    shape = RoundedCornerShape(8.dp)
                ) {
                    Text(if (tState.isActive) "Disconnect" else "Connect")
                }
            }
            val status = when {
                tState.error != null -> tState.error!!
                tState.isConnected -> "Receiver responding"
                tState.isActive -> "Sending — no reply from receiver yet"
                else -> "Idle"
            }
            Spacer(modifier = Modifier.height(6.dp))
            Text(
                status,
                fontSize = 12.sp,
                color = if (tState.error != null) MaterialTheme.colorScheme.error
                        else MaterialTheme.colorScheme.onSurfaceVariant
            )
        }

        // ---- Madgwick AHRS Settings ----
        SectionCard("Madgwick AHRS Filter") {
            Text(
                "Beta (filter gain): ${String.format("%.3f", beta)}",
                fontSize = 13.sp,
                color = MaterialTheme.colorScheme.onSurface
            )
            Text(
                "Higher = more accelerometer trust, less drift, but more noise from movement",
                fontSize = 11.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Slider(
                value = beta,
                onValueChange = {
                    beta = it
                    steeringProcessor.madgwick.beta = it
                },
                valueRange = 0.01f..1.0f,
                modifier = Modifier.fillMaxWidth()
            )
        }

        // ---- Steering Settings ----
        SectionCard("Steering") {
            // Axis selection
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Text("Steering axis:", fontSize = 13.sp, color = MaterialTheme.colorScheme.onSurface)
                SteeringProcessor.SteeringAxis.entries.forEach { option ->
                    FilterChip(
                        selected = axis == option,
                        onClick = {
                            axis = option
                            steeringProcessor.steeringAxis = option
                            // Old center was measured on a different axis
                            steeringProcessor.centerOffsetRad = 0f
                        },
                        label = { Text(option.name.lowercase().replaceFirstChar { it.uppercase() }) }
                    )
                }
            }
            Text(
                "Tilt = turn the phone like a wheel (recommended). Roll/Yaw are raw Euler angles.",
                fontSize = 11.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Spacer(modifier = Modifier.height(12.dp))

            // Lock-to-lock angle
            Text(
                "Lock angle: ±${lockAngle.toInt()}° (${(lockAngle * 2).toInt()}° total)",
                fontSize = 13.sp,
                color = MaterialTheme.colorScheme.onSurface
            )
            Slider(
                value = lockAngle,
                onValueChange = {
                    lockAngle = it
                    steeringProcessor.lockAngleDeg = it
                },
                valueRange = 30f..180f,
                modifier = Modifier.fillMaxWidth()
            )

            Spacer(modifier = Modifier.height(8.dp))

            // Deadzone
            Text(
                "Deadzone: ${String.format("%.1f", deadzone)}°",
                fontSize = 13.sp,
                color = MaterialTheme.colorScheme.onSurface
            )
            Slider(
                value = deadzone,
                onValueChange = {
                    deadzone = it
                    steeringProcessor.deadzoneDeg = it
                },
                valueRange = 0f..10f,
                modifier = Modifier.fillMaxWidth()
            )

            Spacer(modifier = Modifier.height(8.dp))

            // Response curve
            val quarter = SteeringProcessor.applyCurve(0.25f, curve) * 100f
            val half = SteeringProcessor.applyCurve(0.5f, curve) * 100f
            Text(
                "Center smoothing: ${(curve * 100).toInt()}%",
                fontSize = 13.sp,
                color = MaterialTheme.colorScheme.onSurface
            )
            Text(
                "Turning the phone ¼ of the way to lock gives ${quarter.toInt()}% steering, " +
                    "½ gives ${half.toInt()}%. Higher = calmer on straights, same full lock.",
                fontSize = 11.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Slider(
                value = curve,
                onValueChange = {
                    curve = it
                    steeringProcessor.curve = it
                },
                valueRange = 0f..1f,
                modifier = Modifier.fillMaxWidth()
            )

            Spacer(modifier = Modifier.height(12.dp))

            // Calibrate Center
            Button(
                onClick = { steeringProcessor.calibrateCenter() },
                colors = ButtonDefaults.buttonColors(containerColor = MaterialTheme.colorScheme.tertiary),
                shape = RoundedCornerShape(8.dp)
            ) {
                Icon(Icons.Default.CenterFocusStrong, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(modifier = Modifier.width(6.dp))
                Text("Set Center (Calibrate)", color = MaterialTheme.colorScheme.onTertiary)
            }
            Text(
                "Hold the phone in your desired neutral position and tap to set as center",
                fontSize = 11.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Spacer(modifier = Modifier.height(8.dp))

            // Reset filter
            OutlinedButton(
                onClick = { steeringProcessor.reset() },
                shape = RoundedCornerShape(8.dp)
            ) {
                Icon(Icons.Default.RestartAlt, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(modifier = Modifier.width(6.dp))
                Text("Reset Filter & Calibration")
            }
        }

        // ---- Live Debug Info ----
        SectionCard("Live Debug") {
            val tState by transport.state.collectAsState()
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                DebugRow("Packets sent", "${tState.packetsSent}")
                DebugRow("Pongs received", "${tState.pongReceived}")
                DebugRow("RTT avg", "${tState.rttAvgMs.toInt()} ms")
                DebugRow("RTT p99", "${tState.rttP99Ms.toInt()} ms")
                DebugRow("Send rate", "${tState.sendRateHz.toInt()} Hz")
                DebugRow("Steering angle", "${steeringProcessor.currentAngleDeg.toInt()}°")
                DebugRow("Steering value", "${steeringProcessor.currentSteer}")
                DebugRow("Madgwick beta", "${steeringProcessor.madgwick.beta}")
            }
        }

        Spacer(modifier = Modifier.height(80.dp))
    }
}

@Composable
private fun SectionCard(title: String, content: @Composable ColumnScope.() -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        shape = RoundedCornerShape(12.dp)
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Text(
                text = title,
                fontWeight = FontWeight.Bold,
                fontSize = 14.sp,
                color = MaterialTheme.colorScheme.primary
            )
            Spacer(modifier = Modifier.height(12.dp))
            content()
        }
    }
}

@Composable
private fun DebugRow(label: String, value: String) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(label, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Text(value, fontSize = 12.sp, fontWeight = FontWeight.Medium, color = MaterialTheme.colorScheme.onSurface)
    }
}
