package com.f1controller.engineer

import android.content.Context
import android.os.Build
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import com.f1controller.network.RadioCall
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import java.util.Locale

/**
 * Delivers engineer calls: banner ([current]), voice and vibration, filtered by
 * the chattiness in [settings]. [scope] must run on the main thread.
 *
 * The banner stays up for at least [BANNER_MIN_MS] and until the call has been
 * spoken; a newer call replaces it.
 */
class EngineerRadio(
    context: Context,
    private val settings: EngineerSettings,
    private val scope: CoroutineScope,
) {
    private val _current = MutableStateFlow<RadioCall?>(null)
    val current: StateFlow<RadioCall?> = _current

    private val _voiceAvailable = MutableStateFlow(false)
    val voiceAvailable: StateFlow<Boolean> = _voiceAvailable

    private val vibrator: Vibrator =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S)
            (context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE) as VibratorManager).defaultVibrator
        else
            @Suppress("DEPRECATION") (context.getSystemService(Context.VIBRATOR_SERVICE) as Vibrator)

    // Which shown call is still being spoken / still inside its minimum time
    private var shown = 0
    private var speaking = false
    private var minTimeUp = false
    // Priority of each utterance queued or speaking, by utterance ID
    private val inFlight = HashMap<String, Int>()

    private val tts: TextToSpeech = TextToSpeech(context.applicationContext) { status ->
        scope.launch { onTtsReady(status == TextToSpeech.SUCCESS) }
    }

    private fun onTtsReady(ok: Boolean) {
        val usable = ok && tts.setLanguage(Locale.UK) >= TextToSpeech.LANG_AVAILABLE
        if (!usable) return
        tts.setSpeechRate(SPEECH_RATE)
        tts.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
            override fun onStart(utteranceId: String) {}
            override fun onDone(utteranceId: String) = spoken(utteranceId)
            override fun onStop(utteranceId: String, interrupted: Boolean) = spoken(utteranceId)
            @Deprecated("Deprecated in Java")
            override fun onError(utteranceId: String) = spoken(utteranceId)
        })
        _voiceAvailable.value = true
    }

    /** Shows/speaks/vibrates [call] if the chattiness allows it; [force] ignores chattiness (test call). */
    fun play(call: RadioCall, force: Boolean = false) {
        if (!force && !shouldPlay(settings.level, call.priority)) return

        val token = ++shown
        _current.value = call
        minTimeUp = false
        speaking = settings.voice && _voiceAvailable.value && speak(call, "call-$token")
        if (settings.vibrate) vibrate(call.priority)

        scope.launch {
            delay(BANNER_MIN_MS)
            if (token == shown) {
                minTimeUp = true
                clearIfDone()
            }
        }
    }

    /** Queues [call] for speech; returns false if the engine refused it (no done callback will come). */
    private fun speak(call: RadioCall, utteranceId: String): Boolean {
        val interrupt = shouldInterrupt(call.priority, inFlight.values)
        if (interrupt) inFlight.clear()  // flushed: their callbacks may never arrive
        val mode = if (interrupt) TextToSpeech.QUEUE_FLUSH else TextToSpeech.QUEUE_ADD
        if (tts.speak(call.text, mode, null, utteranceId) != TextToSpeech.SUCCESS) return false
        inFlight[utteranceId] = call.priority
        return true
    }

    private fun spoken(utteranceId: String) {
        scope.launch {
            inFlight.remove(utteranceId)
            if (utteranceId == "call-$shown") {
                speaking = false
                clearIfDone()
            }
        }
    }

    private fun clearIfDone() {
        if (minTimeUp && !speaking) _current.value = null
    }

    private fun vibrate(priority: Int) {
        val pattern = when (priority) {
            1 -> longArrayOf(0, 40)
            2 -> longArrayOf(0, 40, 80, 40)
            3 -> longArrayOf(0, 300)
            else -> return  // chatter: no buzz
        }
        vibrator.vibrate(VibrationEffect.createWaveform(pattern, -1))
    }

    fun shutdown() {
        tts.stop()
        tts.shutdown()
    }

    companion object {
        private const val BANNER_MIN_MS = 4000L
        private const val SPEECH_RATE = 1.15f
    }
}
