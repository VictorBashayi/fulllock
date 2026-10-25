package com.f1controller.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.network.UdpTransport

/**
 * Controller Screen (Phase 1 + 2)
 *
 * Landscape layout:
 * - Top bar: Status, RTT, steering angle visualization
 * - Left side: Brake pedal (vertical slider, 0-255)
 * - Right side: Throttle pedal (vertical slider, 0-255)
 * - Bottom center: F1 racing buttons
 *
 * Xbox controller button bits (XUSB_BUTTON flags):
 *   0x0001 = DPAD_UP       0x0100 = LEFT_SHOULDER
 *   0x0002 = DPAD_DOWN     0x0200 = RIGHT_SHOULDER
 *   0x0004 = DPAD_LEFT     0x0010 = START
 *   0x0008 = DPAD_RIGHT    0x0020 = BACK
 *   0x0040 = LEFT_THUMB    0x0080 = RIGHT_THUMB
 *   0x1000 = A             0x2000 = B
 *   0x4000 = X             0x8000 = Y
 */
@Composable
fun ControllerScreen(
    transport: UdpTransport,
    onSteeringAngleDisplay: () -> Float,
) {
    val tState by transport.state.collectAsState()
    val haptic = LocalHapticFeedback.current

    var throttleVal by remember { mutableFloatStateOf(0f) }
    var brakeVal by remember { mutableFloatStateOf(0f) }
    var buttonMask by remember { mutableIntStateOf(0) }

    // Sync pedal & button values to transport
    LaunchedEffect(throttleVal, brakeVal, buttonMask) {
        transport.throttle = (throttleVal * 255f).toInt()
        transport.brake = (brakeVal * 255f).toInt()
        transport.buttons = buttonMask
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .padding(8.dp)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            // ---- Top Status Bar ----
            StatusBar(tState, transport, onSteeringAngleDisplay)

            Spacer(modifier = Modifier.height(8.dp))

            // ---- Main Control Area ----
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                // Left Pedal: Brake
                VerticalPedal(
                    label = "BRAKE",
                    value = brakeVal,
                    onValueChanged = { brakeVal = it },
                    color = Color(0xFFE10600)
                )

                // Center: Steering vis + Buttons
                CenterPanel(
                    onSteeringAngleDisplay = onSteeringAngleDisplay,
                    buttonMask = buttonMask,
                    onButtonChanged = { buttonMask = it },
                )

                // Right Pedal: Throttle
                VerticalPedal(
                    label = "THROTTLE",
                    value = throttleVal,
                    onValueChanged = { throttleVal = it },
                    color = Color(0xFF00D95F)
                )
            }
        }
    }
}

@Composable
private fun StatusBar(
    tState: UdpTransport.TransportState,
    transport: UdpTransport,
    onSteeringAngleDisplay: () -> Float,
) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .height(40.dp)
            .background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(8.dp))
            .padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            Box(
                modifier = Modifier
                    .size(10.dp)
                    .clip(CircleShape)
                    .background(if (tState.isConnected) Color(0xFF00D95F) else Color(0xFFE10600))
            )
            Text(
                text = if (tState.isConnected) "Connected" else "Disconnected",
                color = MaterialTheme.colorScheme.onSurface,
                fontSize = 13.sp,
                fontWeight = FontWeight.Medium
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
            Text(
                text = "RTT: ${tState.rttAvgMs.toInt()}ms (p99: ${tState.rttP99Ms.toInt()}ms)",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                fontSize = 12.sp
            )
            Text(
                text = "TX: ${tState.sendRateHz.toInt()} Hz",
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                fontSize = 12.sp
            )
            val angle = onSteeringAngleDisplay()
            Text(
                text = "Steer: ${angle.toInt()}°",
                color = MaterialTheme.colorScheme.primary,
                fontSize = 13.sp,
                fontWeight = FontWeight.Bold
            )
        }
    }
}

@Composable
private fun RowScope.CenterPanel(
    onSteeringAngleDisplay: () -> Float,
    buttonMask: Int,
    onButtonChanged: (Int) -> Unit,
) {
    Column(
        modifier = Modifier
            .weight(1f)
            .fillMaxHeight()
            .padding(horizontal = 16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.SpaceEvenly
    ) {
        // Steering Angle Visualizer Circle
        Box(
            modifier = Modifier
                .size(120.dp)
                .clip(CircleShape)
                .background(MaterialTheme.colorScheme.surface)
                .border(3.dp, MaterialTheme.colorScheme.primary, CircleShape),
            contentAlignment = Alignment.Center
        ) {
            val angle = onSteeringAngleDisplay()
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                Icon(
                    Icons.Default.Refresh,
                    contentDescription = null,
                    tint = MaterialTheme.colorScheme.primary,
                    modifier = Modifier.size(32.dp)
                )
                Spacer(modifier = Modifier.height(2.dp))
                Text(
                    text = "${angle.toInt()}°",
                    color = MaterialTheme.colorScheme.onSurface,
                    fontSize = 20.sp,
                    fontWeight = FontWeight.Bold
                )
                Text(
                    text = "GYRO STEER",
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    fontSize = 9.sp
                )
            }
        }

        // Button Grid
        Column(
            verticalArrangement = Arrangement.spacedBy(6.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            // Row 1: Gear buttons + DRS/ERS
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                F1Button("GEAR ↓", 0x0100, buttonMask, onButtonChanged)  // LEFT_SHOULDER
                F1Button("GEAR ↑", 0x0200, buttonMask, onButtonChanged)  // RIGHT_SHOULDER
                F1Button("DRS",    0x4000, buttonMask, onButtonChanged)  // X
                F1Button("ERS",    0x8000, buttonMask, onButtonChanged)  // Y
            }
            // Row 2: A / B / Start / Back
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                F1Button("A",      0x1000, buttonMask, onButtonChanged)  // A
                F1Button("B",      0x2000, buttonMask, onButtonChanged)  // B
                F1Button("MENU",   0x0010, buttonMask, onButtonChanged)  // START
                F1Button("MFD",    0x0020, buttonMask, onButtonChanged)  // BACK
            }
        }
    }
}

/**
 * Vertical pedal control — drag up from bottom to apply pressure, release to return to zero.
 * Value: 0f (released) to 1f (fully pressed).
 */
@Composable
fun VerticalPedal(
    label: String,
    value: Float,
    onValueChanged: (Float) -> Unit,
    color: Color
) {
    Box(
        modifier = Modifier
            .width(80.dp)
            .fillMaxHeight()
            .clip(RoundedCornerShape(12.dp))
            .background(MaterialTheme.colorScheme.surface)
            .border(1.dp, MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(12.dp))
            .pointerInput(Unit) {
                detectDragGestures(
                    onDragStart = { offset ->
                        val v = 1f - (offset.y / size.height).coerceIn(0f, 1f)
                        onValueChanged(v)
                    },
                    onDrag = { change, _ ->
                        val v = 1f - (change.position.y / size.height).coerceIn(0f, 1f)
                        onValueChanged(v)
                    },
                    onDragEnd = { onValueChanged(0f) },
                    onDragCancel = { onValueChanged(0f) }
                )
            },
        contentAlignment = Alignment.BottomCenter
    ) {
        // Background fill bar
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .fillMaxHeight(value.coerceIn(0f, 1f))
                .background(color.copy(alpha = 0.35f))
        )
        // Labels
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(8.dp),
            verticalArrangement = Arrangement.SpaceBetween,
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(
                text = label,
                color = MaterialTheme.colorScheme.onSurface,
                fontSize = 10.sp,
                fontWeight = FontWeight.Bold
            )
            Text(
                text = "${(value * 100).toInt()}%",
                color = color,
                fontSize = 18.sp,
                fontWeight = FontWeight.Bold
            )
            Icon(
                Icons.Default.SwapVert,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.size(20.dp)
            )
        }
    }
}

/**
 * Momentary push-button mapped to an Xbox controller button bit.
 * Press and hold to activate; release to deactivate.
 */
@Composable
fun F1Button(
    label: String,
    bit: Int,
    currentMask: Int,
    onMaskChanged: (Int) -> Unit
) {
    val isPressed = (currentMask and bit) != 0
    val haptic = LocalHapticFeedback.current

    Surface(
        modifier = Modifier
            .height(40.dp)
            .pointerInput(bit, currentMask) {
                awaitPointerEventScope {
                    while (true) {
                        // Wait for finger down
                        val down = awaitPointerEvent()
                        if (down.changes.any { it.pressed }) {
                            onMaskChanged(currentMask or bit)
                            haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                            // Wait for finger up
                            do {
                                val event = awaitPointerEvent()
                            } while (event.changes.any { it.pressed })
                            onMaskChanged(currentMask and bit.inv())
                        }
                    }
                }
            },
        color = if (isPressed)
            MaterialTheme.colorScheme.primary
        else
            MaterialTheme.colorScheme.surfaceVariant,
        shape = RoundedCornerShape(8.dp),
        tonalElevation = if (isPressed) 0.dp else 2.dp,
    ) {
        Box(
            modifier = Modifier.padding(horizontal = 14.dp, vertical = 8.dp),
            contentAlignment = Alignment.Center
        ) {
            Text(
                text = label,
                fontSize = 11.sp,
                fontWeight = FontWeight.Bold,
                color = if (isPressed) Color.White else MaterialTheme.colorScheme.onSurface
            )
        }
    }
}
