package com.estudos.metadisplay

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import com.estudos.metadisplay.Bridge.emit
import com.estudos.metadisplay.Bridge.json
import com.meta.wearable.dat.camera.Camera
import com.meta.wearable.dat.camera.addCamera
import com.meta.wearable.dat.camera.photo.types.PhotoQuality
import com.meta.wearable.dat.camera.photo.types.PhotoResolution
import com.meta.wearable.dat.camera.photo.types.PhotoState
import com.meta.wearable.dat.camera.removeCamera
import com.meta.wearable.dat.camera.types.AudioCodec
import com.meta.wearable.dat.camera.types.AudioFrame
import com.meta.wearable.dat.camera.types.AudioSampleRate
import com.meta.wearable.dat.camera.types.PhotoData
import com.meta.wearable.dat.camera.types.StreamConfiguration
import com.meta.wearable.dat.camera.types.VideoFrame
import com.meta.wearable.dat.camera.types.VideoQuality
import com.meta.wearable.dat.camera.types.CameraState
import com.meta.wearable.dat.camera.types.StreamState
import com.meta.wearable.dat.core.session.DeviceSession
import com.meta.wearable.dat.core.types.DeviceSessionError
import com.meta.wearable.dat.core.types.Permission
import android.util.Log
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.abs
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.merge
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout

/**
 * Câmera dos óculos.
 * - Stream de vídeo: frames YUV 4:2:0 copiados para ByteBuffers diretos (o C# lê sem alocar).
 * - Foto durante o stream ([capturePhoto]) e foto standalone em alta qualidade ([captureHighQualityPhoto]).
 * - Áudio PCM 16 bits junto com o stream (experimental).
 */
object MDCamera : SessionCapability {
  /** Sem frames nesse tempo após iniciar = o stream travou; reinicia uma vez. */
  private const val STREAM_WATCHDOG_MS = 12_000L
  private const val HQ_PHOTO_TIMEOUT_MS = 180_000L

  private var wantedConfig: StreamConfiguration? = null
  private var camera: Camera? = null
  private val jobs = mutableListOf<Job>()
  private var watchdogJob: Job? = null
  @Volatile private var hqPhotoBusy = false

  /**
   * Toda operação que anexa/remove a câmera (start, stop, foto HQ, sessão iniciando) passa por aqui,
   * uma de cada vez. Sem isso, um "Iniciar" durante a foto HQ falhava com CAPABILITY_ALREADY_ADDED
   * e o pedido se perdia.
   */
  private val cameraMutex = Mutex()

  private fun log(message: String) = Log.i(Bridge.TAG, "[camera] $message")

  private fun info(message: String) {
    log(message)
    emit("camera.info", json("message" to message))
  }

  // ───────────── stream ─────────────

  /**
   * quality: LOW (360x640), MEDIUM (504x896), HIGH (720x1280). fps: 2, 7, 15, 24 ou 30.
   * audioSampleRate: 0 (sem áudio), 16000, 44100 ou 48000. Pede Permission.CAMERA (e MICROPHONE com áudio).
   */
  @JvmStatic
  fun startStream(quality: String, fps: Int, audioSampleRate: Int, audioChannels: Int) {
    Bridge.scope.launch {
      val audio =
          when (audioSampleRate) {
            16000 -> AudioCodec.PCM(AudioSampleRate.RATE_16000, audioChannels.coerceIn(1, 2))
            44100 -> AudioCodec.PCM(AudioSampleRate.RATE_44100, audioChannels.coerceIn(1, 2))
            48000 -> AudioCodec.PCM(AudioSampleRate.RATE_48000, audioChannels.coerceIn(1, 2))
            else -> null
          }
      val q = runCatching { VideoQuality.valueOf(quality.uppercase()) }.getOrDefault(VideoQuality.MEDIUM)
      val validFps = listOf(2, 7, 15, 24, 30).minByOrNull { abs(it - fps) } ?: 24
      val requested =
          StreamConfiguration(audioCodec = audio, videoQuality = q, frameRate = validFps, compressVideo = false)
      wantedConfig = requested
      audioChannelsConfigured = audioChannels.coerceIn(1, 2)
      audioRateConfigured = if (audio != null) audioSampleRate else 0
      log("pedido de stream: ${q.name} ${validFps}fps áudio=${audioRateConfigured}Hz")
      if (hqPhotoBusy) info("Aguardando a foto em alta qualidade terminar para iniciar o vídeo…")

      cameraMutex.withLock {
        if (wantedConfig !== requested) return@withLock // chegou um stop ou outro start depois deste
        if (!Permissions.ensure(Permission.CAMERA)) {
          emit("camera.error", json("error" to "PERMISSION_DENIED", "message" to "Permissão de câmera não concedida no Meta AI (status: ${Permissions.lastStatus(Permission.CAMERA)})"))
          if (wantedConfig === requested) wantedConfig = null
          return@withLock
        }
        if (audio != null && !Permissions.ensure(Permission.MICROPHONE)) {
          emit("camera.error", json("error" to "PERMISSION_DENIED", "message" to "Permissão de microfone não concedida; seguindo sem áudio"))
          wantedConfig = StreamConfiguration(audioCodec = null, videoQuality = q, frameRate = validFps, compressVideo = false)
          audioRateConfigured = 0
        }
        val cfg = wantedConfig ?: return@withLock
        val session = MDSession.startedSession()
        if (session == null) {
          info("Sessão ainda não está ativa; o vídeo começa quando ela conectar")
          return@withLock
        }
        detachCamera(session)
        attach(session, cfg, allowRetry = true)
      }
    }
  }

  @JvmStatic
  fun stopStream() {
    Bridge.scope.launch {
      log("pedido de parada")
      wantedConfig = null
      cameraMutex.withLock {
        val session = MDSession.session
        if (session != null) detachCamera(session) else clearLocalState()
      }
      emit("camera.state", json("state" to "STOPPED"))
    }
  }

  override fun onSessionStarted(session: DeviceSession) {
    if (wantedConfig == null || camera != null) return
    Bridge.scope.launch {
      cameraMutex.withLock {
        val cfg = wantedConfig ?: return@withLock
        if (camera == null && MDSession.startedSession() === session) attach(session, cfg, allowRetry = true)
      }
    }
  }

  override fun onSessionStopped() = clearLocalState()

  private fun clearLocalState() {
    watchdogJob?.cancel()
    watchdogJob = null
    jobs.forEach { it.cancel() }
    jobs.clear()
    camera = null
  }

  private suspend fun attach(session: DeviceSession, cfg: StreamConfiguration, allowRetry: Boolean) {
    if (camera != null) return
    var result = session.addCamera(cfg)
    if (result.errorOrNull() == DeviceSessionError.CAPABILITY_ALREADY_ADDED) {
      // Sobrou uma câmera anexada (ex.: parada que ainda não terminou): remove e tenta de novo.
      log("addCamera: CAPABILITY_ALREADY_ADDED; removendo a câmera anterior e tentando de novo")
      session.removeCamera()
      delay(600)
      result = session.addCamera(cfg)
    }
    val cam = result.getOrNull()
    if (cam == null) {
      val e = result.errorOrNull()
      log("addCamera falhou: ${Bridge.errorName(e)}")
      emit("camera.error", json("error" to Bridge.errorName(e), "message" to (e?.description ?: "addCamera falhou")))
      return
    }
    camera = cam
    val stream = cam.stream
    jobs +=
        Bridge.scope.launch {
          stream.state.collect {
            log("stream: ${it.name}")
            emit("camera.state", json("state" to it.name))
          }
        }
    jobs +=
        Bridge.scope.launch {
          stream.errorStream.collect { e ->
            log("erro do stream: ${e.name}")
            emit("camera.error", json("error" to e.name, "message" to e.description))
          }
        }
    jobs += Bridge.scope.launch(Dispatchers.Default) { stream.videoStream.collect { onVideoFrame(it) } }
    if (cfg.audioCodec != null) {
      jobs += Bridge.scope.launch(Dispatchers.IO) { stream.audioStream.collect { onAudioFrame(it) } }
    }
    stream.start().onFailure { e, _ ->
      log("stream.start falhou: ${e.name}")
      emit("camera.error", json("error" to e.name, "message" to e.description))
    }
    startWatchdog(session, cfg, cam, allowRetry)
  }

  /** Se nenhum frame chegar em [STREAM_WATCHDOG_MS], reinicia o stream (uma vez). */
  private fun startWatchdog(session: DeviceSession, cfg: StreamConfiguration, cam: Camera, allowRetry: Boolean) {
    watchdogJob?.cancel()
    val countAtStart = frameCounter
    watchdogJob =
        Bridge.scope.launch {
          delay(STREAM_WATCHDOG_MS)
          if (camera !== cam || frameCounter != countAtStart) return@launch
          val state = cam.stream.state.value
          if (state == StreamState.PAUSED) return@launch // pausado nos óculos (toque na haste): normal
          if (!allowRetry) {
            log("ainda sem frames após nova tentativa (estado $state)")
            emit("camera.error", json("error" to "NO_FRAMES", "message" to "A câmera não enviou vídeo (estado $state). Pare e inicie de novo, ou reconecte a sessão."))
            return@launch
          }
          info("Nenhum frame em ${STREAM_WATCHDOG_MS / 1000} s (estado $state); reiniciando o stream…")
          cameraMutex.withLock {
            if (wantedConfig !== cfg || camera !== cam || MDSession.startedSession() !== session) return@withLock
            detachCamera(session)
            attach(session, cfg, allowRetry = false)
          }
        }
  }

  /** Para a câmera, espera ela confirmar a parada e só então remove a capacidade da sessão. */
  private suspend fun detachCamera(session: DeviceSession) {
    val cam = camera
    clearLocalState()
    if (cam != null) stopAndRemove(session, cam)
  }

  private suspend fun stopAndRemove(session: DeviceSession, cam: Camera) {
    log("parando a câmera")
    cam.stop()
    val stopped = withTimeoutOrNull(3_000) { cam.state.first { it == CameraState.STOPPED } }
    if (stopped == null) log("a câmera não confirmou STOPPED em 3 s (estado ${cam.state.value.name}); removendo assim mesmo")
    session.removeCamera().onFailure { e, _ -> log("removeCamera: ${e.name} (ignorado)") }
  }

  // ───────────── frames (triple buffer em memória direta) ─────────────

  private val frameLock = Any()
  private val slots = arrayOfNulls<ByteBuffer>(3)
  private var readySlot = -1
  private var inUseSlot = -1
  @Volatile private var frameWidth = 0
  @Volatile private var frameHeight = 0
  @Volatile private var frameSize = 0
  @Volatile private var frameCounter = 0
  @Volatile private var frameTimestampUs = 0L
  /** 0 = ainda não detectado, 1 = I420 (planar), 2 = NV12/NV21 (semi-planar). */
  @Volatile private var frameLayout = 0
  private var layoutVotes = 0

  private fun onVideoFrame(frame: VideoFrame) {
    if (frame.isCompressed) return
    val w = frame.width
    val h = frame.height
    val tight = w * h * 3 / 2
    val src = frame.buffer.duplicate()
    val available = src.remaining()
    if (w <= 0 || h <= 0 || available < tight) return

    val slot =
        synchronized(frameLock) {
          (0 until 3).first { it != readySlot && it != inUseSlot }
        }
    var buf = slots[slot]
    if (buf == null || buf.capacity() < tight) {
      buf = ByteBuffer.allocateDirect(tight).order(ByteOrder.nativeOrder())
      slots[slot] = buf
    }
    buf!!.clear()
    if (available == tight) {
      buf.put(src)
    } else {
      // Linhas com padding: deduz o stride e copia só os pixels úteis
      copyWithStride(src, buf, w, h, available)
    }
    buf.flip()

    if (frameLayout == 0 || layoutVotes < 5) detectLayout(buf, w, h)

    synchronized(frameLock) {
      readySlot = slot
      frameWidth = w
      frameHeight = h
      frameSize = tight
      frameTimestampUs = frame.presentationTimeUs
      frameCounter++
    }
  }

  private fun copyWithStride(src: ByteBuffer, dst: ByteBuffer, w: Int, h: Int, available: Int) {
    val stride = (available * 2 / (h * 3)).coerceAtLeast(w)
    val base = src.position()
    val row = ByteArray(w)
    for (y in 0 until h) {
      src.position(base + y * stride)
      src.get(row, 0, w)
      dst.put(row)
    }
    // Plano(s) de crominância: h/2 linhas com o mesmo stride (semi-planar) ou stride/2 (planar)
    val chromaBase = base + stride * h
    val chromaRows = h / 2
    val chromaBytesPerRow = w
    val chromaRow = ByteArray(chromaBytesPerRow)
    for (y in 0 until chromaRows) {
      val pos = chromaBase + y * stride
      if (pos + chromaBytesPerRow > base + available) break
      src.position(pos)
      src.get(chromaRow, 0, chromaBytesPerRow)
      dst.put(chromaRow)
    }
    while (dst.position() < w * h * 3 / 2) dst.put(128.toByte())
  }

  /**
   * Heurística: no semi-planar (UVUV...) bytes vizinhos alternam U e V, então a diferença com o
   * vizinho imediato é bem maior que a diferença com o byte 2 posições à frente.
   */
  private fun detectLayout(buf: ByteBuffer, w: Int, h: Int) {
    val start = w * h
    val len = w * h / 2
    var d1 = 0L
    var d2 = 0L
    var i = start
    val end = start + len - 2
    val step = maxOf(2, len / 4000) or 1 // amostragem
    while (i < end) {
      val a = buf.get(i).toInt() and 0xFF
      val b = buf.get(i + 1).toInt() and 0xFF
      val c = buf.get(i + 2).toInt() and 0xFF
      d1 += abs(a - b)
      d2 += abs(a - c)
      i += step
    }
    if (d1 + d2 < 200) return // imagem sem cor (cinza): inconclusivo
    val guess = if (d1 > d2 * 3 / 2) 2 else 1
    if (guess == frameLayout) layoutVotes++ else {
      frameLayout = guess
      layoutVotes = 1
    }
  }

  /** [largura, altura, tamanho, contador, layout] do frame mais recente. */
  @JvmStatic
  fun frameInfo(): IntArray =
      synchronized(frameLock) { intArrayOf(frameWidth, frameHeight, frameSize, frameCounter, frameLayout) }

  @JvmStatic fun frameCounter(): Int = frameCounter

  @JvmStatic fun frameTimestampUs(): Long = frameTimestampUs

  /** Reserva o frame mais recente (ByteBuffer direto). Chame [releaseFrame] depois de ler. */
  @JvmStatic
  fun acquireFrame(): ByteBuffer? =
      synchronized(frameLock) {
        if (readySlot < 0) return null
        inUseSlot = readySlot
        slots[inUseSlot]?.duplicate()?.apply {
          position(0)
          limit(frameSize)
        }
      }

  @JvmStatic
  fun releaseFrame() {
    synchronized(frameLock) { inUseSlot = -1 }
  }

  /** Cópia do frame atual como JPEG (útil para salvar ou enviar para uma API). */
  @JvmStatic
  fun frameAsJpeg(quality: Int): ByteArray? {
    val buf = acquireFrame() ?: return null
    return try {
      val w = frameWidth
      val h = frameHeight
      val nv21 = ByteArray(w * h * 3 / 2)
      buf.get(nv21, 0, w * h)
      val uvSize = w * h / 2
      val chroma = ByteArray(uvSize)
      buf.get(chroma, 0, uvSize)
      if (frameLayout == 1) {
        // I420 -> NV21 (VU intercalado)
        val q = uvSize / 2
        for (i in 0 until q) {
          nv21[w * h + i * 2] = chroma[q + i]
          nv21[w * h + i * 2 + 1] = chroma[i]
        }
      } else {
        // NV12 -> NV21 (troca U e V)
        for (i in 0 until uvSize step 2) {
          nv21[w * h + i] = chroma[i + 1]
          nv21[w * h + i + 1] = chroma[i]
        }
      }
      val out = ByteArrayOutputStream()
      android.graphics.YuvImage(nv21, android.graphics.ImageFormat.NV21, w, h, null)
          .compressToJpeg(android.graphics.Rect(0, 0, w, h), quality.coerceIn(10, 100), out)
      out.toByteArray()
    } finally {
      releaseFrame()
    }
  }

  // ───────────── áudio (PCM 16 bits, little endian) ─────────────

  private val audioLock = Any()
  private var audioBuffer = ByteArray(0)
  private var audioLength = 0
  @Volatile private var audioRateConfigured = 0
  @Volatile private var audioChannelsConfigured = 1
  private const val MAX_AUDIO_BYTES = 48000 * 2 * 2 * 2 // ~2 s em 48 kHz estéreo

  private fun onAudioFrame(frame: AudioFrame) {
    val src = frame.buffer.duplicate()
    val n = src.remaining()
    synchronized(audioLock) {
      if (audioLength + n > MAX_AUDIO_BYTES) audioLength = 0 // o Unity não está consumindo: descarta
      if (audioBuffer.size < audioLength + n) audioBuffer = audioBuffer.copyOf(maxOf(audioLength + n, 8192))
      src.get(audioBuffer, audioLength, n)
      audioLength += n
    }
  }

  /** Bytes PCM acumulados desde a última chamada. */
  @JvmStatic
  fun drainAudio(): ByteArray =
      synchronized(audioLock) {
        val out = audioBuffer.copyOf(audioLength)
        audioLength = 0
        out
      }

  /** [sampleRate, canais] configurados (0 = sem áudio). */
  @JvmStatic fun audioFormat(): IntArray = intArrayOf(audioRateConfigured, audioChannelsConfigured)

  // ───────────── fotos ─────────────

  private val photos = ConcurrentHashMap<Int, ByteArray>()

  /** Foto durante o stream ativo. Resultado: "camera.photo" (use [takePhoto]) ou "camera.photoFailed". */
  @JvmStatic
  fun capturePhoto(requestId: Int) {
    Bridge.scope.launch {
      val cam = camera
      if (cam == null) {
        emit("camera.photoFailed", json("requestId" to requestId, "error" to "NOT_STREAMING", "message" to "Inicie o stream antes"))
        return@launch
      }
      val result = cam.stream.capturePhoto()
      val data = result.getOrNull()
      if (data == null) {
        val e = result.errorOrNull()
        emit("camera.photoFailed", json("requestId" to requestId, "error" to Bridge.errorName(e), "message" to (e?.description ?: "Falha na captura")))
        return@launch
      }
      // fold() não é inline, então a conversão (suspensa) fica fora dele
      val jpeg = withContext(Dispatchers.Default) { photoToJpeg(data) }
      if (jpeg == null) {
        emit("camera.photoFailed", json("requestId" to requestId, "error" to "DECODE_FAILED", "message" to "Formato não suportado"))
      } else {
        photos[requestId] = jpeg
        emit("camera.photo", json("requestId" to requestId, "bytes" to jpeg.size))
      }
    }
  }

  /**
   * Foto standalone em alta qualidade (experimental). Para o stream (se houver), tira a foto
   * e depois religa o stream. resolution: SMALL/MEDIUM/LARGE/FULL, quality: LOW/MEDIUM/HIGH.
   */
  @JvmStatic
  fun captureHighQualityPhoto(resolution: String, quality: String, requestId: Int) {
    Bridge.scope.launch {
      if (hqPhotoBusy) {
        emit("camera.photoFailed", json("requestId" to requestId, "error" to "BUSY", "message" to "Já existe uma foto em alta qualidade em andamento"))
        return@launch
      }
      hqPhotoBusy = true
      var encoded: ByteArray? = null
      try {
        cameraMutex.withLock {
          val session = MDSession.startedSession()
          if (session == null) {
            emit("camera.photoFailed", json("requestId" to requestId, "error" to "NO_SESSION", "message" to "Conecte aos óculos antes"))
            return@withLock
          }
          if (!Permissions.ensure(Permission.CAMERA)) {
            emit("camera.photoFailed", json("requestId" to requestId, "error" to "PERMISSION_DENIED", "message" to "Permissão de câmera não concedida (status: ${Permissions.lastStatus(Permission.CAMERA)})"))
            return@withLock
          }
          log("foto HQ: liberando a câmera do stream (se houver)")
          detachCamera(session)
          var added = session.addCamera(StreamConfiguration())
          if (added.errorOrNull() == DeviceSessionError.CAPABILITY_ALREADY_ADDED) {
            session.removeCamera()
            delay(600)
            added = session.addCamera(StreamConfiguration())
          }
          val cam = added.getOrNull()
          if (cam == null) {
            val e = added.errorOrNull()
            emit("camera.photoFailed", json("requestId" to requestId, "error" to Bridge.errorName(e), "message" to (e?.description ?: "Não foi possível anexar a câmera")))
            return@withLock
          }
          val photo = cam.photo
          var lastReported = -1.0
          val progressJob =
              Bridge.scope.launch {
                photo.transferProgressStream.collect {
                  // A transferência pelo Bluetooth é lenta (FULL ≈ 1,6 MB ≈ 1 min): avisa a cada 5%
                  if (it.fraction >= 1.0 || it.fraction - lastReported >= 0.05) {
                    lastReported = it.fraction
                    emit("camera.photoProgress", json("requestId" to requestId, "fraction" to it.fraction))
                  }
                }
              }
          try {
            encoded =
                withTimeout(HQ_PHOTO_TIMEOUT_MS) {
                  photo.state.first { it == PhotoState.STARTED }
                  val result =
                      Bridge.scope.async(start = CoroutineStart.UNDISPATCHED) {
                        merge(
                                photo.photoStream.map { Result.success(it.imageData) },
                                photo.errors.filterNotNull().map { Result.failure<ByteArray>(IllegalStateException(it.name + ": " + it.description)) },
                            )
                            .first()
                      }
                  log("foto HQ: capturando ($resolution, $quality)")
                  photo.capturePhoto(
                      runCatching { PhotoResolution.valueOf(resolution.uppercase()) }.getOrDefault(PhotoResolution.FULL),
                      runCatching { PhotoQuality.valueOf(quality.uppercase()) }.getOrDefault(PhotoQuality.HIGH),
                  )
                  result.await().getOrThrow()
                }
            log("foto HQ recebida: ${encoded?.size} bytes")
          } catch (t: TimeoutCancellationException) {
            emit("camera.photoFailed", json("requestId" to requestId, "error" to "TIMEOUT", "message" to "A foto não chegou em ${HQ_PHOTO_TIMEOUT_MS / 1000} s (a transferência é pelo Bluetooth; tente uma resolução menor)"))
          } catch (t: Throwable) {
            emit("camera.photoFailed", json("requestId" to requestId, "error" to "CAPTURE_FAILED", "message" to (t.message ?: t.toString())))
          } finally {
            progressJob.cancel()
            photo.stop()
            stopAndRemove(session, cam)
          }
          // Religa o vídeo se ele foi pedido antes ou durante a foto.
          val cfg = wantedConfig
          if (cfg != null && MDSession.startedSession() === session) {
            info("Foto concluída; retomando o vídeo")
            attach(session, cfg, allowRetry = true)
          }
        }
      } finally {
        hqPhotoBusy = false
      }
      // A conversão HEIC → JPEG é lenta para fotos grandes: fica fora da fila da câmera.
      val bytes = encoded ?: return@launch
      val jpeg = withContext(Dispatchers.Default) { encodedToJpeg(bytes) } ?: bytes
      photos[requestId] = jpeg
      emit("camera.photo", json("requestId" to requestId, "bytes" to jpeg.size, "highQuality" to true))
    }
  }

  /** Retira (e apaga) a foto pronta de um pedido. JPEG. */
  @JvmStatic fun takePhoto(requestId: Int): ByteArray? = photos.remove(requestId)

  private fun photoToJpeg(data: PhotoData): ByteArray? =
      when (data) {
        is PhotoData.Bitmap -> bitmapToJpeg(data.bitmap)
        is PhotoData.HEIC -> {
          val b = data.data.duplicate()
          val bytes = ByteArray(b.remaining()).also { b.get(it) }
          encodedToJpeg(bytes)
        }
        else -> null
      }

  private fun encodedToJpeg(bytes: ByteArray): ByteArray? =
      BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.let { bitmapToJpeg(it) }

  private fun bitmapToJpeg(bmp: Bitmap): ByteArray =
      ByteArrayOutputStream().also { bmp.compress(Bitmap.CompressFormat.JPEG, 92, it) }.toByteArray()
}
