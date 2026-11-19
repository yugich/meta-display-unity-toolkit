package com.metadisplay.bridge

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import com.metadisplay.bridge.Bridge.emit
import com.metadisplay.bridge.Bridge.json
import com.meta.wearable.dat.core.session.DeviceSession
import com.meta.wearable.dat.display.Display
import com.meta.wearable.dat.display.addDisplay
import com.meta.wearable.dat.display.removeDisplay
import com.meta.wearable.dat.display.types.DisplayConfiguration
import com.meta.wearable.dat.display.types.DisplayState
import com.meta.wearable.dat.display.types.VideoCodec
import com.meta.wearable.dat.display.types.VideoSource
import com.meta.wearable.dat.display.views.ActionRole
import com.meta.wearable.dat.display.views.Alignment
import com.meta.wearable.dat.display.views.ButtonGroupAlignment
import com.meta.wearable.dat.display.views.ButtonStyle
import com.meta.wearable.dat.display.views.ContentScope
import com.meta.wearable.dat.display.views.CornerRadius
import com.meta.wearable.dat.display.views.Direction
import com.meta.wearable.dat.display.views.FlexBoxBackground
import com.meta.wearable.dat.display.views.FlexBoxScope
import com.meta.wearable.dat.display.views.IconName
import com.meta.wearable.dat.display.views.IconStyle
import com.meta.wearable.dat.display.views.ImageSize
import com.meta.wearable.dat.display.views.TextColor
import com.meta.wearable.dat.display.views.TextStyle
import com.meta.wearable.dat.display.views.VideoPlayer
import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.launch
import org.json.JSONObject

/**
 * Glasses display. C# sends the whole screen as JSON (see GlassesNode.cs) and this class
 * rebuilds the tree with the official DSL (flexBox, text, image, button, icon, buttonGroup).
 */
object MDDisplay : SessionCapability {
  private var wanted = false
  private var display: Display? = null
  private var stateJob: Job? = null

  private var player: VideoPlayer? = null
  private val videoJobs = mutableListOf<Job>()

  /** Images sent by Unity (PNG/JPG), already decoded, by key. */
  private val images = ConcurrentHashMap<String, Deferred<Bitmap?>>()

  // ───────────── capability ─────────────

  /** Requests the Display. If the session has not started yet, it is attached when it reaches STARTED. */
  @JvmStatic
  fun enable() {
    Bridge.scope.launch {
      wanted = true
      MDSession.startedSession()?.let { attach(it) }
    }
  }

  @JvmStatic
  fun disable() {
    Bridge.scope.launch {
      wanted = false
      release()
      MDSession.session?.removeDisplay()?.onFailure { e, _ ->
        Bridge.error("display", e.description, e.name)
      }
      emit("display.state", json("state" to "STOPPED"))
    }
  }

  @JvmStatic fun state(): String = display?.state?.value?.name ?: "STOPPED"

  override fun onSessionStarted(session: DeviceSession) {
    if (wanted) attach(session)
  }

  override fun onSessionStopped() = release()

  private fun attach(session: DeviceSession) {
    if (display != null) return
    session
        .addDisplay(DisplayConfiguration())
        .fold(
            onSuccess = { d ->
              display = d
              stateJob =
                  Bridge.scope.launch {
                    d.state.collect { st -> emit("display.state", json("state" to st.name)) }
                  }
            },
            onFailure = { e, _ ->
              emit("display.error", json("error" to e.name, "message" to e.description))
            },
        )
  }

  private fun release() {
    closeVideo()
    stateJob?.cancel()
    stateJob = null
    display = null
  }

  private fun readyDisplay(requestId: Int): Display? {
    val d = display
    if (d == null || d.state.value != DisplayState.STARTED) {
      emit(
          "display.sendFailed",
          json(
              "requestId" to requestId,
              "error" to "NOT_READY",
              "message" to "Display is not ready (state: ${d?.state?.value?.name ?: "no display"})",
          ),
      )
      return null
    }
    return d
  }

  // ───────────── content ─────────────

  /** Sends a complete screen (replaces the previous one). Result: "display.sent" or "display.sendFailed". */
  @JvmStatic
  fun sendLayout(layoutJson: String, requestId: Int) {
    Bridge.scope.launch {
      val d = readyDisplay(requestId) ?: return@launch
      val root =
          runCatching { JSONObject(layoutJson) }
              .getOrElse {
                emit("display.sendFailed", json("requestId" to requestId, "error" to "INVALID_JSON", "message" to it.message))
                return@launch
              }
      // Wait for the referenced images to finish decoding
      val bitmaps = HashMap<String, Bitmap>()
      collectImageKeys(root).forEach { key -> images[key]?.await()?.let { bitmaps[key] = it } }
      closeVideo()
      val result =
          runCatching { d.sendContent { LayoutBuilder.buildRoot(this, root, bitmaps) } }
              .getOrElse {
                emit("display.sendFailed", json("requestId" to requestId, "error" to "BUILD_FAILED", "message" to it.message))
                return@launch
              }
      result.fold(
          onSuccess = { emit("display.sent", json("requestId" to requestId)) },
          onFailure = { e, _ ->
            emit("display.sendFailed", json("requestId" to requestId, "error" to e.name, "message" to e.description))
          },
      )
    }
  }

  @JvmStatic
  fun clear() {
    Bridge.scope.launch {
      val d = display ?: return@launch
      closeVideo()
      d.clearDisplay().onFailure { e, _ -> Bridge.error("display", e.description, e.name) }
    }
  }

  // ───────────── images ─────────────

  /**
   * Registers an image (PNG/JPG) under [key]. Decoding starts right away, so a
   * sendLayout called right after already finds the image.
   */
  @JvmStatic
  fun setImage(key: String, encoded: ByteArray, maxSize: Int) {
    images[key] =
        Bridge.scope.async(Dispatchers.Default, start = CoroutineStart.DEFAULT) {
          val decoded = BitmapFactory.decodeByteArray(encoded, 0, encoded.size)
          if (decoded == null) {
            Bridge.error("display", "Invalid image: $key", "INVALID_IMAGE")
            null
          } else {
            downscale(decoded, if (maxSize > 0) maxSize else 600)
          }
        }
  }

  @JvmStatic fun hasImage(key: String): Boolean = images.containsKey(key)

  @JvmStatic
  fun removeImage(key: String) {
    images.remove(key)
  }

  @JvmStatic
  fun clearImages() {
    images.clear()
  }

  private fun downscale(bmp: Bitmap, maxSize: Int): Bitmap {
    val largest = maxOf(bmp.width, bmp.height)
    if (largest <= maxSize) return bmp
    val scale = maxSize.toFloat() / largest
    return Bitmap.createScaledBitmap(bmp, (bmp.width * scale).toInt().coerceAtLeast(1), (bmp.height * scale).toInt().coerceAtLeast(1), true)
  }

  private fun collectImageKeys(node: JSONObject, out: MutableSet<String> = mutableSetOf()): Set<String> {
    node.optString("imageKey").takeIf { it.isNotEmpty() }?.let { out += it }
    node.optJSONArray("children")?.let { arr ->
      for (i in 0 until arr.length()) arr.optJSONObject(i)?.let { collectImageKeys(it, out) }
    }
    return out
  }

  // ───────────── video ─────────────

  /** MP4 video from an HTTPS URL (up to 400 px per side and 70,000 px total). Takes the whole screen. */
  @JvmStatic
  fun playVideo(url: String, requestId: Int) {
    Bridge.scope.launch {
      val d = readyDisplay(requestId) ?: return@launch
      closeVideo()
      val p = VideoPlayer(VideoSource.Url(url), VideoCodec.MP4)
      player = p
      videoJobs += Bridge.scope.launch { p.state.collect { st -> emit("video.state", json("state" to st.name)) } }
      videoJobs +=
          Bridge.scope.launch {
            p.error.collect { e -> if (e != null) emit("video.error", json("error" to e.name, "message" to e.description)) }
          }
      d.sendContent { video(player = p) }
          .fold(
              onSuccess = {
                emit("display.sent", json("requestId" to requestId))
                p.play()
              },
              onFailure = { e, _ ->
                emit("display.sendFailed", json("requestId" to requestId, "error" to e.name, "message" to e.description))
              },
          )
    }
  }

  @JvmStatic
  fun stopVideo() {
    Bridge.scope.launch { closeVideo() }
  }

  private fun closeVideo() {
    videoJobs.forEach { it.cancel() }
    videoJobs.clear()
    player?.close()
    player = null
  }
}

/** Converts the JSON sent by Unity into the mwdat-display DSL. */
internal object LayoutBuilder {

  fun buildRoot(scope: ContentScope, root: JSONObject, bitmaps: Map<String, Bitmap>) {
    scope.flexBox(
        direction = enumOr(root, "direction", Direction.COLUMN),
        gap = root.optInt("gap", 0),
        alignment = enumOr(root, "alignment", Alignment.START),
        crossAlignment = enumOr(root, "crossAlignment", Alignment.START),
        wrap = root.optBoolean("wrap", false),
        padding = root.optInt("padding", 0),
        paddingTop = optIntOrNull(root, "paddingTop"),
        paddingBottom = optIntOrNull(root, "paddingBottom"),
        paddingStart = optIntOrNull(root, "paddingStart"),
        paddingEnd = optIntOrNull(root, "paddingEnd"),
        background = enumOr(root, "background", FlexBoxBackground.NONE),
        onClick = clickHandler(root),
    ) {
      children(this, root, bitmaps)
    }
  }

  private fun children(scope: FlexBoxScope, node: JSONObject, bitmaps: Map<String, Bitmap>) {
    val arr = node.optJSONArray("children") ?: return
    for (i in 0 until arr.length()) {
      val child = arr.optJSONObject(i) ?: continue
      child(scope, child, bitmaps)
    }
  }

  private fun child(scope: FlexBoxScope, n: JSONObject, bitmaps: Map<String, Bitmap>) {
    val grow = n.optDouble("flexGrow", 0.0).toFloat()
    val shrink = n.optDouble("flexShrink", 0.0).toFloat()
    val alignSelf = enumOrNull<Alignment>(n, "alignSelf")
    when (n.optString("type")) {
      "flex" ->
          scope.flexBox(
              direction = enumOr(n, "direction", Direction.COLUMN),
              gap = n.optInt("gap", 0),
              alignment = enumOr(n, "alignment", Alignment.START),
              crossAlignment = enumOr(n, "crossAlignment", Alignment.START),
              wrap = n.optBoolean("wrap", false),
              padding = n.optInt("padding", 0),
              paddingTop = optIntOrNull(n, "paddingTop"),
              paddingBottom = optIntOrNull(n, "paddingBottom"),
              paddingStart = optIntOrNull(n, "paddingStart"),
              paddingEnd = optIntOrNull(n, "paddingEnd"),
              background = enumOr(n, "background", FlexBoxBackground.NONE),
              onClick = clickHandler(n),
              flexGrow = grow,
              flexShrink = shrink,
              alignSelf = alignSelf,
          ) {
            children(this, n, bitmaps)
          }
      "text" ->
          scope.text(
              n.optString("text"),
              style = enumOr(n, "style", TextStyle.BODY),
              color = enumOr(n, "color", TextColor.PRIMARY),
              flexGrow = grow,
              flexShrink = shrink,
              alignSelf = alignSelf,
          )
      "image" -> {
        val bitmap = n.optString("imageKey").takeIf { it.isNotEmpty() }?.let { bitmaps[it] }
        val uri = n.optString("uri").takeIf { it.isNotEmpty() }
        if (bitmap == null && uri == null) return
        scope.image(
            uri = if (bitmap == null) uri else null,
            bitmap = bitmap,
            sizePreset = enumOr(n, "size", ImageSize.FILL),
            cornerRadius = enumOr(n, "cornerRadius", CornerRadius.NONE),
            flexGrow = grow,
            flexShrink = shrink,
            alignSelf = alignSelf,
        )
      }
      "button" ->
          scope.button(
              label = n.optString("label"),
              style = enumOr(n, "style", ButtonStyle.PRIMARY),
              iconName = enumOrNull<IconName>(n, "icon"),
              onClick = clickHandler(n),
              flexGrow = grow,
              flexShrink = shrink,
              alignSelf = alignSelf,
              actionRole = if (n.optBoolean("primary", false)) ActionRole.PRIMARY else null,
          )
      "icon" -> {
        val name = enumOrNull<IconName>(n, "icon") ?: return
        scope.icon(
            name = name,
            style = enumOr(n, "iconStyle", IconStyle.FILLED),
            flexGrow = grow,
            flexShrink = shrink,
            alignSelf = alignSelf,
        )
      }
      "buttonGroup" ->
          scope.buttonGroup(
              alignment = enumOr(n, "alignment", ButtonGroupAlignment.CENTER),
              flexGrow = grow,
              flexShrink = shrink,
              alignSelf = alignSelf,
          ) {
            val arr = n.optJSONArray("children") ?: return@buttonGroup
            for (i in 0 until arr.length()) {
              val b = arr.optJSONObject(i) ?: continue
              button(
                  label = b.optString("label"),
                  style = enumOr(b, "style", ButtonStyle.PRIMARY),
                  iconName = enumOrNull<IconName>(b, "icon"),
                  onClick = clickHandler(b),
                  actionRole = if (b.optBoolean("primary", false)) ActionRole.PRIMARY else null,
              )
            }
          }
      else -> Unit
    }
  }

  private fun clickHandler(n: JSONObject): (() -> Unit)? {
    val id = n.optString("clickId")
    if (id.isEmpty()) return null
    return { emit("display.click", json("id" to id)) }
  }

  private fun optIntOrNull(n: JSONObject, key: String): Int? =
      if (n.has(key) && !n.isNull(key)) n.optInt(key) else null

  private inline fun <reified T : Enum<T>> enumOrNull(n: JSONObject, key: String): T? {
    val raw = n.optString(key)
    if (raw.isEmpty() || raw == "null") return null
    return runCatching { enumValueOf<T>(raw.uppercase()) }.getOrNull()
  }

  private inline fun <reified T : Enum<T>> enumOr(n: JSONObject, key: String, fallback: T): T =
      enumOrNull<T>(n, key) ?: fallback
}
