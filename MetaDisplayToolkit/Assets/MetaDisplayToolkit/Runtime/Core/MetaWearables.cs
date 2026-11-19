using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Ponto de entrada: inicialização, registro no Meta AI, dispositivos, permissões e sessão.
    /// Todos os eventos são disparados na main thread do Unity.
    /// </summary>
    public static class MetaWearables
    {
        static readonly Dictionary<string, DeviceInfo> s_Devices = new Dictionary<string, DeviceInfo>();
        static readonly Dictionary<MetaPermission, PermissionResult> s_Permissions = new Dictionary<MetaPermission, PermissionResult>();
        static string[] s_Modules = Array.Empty<string>();

        public static bool IsInitialized { get; private set; }
        /// <summary>Rodando no aparelho com a ponte nativa (false no Editor).</summary>
        public static bool IsNative => MetaNative.IsNative;
        public static bool IsDevMode { get; private set; }
        public static string SdkVersion { get; private set; } = "1.0.1";
        public static RegistrationState Registration { get; private set; } = RegistrationState.Unknown;
        public static bool IsRegistered => Registration == RegistrationState.Registered;
        public static SessionState Session { get; private set; } = SessionState.None;
        public static bool IsSessionStarted => Session == SessionState.Started;
        /// <summary>Óculos da sessão atual (atualizado ao vivo: bateria, temperatura...).</summary>
        public static DeviceInfo SessionDevice { get; private set; }
        public static IReadOnlyDictionary<string, DeviceInfo> Devices => s_Devices;
        /// <summary>A sessão falhou pedindo atualização do app DAT nos óculos.</summary>
        public static bool GlassesAppUpdateRequired { get; private set; }
        public static IReadOnlyList<string> AvailableModules => s_Modules;

        public static event Action Initialized;
        public static event Action<RegistrationState> RegistrationChanged;
        public static event Action<DeviceInfo> DeviceChanged;
        public static event Action<string> DeviceRemoved;
        public static event Action<SessionState> SessionStateChanged;
        public static event Action<DeviceInfo> SessionDeviceChanged;
        public static event Action<MetaPermission, PermissionResult> PermissionChanged;
        /// <summary>Todos os erros (sessão, display, câmera, ...).</summary>
        public static event Action<MetaError> Error;

        // ───────────── inicialização e registro ─────────────

        /// <summary>Inicializa o SDK. Seguro chamar mais de uma vez. Pede BLUETOOTH_CONNECT no Android 12+.</summary>
        public static void Initialize()
        {
            MetaDispatcher.EnsureExists();
            if (MetaNative.IsNative)
                MetaAndroidPermissions.RequestBluetooth(granted =>
                {
                    if (granted) MetaNative.Call("MDCore", "initialize", MetaNative.Activity, MetaNative.CreateListener());
                    else RaiseError(new MetaError("permission", "BLUETOOTH_DENIED", "Permissão de Bluetooth (Dispositivos próximos) negada."));
                });
            else
                MetaNative.Call("MDCore", "initialize", null, null);
        }

        /// <summary>Abre o app Meta AI para o usuário aprovar este app (uma vez só).</summary>
        public static void StartRegistration() => MetaNative.Call("MDCore", "startRegistration", MetaNative.Activity);

        public static void StartUnregistration() => MetaNative.Call("MDCore", "startUnregistration", MetaNative.Activity);

        public static void OpenFirmwareUpdate() => MetaNative.Call("MDCore", "openFirmwareUpdate", MetaNative.Activity);

        /// <summary>Atualiza o app DAT instalado nos óculos (quando a sessão pede).</summary>
        public static void OpenGlassesAppUpdate() => MetaNative.Call("MDCore", "openGlassesAppUpdate", MetaNative.Activity);

        public static bool HasModule(string module) => Array.IndexOf(s_Modules, module) >= 0;

        // ───────────── permissões do DAT ─────────────

        public static PermissionResult GetPermission(MetaPermission permission) =>
            s_Permissions.TryGetValue(permission, out var r) ? r : PermissionResult.Unknown;

        /// <summary>Consulta a permissão (resultado em <see cref="PermissionChanged"/>).</summary>
        public static void CheckPermission(MetaPermission permission) =>
            MetaNative.Call("MDCore", "checkPermission", MetaEnum.ToSdk(permission));

        /// <summary>Abre o fluxo de permissão do Meta AI. Chame a partir de uma ação do usuário.</summary>
        public static void RequestPermission(MetaPermission permission) =>
            MetaNative.Call("MDCore", "requestPermission", MetaNative.Activity, MetaEnum.ToSdk(permission));

        // ───────────── sessão ─────────────

        /// <summary>Conecta aos óculos. As capacidades pedidas (Display, Câmera...) são anexadas quando a sessão começa.</summary>
        public static void StartSession(DeviceSelection selection = DeviceSelection.DisplayCapable, string deviceId = "")
        {
            GlassesAppUpdateRequired = false;
            MetaNative.Call("MDCore", "startSession", MetaEnum.ToSdk(selection) == "DISPLAY_CAPABLE" ? "DISPLAY" : MetaEnum.ToSdk(selection), deviceId ?? "");
        }

        public static void StopSession() => MetaNative.Call("MDCore", "stopSession");

        // ───────────── eventos da ponte ─────────────

        internal static void RaiseError(MetaError error)
        {
            Debug.LogWarning($"[MetaDisplay] {error}");
            Error?.Invoke(error);
        }

        internal static void Handle(string type, MetaPayload p, string json)
        {
            switch (type)
            {
                case "initialized":
                    IsInitialized = true;
                    IsDevMode = p.devMode;
                    if (!string.IsNullOrEmpty(p.sdkVersion)) SdkVersion = p.sdkVersion;
                    s_Modules = p.modules ?? Array.Empty<string>();
                    Initialized?.Invoke();
                    break;

                case "registration":
                    Registration = MetaEnum.Parse(p.state, RegistrationState.Unknown);
                    RegistrationChanged?.Invoke(Registration);
                    break;

                case "devices":
                    if (p.ids != null)
                    {
                        var keep = new HashSet<string>(p.ids);
                        var removed = new List<string>();
                        foreach (var id in s_Devices.Keys) if (!keep.Contains(id)) removed.Add(id);
                        foreach (var id in removed)
                        {
                            s_Devices.Remove(id);
                            DeviceRemoved?.Invoke(id);
                        }
                    }
                    break;

                case "device":
                {
                    var device = JsonUtility.FromJson<DeviceInfo>(json);
                    if (!string.IsNullOrEmpty(device.id)) s_Devices[device.id] = device;
                    DeviceChanged?.Invoke(device);
                    break;
                }

                case "device.removed":
                    if (p.id != null && s_Devices.Remove(p.id)) DeviceRemoved?.Invoke(p.id);
                    break;

                case "permission":
                {
                    var permission = MetaEnum.Parse(p.permission, MetaPermission.Camera);
                    var status = p.status != null && p.status.StartsWith("ERROR")
                        ? PermissionResult.Error
                        : MetaEnum.Parse(p.status, PermissionResult.Unknown);
                    s_Permissions[permission] = status;
                    PermissionChanged?.Invoke(permission, status);
                    break;
                }

                case "session.state":
                    Session = MetaEnum.Parse(p.state, SessionState.None);
                    if (Session == SessionState.Stopped) SessionDevice = null;
                    SessionStateChanged?.Invoke(Session);
                    break;

                case "session.device":
                    SessionDevice = JsonUtility.FromJson<DeviceInfo>(json);
                    SessionDeviceChanged?.Invoke(SessionDevice);
                    break;

                case "session.error":
                    if (p.error == "DAT_APP_ON_THE_GLASSES_UPDATE_REQUIRED") GlassesAppUpdateRequired = true;
                    RaiseError(new MetaError("session", p.error, p.message));
                    break;

                case "error":
                    RaiseError(new MetaError(p.source, p.code, p.message));
                    break;
            }
        }
    }

    /// <summary>Todos os campos possíveis dos eventos da ponte (o JsonUtility ignora os ausentes).</summary>
    [Serializable]
    internal class MetaPayload
    {
        public string state, error, code, message, source, details, permission, status, id, model, url, adb;
        public string text, locale, kind, direction, button, pressType, action, type, sdkVersion, bridgeVersion;
        public int requestId, port, bytes;
        public bool isFinal, enabled, delivered, highQuality, devMode;
        public float confidence, fraction, x, y, dx, dy;
        public long timestampMs;
        public string[] ids, modules;
    }

    /// <summary>Roteia cada evento da ponte para a classe responsável.</summary>
    internal static class MetaEvents
    {
        internal static void RaiseError(MetaError error) => MetaWearables.RaiseError(error);

        internal static void Route(string type, string json)
        {
            if (type == "__bt")
            {
                MetaAndroidPermissions.OnResult(json);
                return;
            }
            var p = string.IsNullOrEmpty(json) ? new MetaPayload() : JsonUtility.FromJson<MetaPayload>(json) ?? new MetaPayload();
            int dot = type.IndexOf('.');
            string area = dot > 0 ? type.Substring(0, dot) : type;
            switch (area)
            {
                case "display":
                case "video":
                    MetaGlassesDisplay.Handle(type, p);
                    break;
                case "camera":
                    MetaCamera.Handle(type, p);
                    break;
                case "motion":
                    MetaMotion.Handle(type, p);
                    break;
                case "input":
                case "inputs":
                    MetaInputs.Handle(type, p);
                    break;
                case "speech":
                    MetaSpeech.Handle(type, p);
                    break;
                case "voice":
                    MetaVoiceInvocations.Handle(type, p);
                    break;
                case "mock":
                    MetaMockDevice.Handle(type, p);
                    break;
                default:
                    MetaWearables.Handle(type, p, json);
                    break;
            }
        }
    }

    /// <summary>Permissão Android de Bluetooth (Dispositivos próximos), exigida no Android 12+.</summary>
    internal static class MetaAndroidPermissions
    {
        const string BluetoothConnect = "android.permission.BLUETOOTH_CONNECT";

        internal static void RequestBluetooth(Action<bool> done)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            int sdk;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION")) sdk = version.GetStatic<int>("SDK_INT");
            if (sdk < 31 || UnityEngine.Android.Permission.HasUserAuthorizedPermission(BluetoothConnect))
            {
                done(true);
                return;
            }
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            // Os callbacks chegam fora da main thread: devolve pela fila do dispatcher.
            callbacks.PermissionGranted += _ => MetaDispatcher.Enqueue("__bt", "granted");
            callbacks.PermissionDenied += _ => MetaDispatcher.Enqueue("__bt", "denied");
            s_Pending = done;
            UnityEngine.Android.Permission.RequestUserPermission(BluetoothConnect, callbacks);
#else
            done(true);
#endif
        }

        static Action<bool> s_Pending;

        internal static void OnResult(string result)
        {
            var cb = s_Pending;
            s_Pending = null;
            cb?.Invoke(result == "granted");
        }
    }
}
