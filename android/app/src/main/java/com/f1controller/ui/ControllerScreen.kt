package com.f1controller.ui

import com.f1controller.ui.theme.Condensed
import com.f1controller.ui.theme.Palette
import com.f1controller.ui.settings.Pedal
import com.f1controller.ui.settings.pedalSlots

import androidx.compose.animation.Crossfade
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.gestures.detectTapGestures
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
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Path
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.graphics.drawscope.rotate
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.network.PairingStatus
import com.f1controller.network.RadioCall
import com.f1controller.network.UdpTransport
import kotlinx.coroutines.delay
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin

/**
 * Controller Screen — full-screen landscape driving layout.
 *
 *   [GEAR ↓][ERS]      [BACK][START]      [DRS][GEAR ↑]
 *   [BRAKE] [D-pad]    steering wheel     [YXBA][THROTTLE]
 *
 * Xbox controller button bits (XUSB_BUTTON flags):
 *   0x0001 = DPAD_UP       0x0100 = LEFT_SHOULDER   (gear down)
 *   0x0002 = DPAD_DOWN     0x0200 = RIGHT_SHOULDER  (gear up)
 *   0x0004 = DPAD_LEFT     0x0010 = START
 *   0x0008 = DPAD_RIGHT    0x0020 = BACK
 *   0x0040 = LEFT_THUMB    (ERS)
 *   0x0080 = RIGHT_THUMB   (DRS)
 *   0x1000 = A             0x2000 = B
 *   0x4000 = X             0x8000 = Y
 */

private const val BTN_DPAD_UP = 0x0001
private const val BTN_DPAD_DOWN = 0x0002
private const val BTN_DPAD_LEFT = 0x0004
private const val BTN_DPAD_RIGHT = 0x0008
private const val BTN_START = 0x0010
private const val BTN_BACK = 0x0020
private const val BTN_ERS = 0x0040     // LEFT_THUMB
private const val BTN_DRS = 0x0080     // RIGHT_THUMB
private const val BTN_GEAR_DOWN = 0x0100
private const val BTN_GEAR_UP = 0x0200
private const val BTN_A = 0x1000
private const val BTN_B = 0x2000
private const val BTN_X = 0x4000
private const val BTN_Y = 0x8000

/** Pedal height as a fraction of the main row; they stay pinned to the bottom. */
private const val PEDAL_HEIGHT = 0.72f

@Composable
fun ControllerScreen(
    transport: UdpTransport,
    onSteeringAngleDisplay: () -> Float,
    lockAngleDeg: () -> Float,
    pedalSettings: PedalSettings,
    radioCall: RadioCall?,
    onOpenSettings: () -> Unit,
    pairingStatus: PairingStatus,
    onRepair: () -> Unit,
) {
    val tState by transport.state.collectAsState()
    // Null until the receiver forwards game telemetry (game running with UDP telemetry on)
    val game by transport.telemetry.collectAsState()
    val race by transport.race.collectAsState()
    val pbs by transport.pbs.collectAsState()
    // Centre panel: 0 wheel, 1 personal bests, 2 timing — tap to cycle
    var centerPage by remember { mutableIntStateOf(0) }

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

    val gap = 12.dp

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(Brush.radialGradient(listOf(Palette.BgCenter, Palette.BgEdge)))
            .padding(horizontal = 20.dp, vertical = 14.dp),
        verticalArrangement = Arrangement.spacedBy(gap)
    ) {
        // ---- Top row ----
        Row(
            modifier = Modifier.fillMaxWidth().weight(0.27f),
            horizontalArrangement = Arrangement.spacedBy(gap)
        ) {
            Row(Modifier.weight(0.34f).fillMaxHeight(), horizontalArrangement = Arrangement.spacedBy(gap)) {
                GearButton(up = false, BTN_GEAR_DOWN, buttonMask, onButton, Modifier.weight(1.35f))
                PanelButton(BTN_ERS, buttonMask, onButton, Modifier.weight(1f).fillMaxHeight()) { pressed ->
                    val g = game
                    val overtake = g?.ersMode == 3
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text("ERS", fontFamily = Condensed, fontSize = 30.sp,
                            color = if (pressed || overtake) Palette.Yellow else Palette.Text)
                        Text(
                            if (g == null) "DEPLOY" else "${g.ersPct}% · ${ersModeName(g.ersMode)}",
                            fontFamily = Condensed, fontSize = 11.sp, letterSpacing = 2.sp,
                            color = if (overtake) Palette.Yellow else Palette.TextDim
                        )
                    }
                }
            }
            Column(
                Modifier.weight(0.32f).fillMaxHeight(),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                Row(horizontalArrangement = Arrangement.spacedBy(gap)) {
                    PillButton("BACK", BTN_BACK, buttonMask, onButton, Palette.Text)
                    PillButton("START", BTN_START, buttonMask, onButton, Palette.Cyan)
                }
                race?.let { InfoStrip(it) }
            }
            Row(Modifier.weight(0.34f).fillMaxHeight(), horizontalArrangement = Arrangement.spacedBy(gap)) {
                DrsButton(game, buttonMask, onButton, Modifier.weight(1f).fillMaxHeight())
                GearButton(up = true, BTN_GEAR_UP, buttonMask, onButton, Modifier.weight(1.35f))
            }
        }

        // Swap sides mirrors only the pedals; each keeps its own colour, label and transport field
        val slots = pedalSlots(pedalSettings.swapPedals)
        LaunchedEffect(pedalSettings.swapPedals) {
            // A pedal held across a swap would otherwise stay pressed
            brakeVal = 0f; throttleVal = 0f
            transport.brake = 0; transport.throttle = 0
        }
        @Composable
        fun RowScope.PedalSlot(pedal: Pedal) = when (pedal) {
            Pedal.BRAKE -> VerticalPedal(
                label = "BRAKE",
                value = brakeVal,
                onValueChanged = {
                    brakeVal = it
                    transport.brake = (it * 255f).toInt()
                },
                color = Palette.Red,
                asButton = pedalSettings.brakeAsButton,
                modifier = Modifier.weight(0.14f).fillMaxHeight(PEDAL_HEIGHT).align(Alignment.Bottom)
            )
            Pedal.THROTTLE -> VerticalPedal(
                label = "THROTTLE",
                value = throttleVal,
                onValueChanged = {
                    throttleVal = it
                    transport.throttle = (it * 255f).toInt()
                },
                color = Palette.Cyan,
                asButton = pedalSettings.throttleAsButton,
                modifier = Modifier.weight(0.14f).fillMaxHeight(PEDAL_HEIGHT).align(Alignment.Bottom)
            )
        }

        // ---- Main row ----
        Row(
            modifier = Modifier.fillMaxWidth().weight(0.73f),
            horizontalArrangement = Arrangement.spacedBy(gap),
            verticalAlignment = Alignment.CenterVertically
        ) {
            PedalSlot(slots.first)
            Box(Modifier.weight(0.23f).fillMaxHeight(), contentAlignment = Alignment.Center) {
                DPad(buttonMask, onButton)
            }
            Column(
                Modifier.weight(0.26f).fillMaxHeight(),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Box(
                    Modifier
                        .fillMaxWidth()
                        .weight(1f)
                        .pointerInput(Unit) { detectTapGestures { centerPage = (centerPage + 1) % 3 } },
                    contentAlignment = Alignment.Center
                ) {
                    when (centerPage) {
                        0 -> SteeringWheel(
                            angle = angle,
                            lockAngleDeg = lockAngleDeg(),
                            // Without telemetry the LEDs follow the throttle pedal instead
                            revLevel = game?.let { it.revLightsPct / 100f } ?: throttleVal,
                            gearText = game?.let { gearName(it.gear) } ?: "–",
                            modifier = Modifier.fillMaxSize()
                        )
                        1 -> PbView(race?.trackId ?: -1, pbs)
                        else -> TimingView(race)
                    }
                    // Engineer calls sit on top of the centre view; it's the biggest free space
                    Crossfade(
                        targetState = radioCall,
                        label = "engineer",
                        modifier = Modifier.align(Alignment.TopCenter).offset(y = (-10).dp)
                    ) { call ->
                        if (call != null) EngineerBanner(call)
                    }
                }
                Text(
                    text = game?.let { "${angle.toInt()}°  ·  ${it.speedKph} km/h" } ?: "${angle.toInt()}°",
                    fontFamily = FontFamily.Monospace,
                    fontSize = 16.sp,
                    color = Palette.TextDim
                )
                PageDots(centerPage, count = 3)
                if (pairingStatus != PairingStatus.PAIRED) {
                    Text(pairingStatusText(pairingStatus), fontSize = 11.sp, color = Palette.Yellow)
                    if (pairingStatus == PairingStatus.CODE_CHANGED || pairingStatus == PairingStatus.NEEDS_PAIRING)
                        TextButton(onClick = onRepair, contentPadding = PaddingValues(horizontal = 8.dp, vertical = 0.dp)) {
                            Text(if (pairingStatus == PairingStatus.CODE_CHANGED) "Re-pair" else "Pair", fontSize = 12.sp)
                        }
                }
                StatusPills(tState, lockAngleDeg(), onOpenSettings)
            }
            Box(Modifier.weight(0.23f).fillMaxHeight(), contentAlignment = Alignment.Center) {
                FaceButtons(buttonMask, onButton)
            }
            PedalSlot(slots.second)
        }
    }
}

private fun gearName(gear: Int) = when {
    gear < 0 -> "R"
    gear == 0 -> "N"
    else -> gear.toString()
}

private fun ersModeName(mode: Int) = when (mode) {
    1 -> "MED"
    2 -> "HOT"
    3 -> "OVT"
    else -> "OFF"
}

// ---- Buttons ----

/**
 * Press-and-hold mapped to an Xbox button bit: down sets it, up/cancel clears it.
 * Keyed on `bit` only: keying on the mask restarted this handler mid-press,
 * so the release was never seen and buttons stuck down.
 */
@Composable
private fun Modifier.holdButton(bit: Int, onButton: (bit: Int, pressed: Boolean) -> Unit): Modifier {
    val haptic = LocalHapticFeedback.current
    val currentOnButton by rememberUpdatedState(onButton)
    return pointerInput(bit) {
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
    }
}

/** Rounded dark panel that lights up with [accent] while held. */
@Composable
private fun PanelButton(
    bit: Int,
    mask: Int,
    onButton: (bit: Int, pressed: Boolean) -> Unit,
    modifier: Modifier = Modifier,
    accent: Color = Palette.Cyan,
    shape: RoundedCornerShape = RoundedCornerShape(18.dp),
    idleBorder: Color = Palette.PanelBorder,
    idleFill: Color = Palette.Panel,
    content: @Composable BoxScope.(pressed: Boolean) -> Unit,
) {
    val pressed = (mask and bit) != 0
    Box(
        modifier = modifier
            .clip(shape)
            .background(if (pressed) accent.copy(alpha = 0.22f) else idleFill)
            .border(if (pressed) 2.dp else 1.dp, if (pressed) accent else idleBorder, shape)
            .holdButton(bit, onButton),
        contentAlignment = Alignment.Center
    ) {
        content(pressed)
    }
}

@Composable
private fun GearButton(
    up: Boolean,
    bit: Int,
    mask: Int,
    onButton: (bit: Int, pressed: Boolean) -> Unit,
    modifier: Modifier,
) {
    PanelButton(bit, mask, onButton, modifier.fillMaxHeight(), shape = RoundedCornerShape(22.dp)) { pressed ->
        val chevron = if (up) Icons.Default.KeyboardArrowUp else Icons.Default.KeyboardArrowDown
        Row(
            modifier = Modifier.fillMaxSize().padding(horizontal = 18.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = if (up) Arrangement.End else Arrangement.Start
        ) {
            if (up) {
                Icon(chevron, null, tint = Palette.Cyan, modifier = Modifier.size(30.dp))
                Spacer(Modifier.weight(1f))
            }
            Column(horizontalAlignment = if (up) Alignment.End else Alignment.Start) {
                Text("GEAR", fontFamily = Condensed, fontSize = 12.sp, letterSpacing = 3.sp,
                    color = Palette.TextDim)
                Text(if (up) "UP" else "DOWN", fontFamily = Condensed, fontSize = 34.sp,
                    color = if (pressed) Palette.Cyan else Palette.Text)
            }
            if (!up) {
                Icon(chevron, null, tint = Palette.Cyan, modifier = Modifier.size(30.dp))
                Spacer(Modifier.weight(1f))
            }
        }
    }
}

/**
 * With telemetry: green READY only when the game allows DRS, solid when the
 * flap is open, grey otherwise. Without telemetry it just shows the press.
 */
@Composable
private fun DrsButton(
    game: UdpTransport.GameTelemetry?,
    mask: Int,
    onButton: (bit: Int, pressed: Boolean) -> Unit,
    modifier: Modifier,
) {
    val open = game?.drsOpen == true
    val available = game == null || game.drsAllowed || open
    val color = if (available) Palette.Green else Palette.TextDim
    PanelButton(
        BTN_DRS, mask, onButton, modifier,
        accent = color,
        shape = RoundedCornerShape(22.dp),
        idleBorder = if (open) color else color.copy(alpha = 0.6f),
        idleFill = color.copy(alpha = if (open) 0.25f else 0.08f),
    ) { pressed ->
        val status = when {
            game == null -> if (pressed) "OPEN" else "READY"
            open -> "OPEN"
            game.drsAllowed -> "READY"
            else -> "NOT READY"
        }
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text("DRS", fontFamily = Condensed, fontSize = 38.sp, color = color)
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(8.dp).clip(CircleShape).background(color))
                Spacer(Modifier.width(6.dp))
                Text(status, fontFamily = Condensed, fontSize = 12.sp,
                    letterSpacing = 2.sp, color = color)
            }
        }
    }
}

@Composable
private fun PillButton(
    label: String,
    bit: Int,
    mask: Int,
    onButton: (bit: Int, pressed: Boolean) -> Unit,
    color: Color,
) {
    PanelButton(
        bit, mask, onButton,
        Modifier.height(48.dp).widthIn(min = 80.dp),
        shape = RoundedCornerShape(24.dp),
    ) { pressed ->
        Text(label, fontFamily = Condensed, fontSize = 16.sp, letterSpacing = 3.sp,
            color = if (pressed) Palette.Cyan else color,
            modifier = Modifier.padding(horizontal = 20.dp))
    }
}

/** Cross-shaped D-pad for menu navigation (XUSB_GAMEPAD_DPAD_* bits). */
@Composable
private fun DPad(
    mask: Int,
    onButton: (bit: Int, pressed: Boolean) -> Unit,
) {
    BoxWithConstraints(Modifier.fillMaxWidth().aspectRatio(1f, matchHeightConstraintsFirst = true)) {
        val key = min(maxWidth.value, maxHeight.value).dp / 2.9f
        @Composable
        fun Key(bit: Int, icon: ImageVector, align: Alignment) {
            PanelButton(bit, mask, onButton, Modifier.size(key).align(align),
                shape = RoundedCornerShape(14.dp)) { pressed ->
                Icon(icon, null, tint = if (pressed) Palette.Cyan else Palette.Text,
                    modifier = Modifier.size(key * 0.45f))
            }
        }
        Key(BTN_DPAD_UP, Icons.Default.KeyboardArrowUp, Alignment.TopCenter)
        Key(BTN_DPAD_LEFT, Icons.Default.KeyboardArrowLeft, Alignment.CenterStart)
        Key(BTN_DPAD_RIGHT, Icons.Default.KeyboardArrowRight, Alignment.CenterEnd)
        Key(BTN_DPAD_DOWN, Icons.Default.KeyboardArrowDown, Alignment.BottomCenter)
    }
}

/** Xbox-style diamond: Y top, X left, B right, A bottom. */
@Composable
private fun FaceButtons(
    mask: Int,
    onButton: (bit: Int, pressed: Boolean) -> Unit,
) {
    BoxWithConstraints(Modifier.fillMaxWidth().aspectRatio(1f, matchHeightConstraintsFirst = true)) {
        val d = min(maxWidth.value, maxHeight.value).dp / 2.5f
        @Composable
        fun Face(label: String, bit: Int, color: Color, align: Alignment) {
            PanelButton(
                bit, mask, onButton, Modifier.size(d).align(align),
                accent = color,
                shape = CircleShape,
                idleBorder = color,
                idleFill = color.copy(alpha = 0.10f),
            ) {
                Text(label, fontFamily = Condensed, fontSize = (d.value * 0.42f).sp, color = color)
            }
        }
        Face("Y", BTN_Y, Palette.Yellow, Alignment.TopCenter)
        Face("X", BTN_X, Palette.Blue, Alignment.CenterStart)
        Face("B", BTN_B, Palette.Red, Alignment.CenterEnd)
        Face("A", BTN_A, Palette.Green, Alignment.BottomCenter)
    }
}

// ---- Pedals ----

/**
 * Vertical pedal control: press anywhere and drag; height = pressure. Release returns to zero.
 * With [asButton], any press is full pressure regardless of position.
 * Value: 0f (released) to 1f (fully pressed).
 */
@Composable
fun VerticalPedal(
    label: String,
    value: Float,
    onValueChanged: (Float) -> Unit,
    color: Color,
    asButton: Boolean,
    modifier: Modifier = Modifier,
) {
    val currentOnValueChanged by rememberUpdatedState(onValueChanged)
    val shape = RoundedCornerShape(26.dp)
    val v = value.coerceIn(0f, 1f)

    Box(
        modifier = modifier
            .clip(shape)
            .background(Palette.Panel)
            .border(1.dp, Palette.PanelBorder, shape)
            .drawBehind {
                // Faint horizontal grooves
                val step = 14.dp.toPx()
                var y = step
                while (y < size.height) {
                    drawLine(Color.White.copy(alpha = 0.04f), Offset(0f, y), Offset(size.width, y), 1.dp.toPx())
                    y += step
                }
            }
            .pointerInput(asButton) {
                // Raw down/move/up rather than detectDragGestures: a drag only
                // starts after touch slop, so a plain press wouldn't register.
                awaitEachGesture {
                    val down = awaitFirstDown(requireUnconsumed = false)
                    fun toValue(y: Float) = if (asButton) 1f else 1f - (y / size.height).coerceIn(0f, 1f)
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
        // Fill with a bright edge at the current pressure
        Box(
            modifier = Modifier
                .fillMaxWidth()
                .fillMaxHeight(v)
                .background(Brush.verticalGradient(listOf(color.copy(alpha = 0.12f), color.copy(alpha = 0.55f))))
                .drawBehind {
                    if (v > 0f) drawRect(color, size = Size(size.width, 3.dp.toPx()))
                }
        )
        // Dashed thumb target near the bottom
        Canvas(Modifier.fillMaxWidth(0.62f).aspectRatio(1f).padding(bottom = 16.dp)) {
            drawCircle(
                color = Color.White.copy(alpha = 0.25f),
                style = Stroke(1.dp.toPx(), pathEffect = PathEffect.dashPathEffect(floatArrayOf(6f, 6f)))
            )
        }
        Column(
            modifier = Modifier.fillMaxSize().padding(top = 16.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(label, fontFamily = Condensed, fontSize = 13.sp, letterSpacing = 3.sp, color = Palette.Text)
            Spacer(Modifier.height(6.dp))
            Text(
                buildAnnotatedString {
                    append("${(v * 100).toInt()}")
                    withStyle(SpanStyle(fontSize = 14.sp)) { append("%") }
                },
                fontFamily = Condensed, fontSize = 30.sp, color = color
            )
        }
    }
}

// ---- Center ----

/**
 * Wheel graphic rotated by the gyro angle, under a tick arc with a marker
 * at the current position within the lock. Rev LEDs follow the throttle
 * (there's no telemetry coming back from the PC yet).
 */
@Composable
private fun SteeringWheel(
    angle: Float,
    lockAngleDeg: Float,
    revLevel: Float,
    gearText: String,
    modifier: Modifier = Modifier,
) {
    Box(modifier, contentAlignment = Alignment.Center) {
        val arcSpan = 70f
        Canvas(Modifier.fillMaxSize()) {
            val r = min(size.width * 0.46f, size.height * 0.62f)
            val c = Offset(size.width / 2, size.height * 0.62f)
            val marker = (angle / lockAngleDeg.coerceAtLeast(1f)).coerceIn(-1f, 1f) * arcSpan
            for (i in -14..14) {
                val a = i * 5f
                val major = i % 7 == 0
                val lit = (a <= 0 && a >= marker) || (a >= 0 && a <= marker)
                val len = if (i == 0) 18.dp.toPx() else if (major) 9.dp.toPx() else 5.dp.toPx()
                tick(c, r, a, len, if (i == 0) Palette.Text else if (lit) Palette.Cyan else Palette.TextDim.copy(alpha = 0.45f),
                    if (i == 0) 2.5.dp.toPx() else 1.5.dp.toPx())
            }
            // Marker triangle pointing at the arc
            rotate(marker, c) {
                val tip = Offset(c.x, c.y - r + 2.dp.toPx())
                val w = 7.dp.toPx()
                val h = 14.dp.toPx()
                drawPath(Path().apply {
                    moveTo(tip.x, tip.y)
                    lineTo(tip.x - w, tip.y - h)
                    lineTo(tip.x + w, tip.y - h)
                    close()
                }, Palette.Cyan)
            }
        }
        BoxWithConstraints(
            Modifier
                .fillMaxWidth(0.62f)
                .aspectRatio(2.0f)
                .offset(y = 18.dp)
                .graphicsLayer { rotationZ = angle }
        ) {
            val textSize: TextUnit = (maxHeight.value * 0.22f).sp
            Canvas(Modifier.fillMaxSize()) { drawWheel(revLevel) }
            Text(gearText, fontFamily = FontFamily.Monospace, fontSize = textSize, color = Palette.Cyan,
                modifier = Modifier.align(Alignment.Center))
        }
    }
}

private fun DrawScope.tick(c: Offset, r: Float, deg: Float, len: Float, color: Color, width: Float) {
    val rad = Math.toRadians((deg - 90f).toDouble())
    val dir = Offset(cos(rad).toFloat(), sin(rad).toFloat())
    drawLine(color, c + dir * (r - len), c + dir * r, width)
}

private fun DrawScope.drawWheel(revLevel: Float) {
    val w = size.width
    val h = size.height
    val stroke = Stroke(1.5.dp.toPx())
    val gripFill = Color(0xFF0D161D)
    val bodyFill = Color(0xFF152029)

    // Grips
    val gripW = w * 0.2f
    val gripH = h * 0.95f
    for (x in listOf(0f, w - gripW)) {
        val tl = Offset(x, (h - gripH) / 2)
        val sz = Size(gripW, gripH)
        drawRoundRect(gripFill, tl, sz, CornerRadius(gripW / 2))
        drawRoundRect(Palette.PanelBorder, tl, sz, CornerRadius(gripW / 2), style = stroke)
    }
    // Body
    val bodyTl = Offset(w * 0.14f, h * 0.18f)
    val bodySz = Size(w * 0.72f, h * 0.64f)
    drawRoundRect(bodyFill, bodyTl, bodySz, CornerRadius(h * 0.18f))
    drawRoundRect(Palette.PanelBorder, bodyTl, bodySz, CornerRadius(h * 0.18f), style = stroke)
    // Display
    val dispTl = Offset(w * 0.34f, h * 0.36f)
    val dispSz = Size(w * 0.32f, h * 0.3f)
    drawRoundRect(Color(0xFF05090D), dispTl, dispSz, CornerRadius(h * 0.06f))
    drawRoundRect(Palette.Cyan.copy(alpha = 0.3f), dispTl, dispSz, CornerRadius(h * 0.06f), style = stroke)
    // Rev LEDs: 3 green, 3 red, 2 blue
    val leds = listOf(Palette.Green, Palette.Green, Palette.Green, Palette.Red, Palette.Red, Palette.Red, Palette.Blue, Palette.Blue)
    val lit = (revLevel * leds.size).toInt()
    val ledR = h * 0.035f
    val spacing = w * 0.045f
    val startX = w / 2 - spacing * (leds.size - 1) / 2
    leds.forEachIndexed { i, col ->
        drawCircle(if (i < lit) col else col.copy(alpha = 0.25f), ledR, Offset(startX + i * spacing, h * 0.27f))
    }
    // Rotary dots
    val dotR = h * 0.045f
    drawCircle(Palette.Yellow, dotR, Offset(w * 0.24f, h * 0.7f))
    drawCircle(Palette.Cyan, dotR, Offset(w * 0.3f, h * 0.7f))
    drawCircle(Palette.Red, dotR, Offset(w * 0.7f, h * 0.62f))
    drawCircle(Palette.Green, dotR, Offset(w * 0.76f, h * 0.58f))
}

@Composable
private fun StatusPills(
    tState: UdpTransport.TransportState,
    lockAngleDeg: Float,
    onOpenSettings: () -> Unit,
) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp)
    ) {
        Pill {
            Box(Modifier.size(8.dp).clip(CircleShape)
                .background(if (tState.isConnected) Palette.Green else Palette.Red))
            Spacer(Modifier.width(6.dp))
            Text(
                if (tState.isConnected) "${tState.rttAvgMs.toInt()} ms" else "OFFLINE",
                fontFamily = FontFamily.Monospace, fontSize = 12.sp, color = Palette.Text,
                maxLines = 1, softWrap = false
            )
        }
        Pill {
            Text("LOCK ${(lockAngleDeg * 2).toInt()}°", fontFamily = FontFamily.Monospace,
                fontSize = 12.sp, color = Palette.Text, maxLines = 1, softWrap = false)
        }
        IconButton(onClick = onOpenSettings, modifier = Modifier.size(32.dp)) {
            Icon(Icons.Default.Settings, contentDescription = "Settings", tint = Palette.TextDim,
                modifier = Modifier.size(18.dp))
        }
    }
}

@Composable
private fun Pill(content: @Composable RowScope.() -> Unit) {
    val shape = RoundedCornerShape(20.dp)
    Row(
        modifier = Modifier
            .height(32.dp)
            .clip(shape)
            .background(Palette.Panel)
            .border(1.dp, Palette.PanelBorder, shape)
            .padding(horizontal = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        content = content
    )
}
