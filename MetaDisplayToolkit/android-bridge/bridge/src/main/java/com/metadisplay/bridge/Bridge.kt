package com.metadisplay.bridge

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.util.Log
import androidx.activity.ComponentActivity
import com.meta.wearable.dat.core.Wearables
import com.meta.wearable.dat.core.types.Permission
import com.meta.wearable.dat.core.types.PermissionStatus
import java.lang.ref.WeakReference
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicInteger
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineExceptionHandler
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import org.json.JSONArray
import org.json.JSONObject

/**
 * Implemented in C# (AndroidJavaProxy). Every event arrives here as (type, JSON).
 * Called from Android threads, never from the Unity main thread.
 */
interface MetaDisplayListener {
  fun onEvent(type: String, payload: String)
}

/** Shared state and helpers of the bridge. Not called directly from C#. */
internal object Bridge {
  const val TAG = "MetaDisplayUnity"

  @Volatile var listener: MetaDisplayListener? = null
  @Volatile private var activityRef: WeakReference<Activity>? = null
  lateinit var appContext: Context

  val isReady: Boolean
    get() = ::appContext.isInitialized

  var activity: Activity?
    get() = activityRef?.get()
    set(value) {
      activityRef = value?.let { WeakReference(it) }
    }

  private val crashGuard = CoroutineExceptionHandler { _, t ->
    Log.e(TAG, "Unexpected error in the bridge", t)
    error("bridge", t.message ?: t.toString())
  }

  /** All bridge state is handled on the Android main thread. */
  val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main + crashGuard)

  fun emit(type: String, payload: JSONObject = JSONObject()) {
    val l = listener ?: return
    try {
      l.onEvent(type, payload.toString())
    } catch (t: Throwable) {
      Log.e(TAG, "Failed to deliver event '$type' to Unity", t)
    }
  }

  fun error(source: String, message: String, code: String? = null) {
    Log.w(TAG, "[$source] $code $message")
    emit("error", json("source" to source, "code" to code, "message" to message))
  }

  fun json(vararg pairs: Pair<String, Any?>): JSONObject =
      JSONObject().apply { pairs.forEach { (k, v) -> put(k, v ?: JSONObject.NULL) } }

  fun jsonArray(items: Collection<Any?>): JSONArray = JSONArray().apply { items.forEach { put(it) } }

  /** Stable name of an SDK error (enum or sealed class). */
  fun errorName(e: Any?): String =
      when (e) {
        null -> "UNKNOWN"
        is Enum<*> -> e.name
        else -> e::class.java.simpleName
      }
}

/** DAT permission requests (CAMERA, MICROPHONE) go through the Meta AI app. */
internal object Permissions {
  private val pending = ConcurrentHashMap<Int, CompletableDeferred<String>>()
  private val nextId = AtomicInteger(1)

  fun statusName(status: PermissionStatus): String =
      when (status) {
        is PermissionStatus.Granted -> "GRANTED"
        is PermissionStatus.Denied -> "DENIED"
        else -> status.toString().uppercase()
      }

  private val lastStatuses = ConcurrentHashMap<Permission, String>()

  /** Last known status (GRANTED, DENIED, ERROR:...), useful in error messages. */
  fun lastStatus(permission: Permission): String = lastStatuses[permission] ?: "unknown"

  suspend fun check(permission: Permission): String {
    val status =
        Wearables.checkPermissionStatus(permission)
            .fold(onSuccess = { statusName(it) }, onFailure = { e, _ -> "ERROR:" + Bridge.errorName(e) })
    lastStatuses[permission] = status
    Log.i(Bridge.TAG, "[permission] ${permission.name} check: $status")
    return status
  }

  /** Opens [PermissionRequestActivity] and waits for the result ("GRANTED", "DENIED" or "ERROR:..."). */
  suspend fun request(activity: Activity, permission: Permission): String {
    val id = nextId.getAndIncrement()
    val deferred = CompletableDeferred<String>()
    pending[id] = deferred
    activity.startActivity(
        Intent(activity, PermissionRequestActivity::class.java)
            .putExtra(PermissionRequestActivity.EXTRA_PERMISSION, permission.name)
            .putExtra(PermissionRequestActivity.EXTRA_REQUEST_ID, id),
    )
    return try {
      deferred.await()
    } finally {
      pending.remove(id)
    }
  }

  /**
   * Checks and, if needed, requests the permission. Returns true when it is granted.
   * Note: if the user picked "Allow once" in Meta AI, the next use asks again.
   */
  suspend fun ensure(permission: Permission): Boolean {
    if (check(permission) == "GRANTED") return true
    val activity = Bridge.activity
    if (activity == null) {
      Log.w(Bridge.TAG, "[permission] ${permission.name}: no Activity to open Meta AI")
      return false
    }
    Log.i(Bridge.TAG, "[permission] ${permission.name}: requesting in Meta AI")
    val result = request(activity, permission)
    lastStatuses[permission] = result
    Log.i(Bridge.TAG, "[permission] ${permission.name} request: $result")
    Bridge.emit("permission", Bridge.json("permission" to permission.name, "status" to result))
    return result == "GRANTED"
  }

  internal fun complete(id: Int, result: String) {
    pending[id]?.complete(result)
  }
}

/**
 * Transparent Activity that uses the Activity Result API with the official
 * [Wearables.RequestPermissionContract]. This way Unity does not need its own Activity.
 */
class PermissionRequestActivity : ComponentActivity() {
  companion object {
    const val EXTRA_PERMISSION = "metadisplay.permission"
    const val EXTRA_REQUEST_ID = "metadisplay.requestId"
  }

  private var requestId = 0
  private var permissionName = ""

  private val launcher =
      registerForActivityResult(Wearables.RequestPermissionContract()) { result ->
        val status =
            result.fold(
                onSuccess = { Permissions.statusName(it) },
                onFailure = { e, _ -> "ERROR:" + Bridge.errorName(e) },
            )
        Permissions.complete(requestId, status)
        finish()
      }

  override fun onCreate(savedInstanceState: Bundle?) {
    super.onCreate(savedInstanceState)
    requestId = intent.getIntExtra(EXTRA_REQUEST_ID, 0)
    permissionName = intent.getStringExtra(EXTRA_PERMISSION) ?: ""
    if (savedInstanceState != null) return
    val permission = runCatching { Permission.valueOf(permissionName) }.getOrNull()
    if (permission == null) {
      Permissions.complete(requestId, "ERROR:INVALID_PERMISSION")
      finish()
      return
    }
    launcher.launch(permission)
  }
}
