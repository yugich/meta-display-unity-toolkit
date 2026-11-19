package com.metadisplay.bridge

import android.app.Activity
import com.metadisplay.bridge.Bridge.emit
import com.metadisplay.bridge.Bridge.json
import com.meta.wearable.dat.core.Wearables
import com.meta.wearable.dat.core.selectors.AutoDeviceSelector
import com.meta.wearable.dat.core.selectors.SpecificDeviceSelector
import com.meta.wearable.dat.core.session.DeviceSession
import com.meta.wearable.dat.core.session.DeviceSessionState
import com.meta.wearable.dat.core.types.Device
import com.meta.wearable.dat.core.types.DeviceIdentifier
import com.meta.wearable.dat.core.types.Permission
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject

/** Meta AI registration, devices, permissions and update screens. */
object MDCore {
  const val SDK_VERSION = "1.0.1"
  const val BRIDGE_VERSION = "1.0.0"

  private var initialized = false
  private val metadataJobs = mutableMapOf<DeviceIdentifier, Job>()

  /** First method to call. Can be called again (e.g. when the scene reloads). */
  @JvmStatic
  fun initialize(activity: Activity, listener: MetaDisplayListener) {
    Bridge.listener = listener
    Bridge.activity = activity
    Bridge.scope.launch {
      Bridge.appContext = activity.applicationContext
      if (!initialized) {
        // If MockDeviceKit already initialized the SDK, this returns ALREADY_INITIALIZED (harmless).
        Wearables.initialize(Bridge.appContext)
        initialized = true
        launch {
          Wearables.registrationState.collect {
            emit("registration", json("state" to it.name))
          }
        }
        launch {
          Wearables.registrationErrorStream.collect {
            Bridge.error("registration", it.getLocalizedDescription(Bridge.appContext), it.name)
          }
        }
        launch { Wearables.devices.collect { ids -> onDevicesChanged(ids) } }
      } else {
        emit("registration", json("state" to Wearables.registrationState.value.name))
        onDevicesChanged(Wearables.devices.value, forceMetadata = true)
      }
      emit(
          "initialized",
          json(
              "sdkVersion" to SDK_VERSION,
              "bridgeVersion" to BRIDGE_VERSION,
              "devMode" to Wearables.isDevMode,
              "modules" to Bridge.jsonArray(Modules.available()),
          ),
      )
      MDVoice.checkLaunchIntent(activity)
    }
  }

  /** Updates the Activity used for registration, permissions and overlays. */
  @JvmStatic
  fun setActivity(activity: Activity) {
    Bridge.activity = activity
  }

  @JvmStatic
  fun startRegistration(activity: Activity) {
    Bridge.scope.launch { Wearables.startRegistration(activity) }
  }

  @JvmStatic
  fun startUnregistration(activity: Activity) {
    Bridge.scope.launch { Wearables.startUnregistration(activity) }
  }

  @JvmStatic
  fun openFirmwareUpdate(activity: Activity) {
    Bridge.scope.launch {
      Wearables.openFirmwareUpdate(activity).onFailure { e, _ ->
        Bridge.error("navigation", e.description, e.name)
      }
    }
  }

  @JvmStatic
  fun openGlassesAppUpdate(activity: Activity) {
    Bridge.scope.launch {
      Wearables.openDATGlassesAppUpdate(activity).onFailure { e, _ ->
        Bridge.error("navigation", e.description, e.name)
      }
    }
  }

  /** permission: "CAMERA" or "MICROPHONE". Result arrives in the "permission" event. */
  @JvmStatic
  fun checkPermission(permission: String) {
    Bridge.scope.launch {
      val p = parsePermission(permission) ?: return@launch
      emit("permission", json("permission" to p.name, "status" to Permissions.check(p)))
    }
  }

  /** Opens the Meta AI flow. Only call it from a user action. */
  @JvmStatic
  fun requestPermission(activity: Activity, permission: String) {
    Bridge.activity = activity
    Bridge.scope.launch {
      val p = parsePermission(permission) ?: return@launch
      emit("permission", json("permission" to p.name, "status" to Permissions.request(activity, p)))
    }
  }

  @JvmStatic fun isModuleAvailable(module: String): Boolean = Modules.isAvailable(module)

  @JvmStatic fun bridgeVersion(): String = BRIDGE_VERSION

  // ───────────── session ─────────────

  /**
   * selector: "DISPLAY" (first glasses with a display), "ANY" (any device)
   * or "SPECIFIC" (uses [deviceId]).
   */
  @JvmStatic
  fun startSession(selector: String, deviceId: String) {
    Bridge.scope.launch { MDSession.start(selector, deviceId) }
  }

  @JvmStatic
  fun stopSession() {
    Bridge.scope.launch { MDSession.stop() }
  }

  // ───────────── devices ─────────────

  private fun onDevicesChanged(ids: Set<DeviceIdentifier>, forceMetadata: Boolean = false) {
    (metadataJobs.keys - ids).forEach { removed ->
      metadataJobs.remove(removed)?.cancel()
      emit("device.removed", json("id" to removed.identifier))
    }
    ids.forEach { id ->
      if (forceMetadata) metadataJobs.remove(id)?.cancel()
      metadataJobs.getOrPut(id) {
        Bridge.scope.launch {
          Wearables.devicesMetadata[id]?.collect { device -> emit("device", deviceJson(id, device)) }
        }
      }
    }
    emit("devices", json("ids" to Bridge.jsonArray(ids.map { it.identifier })))
  }

  internal fun deviceJson(id: DeviceIdentifier?, d: Device): JSONObject =
      json(
          "id" to id?.identifier,
          "name" to d.name,
          "type" to d.deviceType.name,
          "linkState" to d.linkState.name,
          "compatibility" to d.compatibility.name,
          "firmware" to d.firmwareInfo,
          "battery" to d.batteryLevel,
          "charging" to d.chargingState.name,
          "don" to d.donState.name,
          "hinge" to d.hingeState.name,
          "thermal" to d.thermalLevel.name,
          "displayCapable" to d.isDisplayCapable(),
      )

  private fun parsePermission(name: String): Permission? {
    val p = runCatching { Permission.valueOf(name.uppercase()) }.getOrNull()
    if (p == null) Bridge.error("permission", "Unknown permission: $name", "INVALID_PERMISSION")
    return p
  }
}

/** A capability attached to the session (Display, Camera, Motion, Inputs, Speech). */
internal interface SessionCapability {
  /** Called when the session reaches STARTED: attach if C# requested the capability. */
  fun onSessionStarted(session: DeviceSession)

  /** Session stopped (terminal): cancel collectors and drop references. */
  fun onSessionStopped()
}

/** DeviceSession lifecycle. There is only one session at a time. */
internal object MDSession {
  var session: DeviceSession? = null
    private set

  private val jobs = mutableListOf<Job>()

  val capabilities: List<SessionCapability> by lazy {
    listOfNotNull(
        MDDisplay,
        if (Modules.isAvailable("camera")) MDCamera else null,
        if (Modules.isAvailable("motion")) MDMotion else null,
        if (Modules.isAvailable("inputs")) MDInputs else null,
        if (Modules.isAvailable("speech")) MDSpeech else null,
    )
  }

  /** Session ready to receive capabilities. */
  fun startedSession(): DeviceSession? = session?.takeIf { it.state.value == DeviceSessionState.STARTED }

  fun start(selector: String, deviceId: String) {
    session?.let {
      emit("session.state", json("state" to it.state.value.name))
      return
    }
    val deviceSelector =
        when (selector.uppercase()) {
          "SPECIFIC" -> SpecificDeviceSelector(DeviceIdentifier(deviceId))
          "ANY" -> AutoDeviceSelector()
          else -> AutoDeviceSelector(filter = { it.isDisplayCapable() })
        }
    Wearables.createSession(deviceSelector)
        .fold(
            onSuccess = { s -> observeAndStart(s) },
            onFailure = { e, _ ->
              emit("session.error", json("error" to e.name, "message" to e.description))
            },
        )
  }

  private fun observeAndStart(s: DeviceSession) {
    session = s
    jobs +=
        Bridge.scope.launch {
          s.errors.collect { e -> emit("session.error", json("error" to e.name, "message" to e.description)) }
        }
    jobs +=
        Bridge.scope.launch {
          s.state.collect { state ->
            emit("session.state", json("state" to state.name))
            when (state) {
              DeviceSessionState.STARTED -> capabilities.forEach { it.onSessionStarted(s) }
              DeviceSessionState.STOPPED -> clear() // terminal
              else -> Unit
            }
          }
        }
    jobs +=
        Bridge.scope.launch {
          s.deviceInfo.collect { d -> emit("session.device", MDCore.deviceJson(null, d)) }
        }
    s.start() // returns immediately; progress arrives via state/errors
  }

  suspend fun stop() {
    val s = session ?: return
    capabilities.forEach { it.onSessionStopped() }
    withContext(Dispatchers.IO) { s.stop() }
  }

  private fun clear() {
    capabilities.forEach { it.onSessionStopped() }
    jobs.forEach { it.cancel() }
    jobs.clear()
    session = null
  }
}

/** Finds out which DAT modules were included in the Unity build. */
internal object Modules {
  private val classes =
      mapOf(
          "core" to "com.meta.wearable.dat.core.Wearables",
          "display" to "com.meta.wearable.dat.display.Display",
          "camera" to "com.meta.wearable.dat.camera.Camera",
          "inputs" to "com.meta.wearable.dat.inputs.Inputs",
          "motion" to "com.meta.wearable.dat.motion.Motion",
          "speech" to "com.meta.wearable.dat.speech.Speech",
          "mockdevice" to "com.meta.wearable.dat.mockdevice.MockDeviceKit",
      )

  private val cache = mutableMapOf<String, Boolean>()

  @Synchronized
  fun isAvailable(module: String): Boolean =
      cache.getOrPut(module.lowercase()) {
        val cls = classes[module.lowercase()] ?: return@getOrPut false
        runCatching { Class.forName(cls) }.isSuccess
      }

  fun available(): List<String> = classes.keys.filter { isAvailable(it) }
}
