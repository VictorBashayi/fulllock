package com.f1controller.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.waitForUpOrCancellation
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
import kotlinx.coroutines.delay

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

    var throttleVal by remember { mutableFloatStateOf(0f) }
    var brakeVal by remember { mutableFloatStateOf(0f) }
    var buttonMask by remember { mutableIntStateOf(0) }

    // The steering angle isn't Compose state; sample it at ~30 Hz for display
    val angle by produceState(0f) {
        while (true) {
            value = onSteeringAngleDisplay()
            delay(33)
        }
    }

    // Inputs are written to the transport straight from the touch callbacks
    // (not via a LaunchedEffect) so they go out on the next 4 ms packet.
    val onButton: (Int, Boolean) -> Unit = { bit, pressed ->
        buttonMask = if (pressed) buttonMask or bit else buttonMask and bit.inv()
        transport.buttons = buttonMask
    }

    // Release everything if the screen leaves composition mid-press
    DisposableEffect(Unit) {
        onDispose {
            transport.throttle = 0
            transport.brake = 0
            transport.buttons = 0
        }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.background)
            .padding(8.dp)
    ) {
        Column(modifier = Modifier.fillMaxSize()) {
            // ---- Top Status Bar ----
            StatusBar(tState, angle)

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
                    onValueChanged = {
                        brakeVal = it
                        transport.brake = (it * 255f).toInt()
                    },
                    color = Color(0xFFE10600)
                )

                // Center: Steering vis + Buttons
                CenterPanel(
                    angle = angle,
                    buttonMask = buttonMask,
                    onButton = onButton,
                )

                // Right Pedal: Throttle
                VerticalPedal(
                    label = "THROTTLE",
                    value = throttleVal,
                    onValueChanged = {
                        throttleVal = it
                        transport.throttle = (it * 255f).toInt()
                    },
                    color = Color(0xFF00D95F)
                )
            }
        }
    }
}

@Composable
private fun StatusBar(
    tState: UdpTransport.TransportState,
    angle: Float,
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
    angle: Float,
    buttonMask: Int,
    onButton: (bit: Int, pressed: Boolean) -> Unit,
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
                F1Button("GEAR ↓", 0x0100, buttonMask, onButton)  // LEFT_SHOULDER
                F1Button("GEAR ↑", 0x0200, buttonMask, onButton)  // RIGHT_SHOULDER
                F1Button("DRS",    0x4000, buttonMask, onButton)  // X
                F1Button("ERS",    0x8000, buttonMask, onButton)  // Y
            }
            // Row 2: A / B / Start / Back
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                F1Button("A",      0x1000, buttonMask, onButton)  // A
                F1Button("B",      0x2000, buttonMask, onButton)  // B
                F1Button("MENU",   0x0010, buttonMask, onButton)  // START
                F1Button("MFD",    0x0020, buttonMask, onButton)  // BACK
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
    val currentOnValueChanged by rememberUpdatedState(onValueChanged)

    Box(
        modifier = Modifier
            .width(80.dp)
            .fillMaxHeight()
            .clip(RoundedCornerShape(12.dp))
            .background(MaterialTheme.colorScheme.surface)
            .border(1.dp, MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(12.dp))
            .pointerInput(Unit) {
                // Raw down/move/up rather than detectDragGestures: a drag only
                // starts after touch slop, so a plain press wouldn't register.
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false)
                    fun toValue(y: Float) = 1f - (y / size.height).coerceIn(0f, 1f)
                    currentOnValueChanged(toValue(down.position.y))
                    while (true) {
                        val event = awaitPointerEvent()
                        val change = event.changes.firstOrNull { it.id == down.id } ?: break
                        if (!change.pressed) break
                        currentOnValueChanged(toValue(change.position.y))
                        change.consume()
                    }
                    currentOnValueChanged(0f)
                }
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
    onButton: (bit: Int, pressed: Boolean) -> Unit,
) {
    val isPressed = (currentMask and bit) != 0
    val haptic = LocalHapticFeedback.current
    val currentOnButton by rememberUpdatedState(onButton)

    Surface(
        modifier = Modifier
            .height(40.dp)
            // Keyed on `bit` only: keying on the mask restarted this handler
            // mid-press, so the release was never seen and buttons stuck down.
            .pointerInput(bit) {
                awaitEachGesture {
                    awaitFirstDown(requireUnconsumed = false)
                    currentOnButton(bit, true)
                    haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                    try {
                        waitForUpOrCancellation()
                    } finally {
                        currentOnButton(bit, false)
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
