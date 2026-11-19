package com.estudos.metadisplay

import com.estudos.metadisplay.Bridge.emit
import com.estudos.metadisplay.Bridge.json
import com.meta.wearable.dat.core.session.DeviceSession
import com.meta.wearable.dat.core.types.Permission
import com.meta.wearable.dat.inputs.Inputs
import com.meta.wearable.dat.inputs.addInputs
import com.meta.wearable.dat.inputs.removeInputs
import com.meta.wearable.dat.inputs.types.InputEvent
import com.meta.wearable.dat.inputs.types.InputSource
import com.meta.wearable.dat.inputs.types.InputsConfiguration
import com.meta.wearable.dat.motion.Motion
import com.meta.wearable.dat.motion.addMotion
import com.meta.wearable.dat.motion.removeMotion
import com.meta.wearable.dat.motion.types.MotionConfiguration
import com.meta.wearable.dat.motion.types.MotionSample
import com.meta.wearable.dat.motion.types.MotionSamplingRate
import com.meta.wearable.dat.motion.types.MotionSource
import com.meta.wearable.dat.speech.Speech
import com.meta.wearable.dat.speech.SpeechConfiguration
import com.meta.wearable.dat.speech.addSpeech
import com.meta.wearable.dat.speech.removeSpeech
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

// ═══════════════════════════════ MOTION (IMU) ═══════════════════════════════

/** Acelerômetro, giroscópio, magnetômetro e orientação dos óculos ou da Neural Band (experimental). */
object MDMotion : SessionCapability {
  /** Doubles por amostra em [drainSamples]. */
  const val STRIDE = 17

  private var wantedRate: MotionSamplingRate? = null
  private var motion: Motion? = null
  private val jobs = mutableListOf<Job>()

  private val lock = Any()
  private var buffer = DoubleArray(STRIDE * 256)
  private var count = 0
  private const val MAX_SAMPLES = 2048

  /** hz: 5, 10, 15, 24, 30 ou 60. */
  @JvmStatic
  fun start(hz: Int) {
    Bridge.scope.launch {
      wantedRate =
          when {
            hz >= 60 -> MotionSamplingRate.HZ_60
            hz >= 30 -> MotionSamplingRate.HZ_30
            hz >= 24 -> MotionSamplingRate.HZ_24
            hz >= 15 -> MotionSamplingRate.HZ_15
            hz >= 10 -> MotionSamplingRate.HZ_10
            else -> MotionSamplingRate.HZ_5
          }
      MDSession.startedSession()?.let {
        detach(it)
        attach(it)
      }
    }
  }

  @JvmStatic
  fun stop() {
    Bridge.scope.launch {
      wantedRate = null
      MDSession.session?.let { detach(it) }
      emit("motion.state", json("state" to "STOPPED"))
    }
  }

  override fun onSessionStarted(session: DeviceSession) {
    if (wantedRate != null && motion == null) attach(session)
  }

  override fun onSessionStopped() {
    jobs.forEach { it.cancel() }
    jobs.clear()
    motion = null
  }

  private fun attach(session: DeviceSession) {
    val rate = wantedRate ?: return
    session
        .addMotion(MotionConfiguration(samplingRate = rate))
        .fold(
            onSuccess = { m ->
              motion = m
              jobs += Bridge.scope.launch(Dispatchers.Default) { m.samples.collect { push(it) } }
              jobs += Bridge.scope.launch { m.state.collect { emit("motion.state", json("state" to it.name)) } }
              jobs +=
                  Bridge.scope.launch {
                    m.errors.collect { e -> if (e != null) emit("motion.error", json("error" to e.name, "message" to e.description)) }
                  }
              m.start()
            },
            onFailure = { e, _ -> emit("motion.error", json("error" to e.name, "message" to e.description)) },
        )
  }

  private fun detach(session: DeviceSession) {
    if (motion == null) return
    jobs.forEach { it.cancel() }
    jobs.clear()
    motion?.stop()
    motion = null
    session.removeMotion()
  }

  private fun push(s: MotionSample) {
    synchronized(lock) {
      if (count >= MAX_SAMPLES) count = 0 // o Unity parou de consumir
      if (buffer.size < (count + 1) * STRIDE) buffer = buffer.copyOf(buffer.size * 2)
      val o = count * STRIDE
      var flags = 0
      buffer[o] = s.timestampNs.toDouble()
      buffer[o + 1] =
          when (s.source) {
            MotionSource.GLASSES -> 0.0
            MotionSource.NEURAL_BAND -> 1.0
            else -> 2.0
          }
      s.accelerometer?.let {
        flags = flags or 1
        buffer[o + 3] = it.x.toDouble(); buffer[o + 4] = it.y.toDouble(); buffer[o + 5] = it.z.toDouble()
      }
      s.gyroscope?.let {
        flags = flags or 2
        buffer[o + 6] = it.x.toDouble(); buffer[o + 7] = it.y.toDouble(); buffer[o + 8] = it.z.toDouble()
      }
      s.magnetometer?.let {
        flags = flags or 4
        buffer[o + 9] = it.x.toDouble(); buffer[o + 10] = it.y.toDouble(); buffer[o + 11] = it.z.toDouble()
      }
      s.orientation?.let {
        flags = flags or 8
        buffer[o + 12] = it.x.toDouble(); buffer[o + 13] = it.y.toDouble(); buffer[o + 14] = it.z.toDouble(); buffer[o + 15] = it.w.toDouble()
      }
      buffer[o + 2] = flags.toDouble()
      buffer[o + 16] = 0.0
      count++
    }
  }

  /**
   * Amostras acumuladas desde a última chamada, 17 doubles cada:
   * [ts_ns, fonte(0 óculos,1 band,2 ?), flags(1 acc,2 gyro,4 mag,8 orient), ax,ay,az, gx,gy,gz, mx,my,mz, qx,qy,qz,qw, 0]
   */
  @JvmStatic
  fun drainSamples(): DoubleArray =
      synchronized(lock) {
        val out = buffer.copyOf(count * STRIDE)
        count = 0
        out
      }
}

// ═══════════════════════════════ INPUTS ═══════════════════════════════

/** Navegação, seleção, voltar, botões, captura e arraste vindos dos óculos/Neural Band (experimental). */
object MDInputs : SessionCapability {
  private var wanted: InputsConfiguration? = null
  private var inputs: Inputs? = null
  private val jobs = mutableListOf<Job>()

  /** sourcesCsv: vazio = todas (padrão do SDK) ou ex. "CAPTOUCH,NEURAL_BAND". */
  @JvmStatic
  fun start(consumeBack: Boolean, sourcesCsv: String) {
    Bridge.scope.launch {
      val sources =
          sourcesCsv.split(',').map { it.trim().uppercase() }.filter { it.isNotEmpty() }
              .mapNotNull { runCatching { InputSource.valueOf(it) }.getOrNull() }.toSet()
      wanted =
          if (sources.isEmpty()) InputsConfiguration(consumeBack = consumeBack)
          else InputsConfiguration(sources = sources, consumeBack = consumeBack)
      MDSession.startedSession()?.let {
        detach(it)
        attach(it)
      }
    }
  }

  @JvmStatic
  fun stop() {
    Bridge.scope.launch {
      wanted = null
      MDSession.session?.let { detach(it) }
      emit("inputs.state", json("state" to "INACTIVE"))
    }
  }

  override fun onSessionStarted(session: DeviceSession) {
    if (wanted != null && inputs == null) attach(session)
  }

  override fun onSessionStopped() {
    jobs.forEach { it.cancel() }
    jobs.clear()
    inputs = null
  }

  private fun attach(session: DeviceSession) {
    val cfg = wanted ?: return
    session
        .addInputs(cfg)
        .fold(
            onSuccess = { i ->
              inputs = i
              jobs += Bridge.scope.launch { i.events.collect { emit("input", eventJson(it)) } }
              jobs += Bridge.scope.launch { i.state.collect { emit("inputs.state", json("state" to it.name)) } }
              jobs +=
                  Bridge.scope.launch {
                    i.errors.collect { e -> if (e != null) emit("inputs.error", json("error" to e.name, "message" to e.description)) }
                  }
            },
            onFailure = { e, _ -> emit("inputs.error", json("error" to e.name, "message" to e.description)) },
        )
  }

  private fun detach(session: DeviceSession) {
    if (inputs == null) return
    jobs.forEach { it.cancel() }
    jobs.clear()
    inputs = null
    session.removeInputs()
  }

  private fun eventJson(e: InputEvent) =
      when (e) {
        is InputEvent.Nav -> json("kind" to "NAV", "direction" to e.direction.name)
        is InputEvent.Select -> json("kind" to "SELECT")
        is InputEvent.Back -> json("kind" to "BACK")
        is InputEvent.Button -> json("kind" to "BUTTON", "button" to e.button.name)
        is InputEvent.Capture -> json("kind" to "CAPTURE", "pressType" to e.pressType.name)
        is InputEvent.Drag ->
            json("kind" to "DRAG", "action" to e.action.name, "x" to e.x.toDouble(), "y" to e.y.toDouble(), "dx" to e.dx.toDouble(), "dy" to e.dy.toDouble())
        else -> json("kind" to "UNKNOWN")
      }.apply {
        put("source", e.source.name)
        put("timestampMs", e.timestampMs)
      }
}

// ═══════════════════════════════ SPEECH ═══════════════════════════════

/** Reconhecimento de fala nos óculos com transcrições parciais e finais (experimental). */
object MDSpeech : SessionCapability {
  private var wanted = false
  private var speech: Speech? = null
  private val jobs = mutableListOf<Job>()

  /** Pede Permission.MICROPHONE no Meta AI se necessário. */
  @JvmStatic
  fun start() {
    Bridge.scope.launch {
      wanted = true
      if (!Permissions.ensure(Permission.MICROPHONE)) {
        emit("speech.error", json("error" to "PERMISSION_DENIED", "message" to "Permissão de microfone negada no Meta AI"))
        wanted = false
        return@launch
      }
      MDSession.startedSession()?.let { attach(it) }
    }
  }

  @JvmStatic
  fun stop() {
    Bridge.scope.launch {
      wanted = false
      MDSession.session?.let { detach(it) }
      emit("speech.state", json("state" to "STOPPED"))
    }
  }

  override fun onSessionStarted(session: DeviceSession) {
    if (wanted && speech == null) attach(session)
  }

  override fun onSessionStopped() {
    jobs.forEach { it.cancel() }
    jobs.clear()
    speech = null
  }

  private fun attach(session: DeviceSession) {
    if (speech != null) return
    session
        .addSpeech(SpeechConfiguration())
        .fold(
            onSuccess = { sp ->
              speech = sp
              jobs +=
                  Bridge.scope.launch {
                    sp.transcriptions.collect { r ->
                      if (r != null) {
                        emit("speech.transcription", json("text" to r.text, "isFinal" to r.isFinal, "confidence" to r.confidence.toDouble()))
                      }
                    }
                  }
              jobs += Bridge.scope.launch { sp.state.collect { emit("speech.state", json("state" to it.name)) } }
              jobs += Bridge.scope.launch { sp.locale.collect { l -> if (l != null) emit("speech.locale", json("locale" to l)) } }
              jobs +=
                  Bridge.scope.launch {
                    sp.errors.collect { e ->
                      if (e != null) emit("speech.error", json("error" to e.name, "message" to e.description, "details" to sp.errorDetails.value))
                    }
                  }
              sp.start().onFailure { e, _ -> emit("speech.error", json("error" to e.name, "message" to e.description)) }
            },
            onFailure = { e, _ -> emit("speech.error", json("error" to e.name, "message" to e.description)) },
        )
  }

  private fun detach(session: DeviceSession) {
    if (speech == null) return
    jobs.forEach { it.cancel() }
    jobs.clear()
    speech?.stop()
    speech = null
    session.removeSpeech()
  }
}
