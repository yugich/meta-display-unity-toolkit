package com.estudos.metadisplay

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
 * Implementada no C# (AndroidJavaProxy). Todos os eventos chegam aqui como (tipo, JSON).
 * É chamada a partir de threads do Android, nunca da main thread do Unity.
 */
interface MetaDisplayListener {
  fun onEvent(type: String, payload: String)
}

/** Estado compartilhado e utilitários da ponte. Não é chamado diretamente pelo C#. */
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
    Log.e(TAG, "Erro inesperado na ponte", t)
    error("bridge", t.message ?: t.toString())
  }

  /** Todo o estado da ponte é manipulado na main thread do Android. */
  val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main + crashGuard)

  fun emit(type: String, payload: JSONObject = JSONObject()) {
    val l = listener ?: return
    try {
      l.onEvent(type, payload.toString())
    } catch (t: Throwable) {
      Log.e(TAG, "Falha ao entregar evento '$type' ao Unity", t)
    }
  }

  fun error(source: String, message: String, code: String? = null) {
    Log.w(TAG, "[$source] $code $message")
    emit("error", json("source" to source, "code" to code, "message" to message))
  }

  fun json(vararg pairs: Pair<String, Any?>): JSONObject =
      JSONObject().apply { pairs.forEach { (k, v) -> put(k, v ?: JSONObject.NULL) } }

  fun jsonArray(items: Collection<Any?>): JSONArray = JSONArray().apply { items.forEach { put(it) } }

  /** Nome estável de um erro do SDK (enum ou classe selada). */
  fun errorName(e: Any?): String =
      when (e) {
        null -> "UNKNOWN"
        is Enum<*> -> e.name
        else -> e::class.java.simpleName
      }
}

/** Pedidos de permissão do DAT (CAMERA, MICROPHONE) passam pelo app Meta AI. */
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

  /** Último status conhecido (GRANTED, DENIED, ERROR:...) — útil nas mensagens de erro. */
  fun lastStatus(permission: Permission): String = lastStatuses[permission] ?: "desconhecido"

  suspend fun check(permission: Permission): String {
    val status =
        Wearables.checkPermissionStatus(permission)
            .fold(onSuccess = { statusName(it) }, onFailure = { e, _ -> "ERROR:" + Bridge.errorName(e) })
    lastStatuses[permission] = status
    Log.i(Bridge.TAG, "[permissão] ${permission.name} consulta: $status")
    return status
  }

  /** Abre a [PermissionRequestActivity] e espera o resultado ("GRANTED", "DENIED" ou "ERROR:..."). */
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
   * Confere e, se preciso, pede a permissão. Retorna true quando está concedida.
   * Obs.: se o usuário escolheu "Permitir uma vez" no Meta AI, o próximo uso pede de novo.
   */
  suspend fun ensure(permission: Permission): Boolean {
    if (check(permission) == "GRANTED") return true
    val activity = Bridge.activity
    if (activity == null) {
      Log.w(Bridge.TAG, "[permissão] ${permission.name}: sem Activity para abrir o Meta AI")
      return false
    }
    Log.i(Bridge.TAG, "[permissão] ${permission.name}: pedindo no Meta AI")
    val result = request(activity, permission)
    lastStatuses[permission] = result
    Log.i(Bridge.TAG, "[permissão] ${permission.name} pedido: $result")
    Bridge.emit("permission", Bridge.json("permission" to permission.name, "status" to result))
    return result == "GRANTED"
  }

  internal fun complete(id: Int, result: String) {
    pending[id]?.complete(result)
  }
}

/**
 * Activity transparente que usa o Activity Result API com o contrato oficial
 * [Wearables.RequestPermissionContract]. Assim o Unity não precisa de uma Activity própria.
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
