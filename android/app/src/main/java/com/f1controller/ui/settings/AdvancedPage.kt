package com.f1controller.ui.settings

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ChevronRight
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.input.SteeringProcessor
import com.f1controller.network.DiscoveryListener
import com.f1controller.network.UdpTransport
import com.f1controller.ui.theme.Palette
import kotlinx.coroutines.delay

@Composable
fun RowScope.AdvancedPage(
    transport: UdpTransport,
    discovery: DiscoveryListener,
    steeringProcessor: SteeringProcessor,
    onOpenLogger: () -> Unit,
) {
    val tState by transport.state.collectAsState()
    val found by discovery.found.collectAsState()
    var serverIp by remember { mutableStateOf(transport.serverIp) }
    LaunchedEffect(found) { serverIp = transport.serverIp } // discovery may have moved the transport
    var serverPort by remember { mutableStateOf(transport.serverPort.toString()) }
    var axis by remember { mutableStateOf(steeringProcessor.steeringAxis) }
    var beta by remember { mutableFloatStateOf(steeringProcessor.madgwick.beta) }
    // Steering numbers aren't Compose state; sample them for the debug card
    val tick by produceState(0) { while (true) { delay(100); value++ } }

    PreviewCard {
        key(tick) {
            Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
                DebugLine("Packets sent", "${tState.packetsSent}")
                DebugLine("Pongs", "${tState.pongReceived}")
                DebugLine("RTT avg", "${tState.rttAvgMs.toInt()} ms")
                DebugLine("RTT p99", "${tState.rttP99Ms.toInt()} ms")
                DebugLine("Send rate", "${tState.sendRateHz.toInt()} Hz")
                DebugLine("Angle", "${steeringProcessor.currentAngleDeg.toInt()}°")
                DebugLine("Steer", "${steeringProcessor.currentSteer}")
                DebugLine("Beta", String.format("%.3f", steeringProcessor.madgwick.beta))
            }
        }
    }

    SettingsGroup(Modifier.weight(1f).verticalScroll(rememberScrollState())) {
        SettingsRow("Server IP", "Filled in when your PC is found") {
            AddressField(serverIp, Modifier.width(150.dp)) { serverIp = it; transport.serverIp = it }
        }
        SettingsRow("Server port", "Filled in when your PC is found") {
            AddressField(serverPort, Modifier.width(90.dp)) {
                serverPort = it
                it.toIntOrNull()?.let { port -> transport.serverPort = port }
            }
        }
        val axes = SteeringProcessor.SteeringAxis.entries
        SettingsRow(
            "Steering axis", "Horizon is the one to use; others are for testing",
            below = {
                PaletteChips(axes.map { it.name.lowercase().replaceFirstChar(Char::uppercase) }, axes.indexOf(axis)) {
                    axis = axes[it]
                    steeringProcessor.steeringAxis = axes[it]
                    steeringProcessor.centerOffsetRad = 0f // old centre was measured on a different axis
                }
            }
        )
        SettingsRow(
            "Filter gain", "Higher trusts gravity more: less drift, more shake",
            below = { PaletteSlider(beta, 0.01f..1f) { beta = it; steeringProcessor.madgwick.beta = it } },
        ) { ValuePill(String.format("%.3f", beta)) }
        SettingsRow("IMU logger", "Record raw sensor data to a file", divider = false) {
            IconButton(onClick = onOpenLogger) { Icon(Icons.Default.ChevronRight, "Open IMU logger", tint = Palette.TextDim) }
        }
    }
}

@Composable
private fun DebugLine(label: String, value: String) {
    Row(Modifier.width(166.dp)) {
        Text(label, fontSize = 12.sp, color = Palette.TextDim, modifier = Modifier.weight(1f))
        Text(value, fontSize = 12.sp, fontFamily = FontFamily.Monospace, color = Palette.Text)
    }
}

@Composable
private fun AddressField(value: String, modifier: Modifier, onChange: (String) -> Unit) {
    OutlinedTextField(
        value = value, onValueChange = onChange, singleLine = true, modifier = modifier,
        textStyle = LocalTextStyle.current.copy(fontFamily = FontFamily.Monospace, fontSize = 14.sp),
        colors = OutlinedTextFieldDefaults.colors(
            focusedBorderColor = Palette.Cyan, unfocusedBorderColor = Palette.PanelBorder,
            focusedTextColor = Palette.Text, unfocusedTextColor = Palette.Text, cursorColor = Palette.Cyan,
        ),
    )
}
