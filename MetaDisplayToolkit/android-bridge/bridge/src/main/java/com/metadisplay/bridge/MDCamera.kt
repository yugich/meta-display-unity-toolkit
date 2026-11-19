package com.metadisplay.bridge

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Log
import com.metadisplay.bridge.Bridge.emit
import com.metadisplay.bridge.Bridge.json
import com.meta.wearable.dat.camera.Camera
import com.meta.wearable.dat.camera.addCamera
import com.meta.wearable.dat.camera.photo.types.PhotoQuality
import com.meta.wearable.dat.camera.photo.types.PhotoResolution
import com.meta.wearable.dat.camera.photo.types.PhotoState
import com.meta.wearable.dat.camera.removeCamera
import com.meta.wearable.dat.camera.types.AudioCodec
import com.meta.wearable.dat.camera.types.AudioFrame
import com.meta.wearable.dat.camera.types.AudioSampleRate
import com.meta.wearable.dat.camera.types.CameraState
import com.meta.wearable.dat.camera.types.PhotoData
import com.meta.wearable.dat.camera.types.StreamConfiguration
import com.meta.wearable.dat.camera.types.StreamState
import com.meta.wearable.dat.camera.types.VideoFrame
import com.meta.wearable.dat.camera.types.VideoQuality
import com.meta.wearable.dat.core.session.DeviceSession
import com.meta.wearable.dat.core.types.DeviceSessionError
import com.meta.wearable.dat.core.types.Permission
import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.concurrent.ConcurrentHashMap
import kotlin.math.abs
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.merge
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Glasses camera.
 * - Video stream: YUV 4:2:0 frames copied into direct ByteBuffers (C# reads them without allocating).
 * - Photo during the stream ([capturePhoto]) and standalone high-quality photo ([captureHighQualityPhoto]).
 * - 16-bit PCM audio alongside the stream (experimental).
 */
object MDCamera : SessionCapability {
  /** No frames within this time after starting = the stream is stuck; restart once. */
  private const val STREAM_WATCHDOG_MS = 12_000L
  private const val HQ_PHOTO_TIMEOUT_MS = 180_000L

  private var wantedConfig: StreamConfiguration? = null
  private var camera: Camera? = null
  private val jobs = mutableListOf<Job>()
  private var watchdogJob: Job? = null
  @Volatile private var hqPhotoBusy = false

  /**
   * Every operation that attaches/removes the camera (start, stop, HQ photo, session starting) goes
   * through here, one at a time. Without it, a "Start" during an HQ photo failed with
   * CAPABILITY_ALREADY_ADDED and the request was lost.
   */
  private val cameraMutex = Mutex()

  private fun log(message: String) = Log.i(Bridge.TAG, "[camera] $message")

  private fun info(message: String) {
    log(message)
    emit("camera.info", json("message" to message))
  }

  // ───────────── stream ─────────────

  /**
   * quality: LOW (360x640), MEDIUM (504x896), HIGH (720x1280). fps: 2, 7, 15, 24 or 30.
   * audioSampleRate: 0 (no audio), 16000, 44100 or 48000. Requests Permission.CAMERA (and MICROPHONE with audio).
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
      log("stream requested: ${q.name} ${validFps}fps audio=${audioRateConfigured}Hz")
      if (hqPhotoBusy) info("Waiting for the high-quality photo to finish before starting the video…")

      cameraMutex.withLock {
        if (wantedConfig !== requested) return@withLock // a stop or another start arrived after this one
        if (!Permissions.ensure(Permission.CAMERA)) {
          emit("camera.error", json("error" to "PERMISSION_DENIED", "message" to "Camera permission not granted in Meta AI (status: ${Permissions.lastStatus(Permission.CAMERA)})"))
          if (wantedConfig === requested) wantedConfig = null
          return@withLock
        }
        if (audio != null && !Permissions.ensure(Permission.MICROPHONE)) {
          emit("camera.error", json("error" to "PERMISSION_DENIED", "message" to "Microphone permission not granted; continuing without audio"))
          wantedConfig = StreamConfiguration(audioCodec = null, videoQuality = q, frameRate = validFps, compressVideo = false)
          audioRateConfigured = 0
        }
        val cfg = wantedConfig ?: return@withLock
        val session = MDSession.startedSession()
        if (session == null) {
          info("Session is not active yet; the video starts when it connects")
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
      log("stop requested")
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
      // A camera is still attached (e.g. a stop that has not finished): remove it and try again.
      log("addCamera: CAPABILITY_ALREADY_ADDED; removing the previous camera and retrying")
      session.removeCamera()
      delay(600)
      result = session.addCamera(cfg)
    }
    val cam = result.getOrNull()
    if (cam == null) {
      val e = result.errorOrNull()
      log("addCamera failed: ${Bridge.errorName(e)}")
      emit("camera.error", json("error" to Bridge.errorName(e), "message" to (e?.description ?: "addCamera failed")))
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
            log("stream error: ${e.name}")
            emit("camera.error", json("error" to e.name, "message" to e.description))
          }
        }
    jobs += Bridge.scope.launch(Dispatchers.Default) { stream.videoStream.collect { onVideoFrame(it) } }
    if (cfg.audioCodec != null) {
      jobs += Bridge.scope.launch(Dispatchers.IO) { stream.audioStream.collect { onAudioFrame(it) } }
    }
    stream.start().onFailure { e, _ ->
      log("stream.start failed: ${e.name}")
      emit("camera.error", json("error" to e.name, "message" to e.description))
    }
    startWatchdog(session, cfg, cam, allowRetry)
  }

  /** If no frame arrives within [STREAM_WATCHDOG_MS], restart the stream (once). */
  private fun startWatchdog(session: DeviceSession, cfg: StreamConfiguration, cam: Camera, allowRetry: Boolean) {
    watchdogJob?.cancel()
    val countAtStart = frameCounter
    watchdogJob =
        Bridge.scope.launch {
          delay(STREAM_WATCHDOG_MS)
          if (camera !== cam || frameCounter != countAtStart) return@launch
          val state = cam.stream.state.value
          if (state == StreamState.PAUSED) return@launch // paused on the glasses (temple tap): expected
          if (!allowRetry) {
            log("still no frames after retrying (state $state)")
            emit("camera.error", json("error" to "NO_FRAMES", "message" to "The camera sent no video (state $state). Stop and start again, or reconnect the session."))
            return@launch
          }
          info("No frames in ${STREAM_WATCHDOG_MS / 1000} s (state $state); restarting the stream…")
          cameraMutex.withLock {
            if (wantedConfig !== cfg || camera !== cam || MDSession.startedSession() !== session) return@withLock
            detachCamera(session)
            attach(session, cfg, allowRetry = false)
          }
        }
  }

  /** Stops the camera, waits for it to confirm, and only then removes the capability from the session. */
  private suspend fun detachCamera(session: DeviceSession) {
    val cam = camera
    clearLocalState()
    if (cam != null) stopAndRemove(session, cam)
  }

  private suspend fun stopAndRemove(session: DeviceSession, cam: Camera) {
    log("stopping the camera")
    cam.stop()
    val stopped = withTimeoutOrNull(3_000) { cam.state.first { it == CameraState.STOPPED } }
    if (stopped == null) log("camera did not confirm STOPPED within 3 s (state ${cam.state.value.name}); removing anyway")
    session.removeCamera().onFailure { e, _ -> log("removeCamera: ${e.name} (ignored)") }
  }

  // ───────────── frames (triple buffer in direct memory) ─────────────

  private val frameLock = Any()
  private val slots = arrayOfNulls<ByteBuffer>(3)
  private var readySlot = -1
  private var inUseSlot = -1
  @Volatile private var frameWidth = 0
  @Volatile private var frameHeight = 0
  @Volatile private var frameSize = 0
  @Volatile private var frameCounter = 0
  @Volatile private var frameTimestampUs = 0L
  /** 0 = not detected yet, 1 = I420 (planar), 2 = NV12/NV21 (semi-planar). */
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
      // Padded rows: infer the stride and copy only the useful pixels
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
    // Chroma plane(s): h/2 rows with the same stride (semi-planar) or stride/2 (planar)
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
   * Heuristic: in semi-planar (UVUV...) neighboring bytes alternate between U and V, so the
   * difference to the immediate neighbor is much larger than to the byte 2 positions ahead.
   */
  private fun detectLayout(buf: ByteBuffer, w: Int, h: Int) {
    val start = w * h
    val len = w * h / 2
    var d1 = 0L
    var d2 = 0L
    var i = start
    val end = start + len - 2
    val step = maxOf(2, len / 4000) or 1 // sampling
    while (i < end) {
      val a = buf.get(i).toInt() and 0xFF
      val b = buf.get(i + 1).toInt() and 0xFF
      val c = buf.get(i + 2).toInt() and 0xFF
      d1 += abs(a - b)
      d2 += abs(a - c)
      i += step
    }
    if (d1 + d2 < 200) return // colorless (gray) image: inconclusive
    val guess = if (d1 > d2 * 3 / 2) 2 else 1
    if (guess == frameLayout) layoutVotes++ else {
      frameLayout = guess
      layoutVotes = 1
    }
  }

  /** [width, height, size, counter, layout] of the latest frame. */
  @JvmStatic
  fun frameInfo(): IntArray =
      synchronized(frameLock) { intArrayOf(frameWidth, frameHeight, frameSize, frameCounter, frameLayout) }

  @JvmStatic fun frameCounter(): Int = frameCounter

  @JvmStatic fun frameTimestampUs(): Long = frameTimestampUs

  /** Reserves the latest frame (direct ByteBuffer). Call [releaseFrame] after reading it. */
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

  /** Copy of the current frame as JPEG (useful to save it or send it to an API). */
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
        // I420 -> NV21 (interleaved VU)
        val q = uvSize / 2
        for (i in 0 until q) {
          nv21[w * h + i * 2] = chroma[q + i]
          nv21[w * h + i * 2 + 1] = chroma[i]
        }
      } else {
        // NV12 -> NV21 (swap U and V)
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

  // ───────────── audio (16-bit PCM, little endian) ─────────────

  private val audioLock = Any()
  private var audioBuffer = ByteArray(0)
  private var audioLength = 0
  @Volatile private var audioRateConfigured = 0
  @Volatile private var audioChannelsConfigured = 1
  private const val MAX_AUDIO_BYTES = 48000 * 2 * 2 * 2 // ~2 s at 48 kHz stereo

  private fun onAudioFrame(frame: AudioFrame) {
    val src = frame.buffer.duplicate()
    val n = src.remaining()
    synchronized(audioLock) {
      if (audioLength + n > MAX_AUDIO_BYTES) audioLength = 0 // Unity is not consuming: drop
      if (audioBuffer.size < audioLength + n) audioBuffer = audioBuffer.copyOf(maxOf(audioLength + n, 8192))
      src.get(audioBuffer, audioLength, n)
      audioLength += n
    }
  }

  /** PCM bytes accumulated since the last call. */
  @JvmStatic
  fun drainAudio(): ByteArray =
      synchronized(audioLock) {
        val out = audioBuffer.copyOf(audioLength)
        audioLength = 0
        out
      }

  /** Configured [sampleRate, channels] (0 = no audio). */
  @JvmStatic fun audioFormat(): IntArray = intArrayOf(audioRateConfigured, audioChannelsConfigured)

  // ───────────── photos ─────────────

  private val photos = ConcurrentHashMap<Int, ByteArray>()

  /** Photo while the stream is active. Result: "camera.photo" (use [takePhoto]) or "camera.photoFailed". */
  @JvmStatic
  fun capturePhoto(requestId: Int) {
    Bridge.scope.launch {
      val cam = camera
      if (cam == null) {
        emit("camera.photoFailed", json("requestId" to requestId, "error" to "NOT_STREAMING", "message" to "Start the stream first"))
        return@launch
      }
      val result = cam.stream.capturePhoto()
      val data = result.getOrNull()
      if (data == null) {
        val e = result.errorOrNull()
        emit("camera.photoFailed", json("requestId" to requestId, "error" to Bridge.errorName(e), "message" to (e?.description ?: "Capture failed")))
        return@launch
      }
      // fold() is not inline, so the (suspending) conversion stays outside of it
      val jpeg = withContext(Dispatchers.Default) { photoToJpeg(data) }
      if (jpeg == null) {
        emit("camera.photoFailed", json("requestId" to requestId, "error" to "DECODE_FAILED", "message" to "Unsupported format"))
      } else {
        photos[requestId] = jpeg
        emit("camera.photo", json("requestId" to requestId, "bytes" to jpeg.size))
      }
    }
  }

  /**
   * Standalone high-quality photo (experimental). Pauses the stream (if any), takes the photo
   * and then resumes the stream. resolution: SMALL/MEDIUM/LARGE/FULL, quality: LOW/MEDIUM/HIGH.
   */
  @JvmStatic
  fun captureHighQualityPhoto(resolution: String, quality: String, requestId: Int) {
    Bridge.scope.launch {
      if (hqPhotoBusy) {
        emit("camera.photoFailed", json("requestId" to requestId, "error" to "BUSY", "message" to "A high-quality photo is already in progress"))
        return@launch
      }
      hqPhotoBusy = true
      var encoded: ByteArray? = null
      try {
        cameraMutex.withLock {
          val session = MDSession.startedSession()
          if (session == null) {
            emit("camera.photoFailed", json("requestId" to requestId, "error" to "NO_SESSION", "message" to "Connect to the glasses first"))
            return@withLock
          }
          if (!Permissions.ensure(Permission.CAMERA)) {
            emit("camera.photoFailed", json("requestId" to requestId, "error" to "PERMISSION_DENIED", "message" to "Camera permission not granted (status: ${Permissions.lastStatus(Permission.CAMERA)})"))
            return@withLock
          }
          log("HQ photo: releasing the stream camera (if any)")
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
            emit("camera.photoFailed", json("requestId" to requestId, "error" to Bridge.errorName(e), "message" to (e?.description ?: "Could not attach the camera")))
            return@withLock
          }
          val photo = cam.photo
          var lastReported = -1.0
          val progressJob =
              Bridge.scope.launch {
                photo.transferProgressStream.collect {
                  // Bluetooth transfer is slow (FULL ≈ 1.6 MB ≈ 1 min): report every 5%
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
                  log("HQ photo: capturing ($resolution, $quality)")
                  photo.capturePhoto(
                      runCatching { PhotoResolution.valueOf(resolution.uppercase()) }.getOrDefault(PhotoResolution.FULL),
                      runCatching { PhotoQuality.valueOf(quality.uppercase()) }.getOrDefault(PhotoQuality.HIGH),
                  )
                  result.await().getOrThrow()
                }
            log("HQ photo received: ${encoded?.size} bytes")
          } catch (t: TimeoutCancellationException) {
            emit("camera.photoFailed", json("requestId" to requestId, "error" to "TIMEOUT", "message" to "The photo did not arrive within ${HQ_PHOTO_TIMEOUT_MS / 1000} s (it is transferred over Bluetooth; try a lower resolution)"))
          } catch (t: Throwable) {
            emit("camera.photoFailed", json("requestId" to requestId, "error" to "CAPTURE_FAILED", "message" to (t.message ?: t.toString())))
          } finally {
            progressJob.cancel()
            photo.stop()
            stopAndRemove(session, cam)
          }
          // Resume the video if it was requested before or during the photo.
          val cfg = wantedConfig
          if (cfg != null && MDSession.startedSession() === session) {
            info("Photo done; resuming the video")
            attach(session, cfg, allowRetry = true)
          }
        }
      } finally {
        hqPhotoBusy = false
      }
      // HEIC → JPEG conversion is slow for large photos: keep it outside the camera queue.
      val bytes = encoded ?: return@launch
      val jpeg = withContext(Dispatchers.Default) { encodedToJpeg(bytes) } ?: bytes
      photos[requestId] = jpeg
      emit("camera.photo", json("requestId" to requestId, "bytes" to jpeg.size, "highQuality" to true))
    }
  }

  /** Takes (and deletes) the finished photo of a request. JPEG. */
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
