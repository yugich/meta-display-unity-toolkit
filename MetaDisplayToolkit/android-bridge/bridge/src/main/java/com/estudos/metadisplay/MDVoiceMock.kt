package com.estudos.metadisplay

import android.app.Activity
import android.net.Uri
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import com.estudos.metadisplay.Bridge.emit
import com.estudos.metadisplay.Bridge.json
import com.meta.wearable.dat.core.Wearables
import com.meta.wearable.dat.core.selectors.AutoDeviceSelector
import com.meta.wearable.dat.core.types.ChargingState
import com.meta.wearable.dat.core.types.Permission
import com.meta.wearable.dat.core.types.PermissionStatus
import com.meta.wearable.dat.core.types.ThermalLevel
import com.meta.wearable.dat.core.voiceinvocations.VoiceInvocationsStream
import com.meta.wearable.dat.core.voiceinvocations.isVoiceInvocationsIntent
import com.meta.wearable.dat.core.voiceinvocations.startVoiceInvocationsStream
import com.meta.wearable.dat.core.voiceinvocations.types.actions.LaunchApp
import com.meta.wearable.dat.core.voiceinvocations.types.actions.ResponseHandle
import com.meta.wearable.dat.inputs.types.ButtonType
import com.meta.wearable.dat.inputs.types.CapturePressType
import com.meta.wearable.dat.inputs.types.DragAction
import com.meta.wearable.dat.inputs.types.InputSource
import com.meta.wearable.dat.mockdevice.MockDeviceKit
import com.meta.wearable.dat.mockdevice.api.GlassesModel
import com.meta.wearable.dat.mockdevice.api.MockDeviceKitConfig
import com.meta.wearable.dat.mockdevice.api.MockGlasses
import com.meta.wearable.dat.mockdevice.api.camera.CameraFacing
import java.io.File
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

// ═══════════════════════════════ VOICE INVOCATIONS ═══════════════════════════════

/**
 * "Hey Meta, start {nome do app}" (experimental). O nome falado é configurado no
 * Wearables Developer Center. Não precisa de sessão ativa.
 */
object MDVoice {
  private var stream: VoiceInvocationsStream? = null
  private val jobs = mutableListOf<Job>()
  private val pending = ConcurrentHashMap<Int, ResponseHandle>()
  private val nextId = AtomicInteger(1)

  @JvmStatic
  fun start() {
    Bridge.scope.launch {
      if (stream != null) return@launch
      val s = Wearables.startVoiceInvocationsStream(AutoDeviceSelector())
      stream = s
      jobs +=
          Bridge.scope.launch {
            s.invocations.collect { inv ->
              when (inv) {
                is LaunchApp -> {
                  val id = nextId.getAndIncrement()
                  pending[id] = inv.responseHandle
                  emit("voice.launchApp", json("requestId" to id))
                }
                else -> emit("voice.unknown", json("type" to inv::class.java.simpleName))
              }
            }
          }
      jobs += Bridge.scope.launch { s.state.collect { emit("voice.state", json("state" to it.name)) } }
      jobs += Bridge.scope.launch { s.errors.collect { e -> emit("voice.error", json("error" to e.name, "message" to e.description)) } }
    }
  }

  @JvmStatic
  fun stop() {
    Bridge.scope.launch {
      jobs.forEach { it.cancel() }
      jobs.clear()
      stream?.close()
      stream = null
      emit("voice.state", json("state" to "STOPPED"))
    }
  }

  /** Toda invocação precisa ser respondida exatamente uma vez. */
  @JvmStatic
  fun respond(id: Int, success: Boolean, message: String) {
    Bridge.scope.launch {
      val handle = pending.remove(id) ?: return@launch
      val output = message.ifEmpty { null }
      val delivered = if (success) handle.sendSuccess(output) else handle.sendFailure(message.ifEmpty { "failed" })
      emit("voice.responded", json("requestId" to id, "delivered" to delivered))
    }
  }

  /** O app foi aberto por "Hey Meta"? (cold start ou quando o Unity recebe um novo intent) */
  @JvmStatic
  fun isVoiceLaunch(activity: Activity): Boolean =
      runCatching { isVoiceInvocationsIntent(activity.intent) }.getOrDefault(false)

  internal fun checkLaunchIntent(activity: Activity) {
    if (isVoiceLaunch(activity)) emit("voice.launchIntent", json())
  }
}

// ═══════════════════════════════ MOCK DEVICE KIT ═══════════════════════════════

/** Óculos simulados para testar sem hardware (inclui preview do display na tela do celular). */
object MDMock {
  private var glasses: MockGlasses? = null
  private var previewView: View? = null

  private fun kit() = MockDeviceKit.getInstance(Bridge.appContext)

  @JvmStatic
  fun enable(initiallyRegistered: Boolean, permissionsGranted: Boolean) {
    Bridge.scope.launch {
      kit().enable(MockDeviceKitConfig(initiallyRegistered = initiallyRegistered, initialPermissionsGranted = permissionsGranted))
      emit("mock.state", json("enabled" to true))
    }
  }

  @JvmStatic
  fun disable() {
    Bridge.scope.launch {
      hidePreviewNow()
      glasses = null
      kit().stopTestServer()
      kit().disable()
      emit("mock.state", json("enabled" to false))
    }
  }

  @JvmStatic fun isEnabled(): Boolean = Bridge.isReady && kit().isEnabled

  /** model: META_RAYBAN_DISPLAY, RAYBAN_META, ... Já liga e "veste" os óculos se [powerOnAndDon]. */
  @JvmStatic
  fun pairGlasses(model: String, powerOnAndDon: Boolean) {
    Bridge.scope.launch {
      val m = runCatching { GlassesModel.valueOf(model.uppercase()) }.getOrDefault(GlassesModel.META_RAYBAN_DISPLAY)
      kit()
          .pairGlasses(m)
          .fold(
              onSuccess = { g ->
                glasses = g
                if (powerOnAndDon) {
                  g.powerOn()
                  g.unfold()
                  g.don()
                }
                emit("mock.paired", json("id" to g.deviceIdentifier.identifier, "model" to m.name))
              },
              onFailure = { e, _ -> Bridge.error("mock", e.description, Bridge.errorName(e)) },
          )
    }
  }

  @JvmStatic
  fun unpairAll() {
    Bridge.scope.launch {
      kit().pairedDevices.toList().forEach { kit().unpairDevice(it) }
      glasses = null
    }
  }

  /** action: POWER_ON, POWER_OFF, DON, DOFF, FOLD, UNFOLD */
  @JvmStatic
  fun glassesAction(action: String) {
    Bridge.scope.launch {
      val g = glasses ?: return@launch Bridge.error("mock", "Nenhum óculos simulado pareado", "NO_MOCK")
      when (action.uppercase()) {
        "POWER_ON" -> g.powerOn()
        "POWER_OFF" -> g.powerOff()
        "DON" -> g.don()
        "DOFF" -> g.doff()
        "FOLD" -> g.fold()
        "UNFOLD" -> g.unfold()
        else -> Bridge.error("mock", "Ação desconhecida: $action", "INVALID_ACTION")
      }
    }
  }

  @JvmStatic
  fun setDeviceState(battery: Int, charging: String, thermal: String) {
    Bridge.scope.launch {
      val g = glasses ?: return@launch
      if (battery >= 0) g.setBatteryLevel(battery.coerceIn(0, 100))
      runCatching { ChargingState.valueOf(charging.uppercase()) }.getOrNull()?.let { g.setChargingState(it) }
      runCatching { ThermalLevel.valueOf(thermal.uppercase()) }.getOrNull()?.let { g.setThermalLevel(it) }
    }
  }

  @JvmStatic
  fun simulateRegistration(success: Boolean) {
    Bridge.scope.launch { kit().simulateRegistrationOutcome(success) }
  }

  /** status: GRANTED ou DENIED. asRequestResult = true controla o resultado de um pedido futuro. */
  @JvmStatic
  fun setPermission(permission: String, status: String, asRequestResult: Boolean) {
    Bridge.scope.launch {
      val p = runCatching { Permission.valueOf(permission.uppercase()) }.getOrNull() ?: return@launch
      val s: PermissionStatus = if (status.uppercase() == "GRANTED") PermissionStatus.Granted else PermissionStatus.Denied
      if (asRequestResult) kit().permissions.setRequestResult(p, s) else kit().permissions.set(p, s)
    }
  }

  // ── câmera simulada ──

  /** Vídeo H.264/H.265 local (caminho absoluto, ex. Application.persistentDataPath). */
  @JvmStatic
  fun setCameraFeedFile(path: String) {
    Bridge.scope.launch { glasses?.services?.camera?.setCameraFeed(Uri.fromFile(File(path))) }
  }

  /** FRONT ou BACK: usa a câmera do celular como feed (precisa de android.permission.CAMERA). */
  @JvmStatic
  fun setCameraFeedPhoneCamera(facing: String) {
    Bridge.scope.launch {
      val f = runCatching { CameraFacing.valueOf(facing.uppercase()) }.getOrDefault(CameraFacing.BACK)
      glasses?.services?.camera?.setCameraFeed(f)
    }
  }

  @JvmStatic
  fun setCapturedImageFile(path: String) {
    Bridge.scope.launch {
      val uri = Uri.fromFile(File(path))
      glasses?.services?.camera?.setCapturedImage(uri)
      glasses?.services?.cameraCapture?.setCapturedPhoto(uri)
    }
  }

  // ── inputs simulados ──

  /** kind: NAV_UP, NAV_DOWN, NAV_LEFT, NAV_RIGHT, SELECT, BACK, BUTTON, CAPTURE_SHORT, CAPTURE_HOLD, CAPTURE_DOUBLE, TAP, TAP_HOLD */
  @JvmStatic
  fun injectInput(kind: String) {
    Bridge.scope.launch {
      val services = glasses?.services ?: return@launch
      val input = services.input
      val src = InputSource.CAPTOUCH
      when (kind.uppercase()) {
        "NAV_UP" -> input.navUp(src)
        "NAV_DOWN" -> input.navDown(src)
        "NAV_LEFT" -> input.navLeft(src)
        "NAV_RIGHT" -> input.navRight(src)
        "SELECT" -> input.select(src)
        "BACK" -> input.back(src)
        "BUTTON" -> input.button(ButtonType.ACTION)
        "CAPTURE_SHORT" -> input.capture(CapturePressType.SHORT_PRESS)
        "CAPTURE_HOLD" -> input.capture(CapturePressType.HOLD)
        "CAPTURE_DOUBLE" -> input.capture(CapturePressType.DOUBLE_PRESS)
        "TAP" -> services.captouch.tap()
        "TAP_HOLD" -> services.captouch.tapAndHold()
        else -> Bridge.error("mock", "Input desconhecido: $kind", "INVALID_INPUT")
      }
    }
  }

  @JvmStatic
  fun injectDrag(action: String, x: Float, y: Float, dx: Float, dy: Float) {
    Bridge.scope.launch {
      val a = runCatching { DragAction.valueOf(action.uppercase()) }.getOrNull() ?: return@launch
      glasses?.services?.input?.drag(a, x, y, dx, dy)
    }
  }

  // ── fala e voz simuladas ──

  @JvmStatic
  fun simulateTranscription(text: String, isFinal: Boolean, confidence: Float) {
    Bridge.scope.launch { glasses?.services?.speech?.simulateTranscription(text, isFinal, confidence) }
  }

  @JvmStatic
  fun simulateVoiceLaunch() {
    Bridge.scope.launch {
      val voice = glasses?.services?.voiceInvocation ?: return@launch
      if (!voice.hasConnectedApps()) {
        Bridge.error("mock", "Nenhum app escutando voice invocations (chame MetaVoiceInvocations.Start)", "NO_LISTENER")
        return@launch
      }
      voice.simulateLaunchAppAction()
    }
  }

  // ── preview do display e servidor de teste ──

  /** Mostra o que apareceria nos óculos num quadrado sobre o Unity (sizePx, canto: TOP_RIGHT etc.). */
  @JvmStatic
  fun showDisplayPreview(activity: Activity, sizePx: Int, corner: String) {
    activity.runOnUiThread {
      hidePreviewNow()
      val g = glasses ?: return@runOnUiThread Bridge.error("mock", "Pareie óculos simulados primeiro", "NO_MOCK")
      val view = g.services.display.createPreviewView(activity)
      val gravity =
          when (corner.uppercase()) {
            "TOP_LEFT" -> Gravity.TOP or Gravity.START
            "BOTTOM_LEFT" -> Gravity.BOTTOM or Gravity.START
            "BOTTOM_RIGHT" -> Gravity.BOTTOM or Gravity.END
            "CENTER" -> Gravity.CENTER
            else -> Gravity.TOP or Gravity.END
          }
      activity.addContentView(view, FrameLayout.LayoutParams(sizePx, sizePx, gravity))
      previewView = view
    }
  }

  @JvmStatic
  fun hideDisplayPreview(activity: Activity) {
    activity.runOnUiThread { hidePreviewNow() }
  }

  private fun hidePreviewNow() {
    previewView?.let { (it.parent as? ViewGroup)?.removeView(it) }
    previewView = null
  }

  /** Servidor do MockDeviceKit para preview no Chrome do PC (adb forward tcp:PORT tcp:PORT). */
  @JvmStatic
  fun startTestServer(port: Int) {
    Bridge.scope.launch {
      kit()
          .startTestServer(port)
          .fold(
              onSuccess = { p -> emit("mock.testServer", json("port" to p, "url" to "http://127.0.0.1:$p/", "adb" to "adb forward tcp:$p tcp:$p")) },
              onFailure = { e, _ -> Bridge.error("mock", e.description, Bridge.errorName(e)) },
          )
    }
  }

  @JvmStatic
  fun stopTestServer() {
    Bridge.scope.launch { kit().stopTestServer() }
  }
}
