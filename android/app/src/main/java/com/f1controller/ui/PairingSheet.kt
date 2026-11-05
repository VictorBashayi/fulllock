package com.f1controller.ui

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.f1controller.network.PairingCode
import com.f1controller.network.PairingStatus

const val ERR_CODE_MISMATCH = "Code doesn't match this PC — check the code on the PC screen"
const val ERR_BAD_QR = "That QR isn't a FullLock pair code"
const val ERR_SAVE_FAILED = "Couldn't save the pairing — you'll need to pair again next time"

/** The five user-visible states, verbatim from the spec. */
fun pairingStatusText(s: PairingStatus): String = when (s) {
    PairingStatus.LOOKING -> "Looking for PC…"
    PairingStatus.NEEDS_PAIRING -> "PC found — pair"
    PairingStatus.CODE_MISMATCH -> ERR_CODE_MISMATCH
    PairingStatus.CODE_CHANGED -> "PC's pair code changed — re-pair"
    PairingStatus.PAIRED -> "Paired ✓"
}

/** Shared between MainActivity (writes) and the screens (read). */
class PairingUiState {
    var status by mutableStateOf(PairingStatus.LOOKING)
    var sheetOpen by mutableStateOf(false)
    var busy by mutableStateOf(false)
    var error by mutableStateOf<String?>(null)
}

/** Dialog rather than a bottom sheet: the app is landscape-only, where a sheet leaves almost no room. */
@Composable
fun PairingSheet(
    status: PairingStatus,
    onSubmit: (String) -> Unit,
    onScan: () -> Unit,
    busy: Boolean,
    error: String?,
    onDismiss: () -> Unit = {},
) {
    var typed by remember { mutableStateOf("") } // always cleaned (normalized-so-far), shown formatted
    AlertDialog(
        onDismissRequest = { if (!busy) onDismiss() },
        title = { Text(pairingStatusText(status).let { if (status == PairingStatus.CODE_MISMATCH) "Pair with PC" else it }) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Scan the code on the FullLock PC window, or type it.", fontSize = 13.sp)
                OutlinedButton(onClick = onScan, enabled = !busy, modifier = Modifier.fillMaxWidth()) { Text("Scan QR") }
                OutlinedTextField(
                    value = PairingCode.format(typed),
                    onValueChange = { typed = PairingCode.clean(it) },
                    label = { Text("Pair code") },
                    singleLine = true,
                    enabled = !busy,
                    keyboardOptions = KeyboardOptions(
                        capitalization = KeyboardCapitalization.Characters,
                        autoCorrect = false,
                        keyboardType = KeyboardType.Ascii,
                    ),
                    modifier = Modifier.fillMaxWidth(),
                )
                if (busy) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                    Text("Pairing…", fontSize = 13.sp)
                } else error?.let { Text(it, fontSize = 13.sp, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            Button(onClick = { onSubmit(typed) }, enabled = !busy && typed.length == PairingCode.LENGTH) { Text("Pair") }
        },
        dismissButton = { TextButton(onClick = onDismiss, enabled = !busy) { Text("Cancel") } },
    )
}
