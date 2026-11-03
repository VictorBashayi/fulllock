package com.f1controller.engineer

import android.content.Context
import android.os.Build
import android.os.SystemClock
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import android.util.Log
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
 *
 * Phone battery managers (RedMagic's NeoSafe) freeze or kill the speech engine
 * mid-race. A refused speak() or an utterance that never starts within
 * [START_TIMEOUT_MS] means the engine is gone: we bind a fresh one (at most once
 * per [RECONNECT_INTERVAL_MS]) and re-speak the call if it's still on screen.
 */
class EngineerRadio(
    context: Context,
    private val settings: EngineerSettings,
    private val scope: CoroutineScope,
) {
    private val appContext = context.applicationContext

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
    // Utterances the engine has started saying, and whether one is being said right now
    private val started = HashSet<String>()
    private var engineTalking = false

    private val reconnectGate = ReconnectGate(RECONNECT_INTERVAL_MS)
    private var initializing = false
    // Call to say again once a reconnected engine is ready
    private var retryToken = 0

    private var tts: TextToSpeech = createEngine()

    private fun createEngine(): TextToSpeech {
        initializing = true
        lateinit var engine: TextToSpeech
        engine = TextToSpeech(appContext) { status ->
            scope.launch { onTtsReady(engine, status == TextToSpeech.SUCCESS) }
        }
        // A ready callback that never comes (engine killed while connecting) leaves initializing
        // set and the next play() can't retry, so give up on it after a while
        scope.launch {
            delay(INIT_TIMEOUT_MS)
            if (engine === tts && initializing) initializing = false
        }
        return engine
    }

    private fun onTtsReady(engine: TextToSpeech, ok: Boolean) {
        if (engine !== tts) return  // an engine we've already replaced
        initializing = false
        val usable = ok && engine.setLanguage(Locale.UK) >= TextToSpeech.LANG_AVAILABLE
        if (!usable) return
        engine.setSpeechRate(SPEECH_RATE)
        engine.setOnUtteranceProgressListener(object : UtteranceProgressListener() {
            override fun onStart(utteranceId: String) = onEngineEvent(engine) {
                started.add(utteranceId)
                engineTalking = true
            }
            override fun onDone(utteranceId: String) = spoken(engine, utteranceId)
            override fun onStop(utteranceId: String, interrupted: Boolean) = spoken(engine, utteranceId)
            @Deprecated("Deprecated in Java")
            override fun onError(utteranceId: String) = spoken(engine, utteranceId)
        })
        _voiceAvailable.value = true

        val token = retryToken
        retryToken = 0
        val call = _current.value
        if (token != 0 && token == shown && call != null && settings.voice) {
            speaking = speakOrRecover(call, token)
        }
    }

    /** Shows/speaks/vibrates [call] if the chattiness allows it; [force] ignores chattiness (test call). */
    fun play(call: RadioCall, force: Boolean = false) {
        if (!force && !shouldPlay(settings.level, call.priority)) return

        val token = ++shown
        _current.value = call
        minTimeUp = false
        speaking = when {
            !settings.voice -> false
            _voiceAvailable.value -> speakOrRecover(call, token)
            // Engine never came up (or a reconnect failed): try binding again
            !initializing -> { reconnect(token); false }
            else -> false
        }
        if (settings.vibrate) vibrate(call.priority)

        scope.launch {
            delay(BANNER_MIN_MS)
            if (token == shown) {
                minTimeUp = true
                clearIfDone()
            }
        }
    }

    /** Speaks [call]; if the engine refuses it, reconnects. Returns whether a done callback will come. */
    private fun speakOrRecover(call: RadioCall, token: Int): Boolean {
        val utteranceId = "call-$token"
        if (!speak(call, utteranceId)) {
            reconnect(token)
            return false
        }
        watchStart(utteranceId, token)
        return true
    }

    /** Queues [call] for speech; returns false if the engine refused it (no done callback will come). */
    private fun speak(call: RadioCall, utteranceId: String): Boolean {
        val interrupt = shouldInterrupt(call.priority, inFlight.values)
        if (interrupt) inFlight.clear()  // flushed: their callbacks may never arrive
        val mode = if (interrupt) TextToSpeech.QUEUE_FLUSH else TextToSpeech.QUEUE_ADD
        val result = try {
            tts.speak(call.text, mode, null, utteranceId)
        } catch (_: Exception) {
            TextToSpeech.ERROR  // dead binder
        }
        if (result != TextToSpeech.SUCCESS) return false
        inFlight[utteranceId] = call.priority
        return true
    }

    /**
     * A frozen engine accepts speak() but never starts. Waits while something
     * else is being said (calls queue behind each other), not otherwise.
     */
    private fun watchStart(utteranceId: String, token: Int) {
        val engine = tts
        scope.launch {
            while (true) {
                delay(START_TIMEOUT_MS)
                if (engine !== tts || utteranceId !in inFlight || utteranceId in started) return@launch
                if (!engineTalking) break
            }
            reconnect(if (token == shown) token else 0)
        }
    }

    /** Replaces the speech engine, then re-speaks call [token] if it's still shown (0 = none). */
    private fun reconnect(token: Int) {
        if (!reconnectGate.tryAcquire(SystemClock.elapsedRealtime())) {
            // Give up on voice for this call; the banner times out as usual
            if (token == shown) {
                speaking = false
                clearIfDone()
            }
            return
        }
        Log.i(TAG, "Speech engine not responding; reconnecting")
        val old = tts
        try { old.shutdown() } catch (_: Exception) {}
        inFlight.clear()
        started.clear()
        engineTalking = false
        _voiceAvailable.value = false
        retryToken = token
        speaking = false
        tts = createEngine()
    }

    private fun spoken(engine: TextToSpeech, utteranceId: String) = onEngineEvent(engine) {
        inFlight.remove(utteranceId)
        started.remove(utteranceId)
        engineTalking = false
        if (utteranceId == "call-$shown") {
            speaking = false
            clearIfDone()
        }
    }

    /** Runs [block] on the main thread, unless [engine] has since been replaced. */
    private fun onEngineEvent(engine: TextToSpeech, block: () -> Unit) {
        scope.launch { if (engine === tts) block() }
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
        private const val TAG = "EngineerRadio"
        private const val BANNER_MIN_MS = 4000L
        private const val SPEECH_RATE = 1.15f
        private const val START_TIMEOUT_MS = 3000L
        private const val RECONNECT_INTERVAL_MS = 10_000L
        private const val INIT_TIMEOUT_MS = 10_000L
    }
}
