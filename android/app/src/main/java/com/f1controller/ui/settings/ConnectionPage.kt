package com.f1controller.ui.settings

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.network.DiscoveryListener
import com.f1controller.network.PairingStatus
import com.f1controller.network.UdpTransport
import com.f1controller.ui.pairingStatusText
import com.f1controller.ui.theme.Condensed
import com.f1controller.ui.theme.Palette

@Composable
fun RowScope.ConnectionPage(
    transport: UdpTransport,
    discovery: DiscoveryListener,
    pairingStatus: PairingStatus,
    onRepair: () -> Unit,
    onForget: () -> Unit,
) {
    val tState by transport.state.collectAsState()
    val found by discovery.found.collectAsState()
    var autoConnect by remember { mutableStateOf(transport.autoConnect) }
    val paired = transport.auth != null

    PreviewCard {
        Canvas(Modifier.size(150.dp, 64.dp)) {
            val ink = Palette.Text
            drawRoundRect(ink, Offset(6f, 14f), Size(30f, 50f), CornerRadius(7f), style = Stroke(3f))
            drawRoundRect(ink, Offset(size.width - 62f, 8f), Size(56f, 38f), CornerRadius(5f), style = Stroke(3f))
            drawLine(ink, Offset(size.width - 46f, 56f), Offset(size.width - 22f, 56f), 3f)
            drawLine(
                if (tState.isConnected) Palette.Cyan else Palette.PanelBorder,
                Offset(46f, 38f), Offset(size.width - 72f, 38f), 3f,
                pathEffect = PathEffect.dashPathEffect(floatArrayOf(8f, 8f))
            )
        }
        Text(found ?: "—", fontFamily = Condensed, fontSize = 16.sp, color = Palette.Text)
        ValuePill(connectionSummary(paired, tState.isConnected, tState.rttAvgMs))
    }

    SettingsGroup(Modifier.weight(1f).verticalScroll(rememberScrollState())) {
        SettingsRow("Paired PC", pairingStatusText(pairingStatus)) {
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinePill("Re-pair", onRepair)
                OutlinePill("Forget", onForget)
            }
        }
        SettingsRow("Connect when the app opens", "Finds your PC on this Wi-Fi by itself") {
            PaletteSwitch(autoConnect) { autoConnect = it; transport.autoConnect = it }
        }
        SettingsRow(
            "Connection",
            tState.error ?: if (tState.isActive) "Sending inputs" else "Paused",
            lineColor = if (tState.error != null) Palette.Red else Palette.TextDim,
            divider = false,
        ) {
            PrimaryPill(
                if (tState.isActive) "Disconnect" else "Connect",
                onClick = {
                    when {
                        transport.isRunning() -> transport.disconnect()
                        transport.auth == null -> onRepair() // unpaired: connect() would do nothing
                        else -> transport.connect()
                    }
                },
                color = if (tState.isActive) Palette.Red else Palette.Cyan,
            )
        }
    }
}
