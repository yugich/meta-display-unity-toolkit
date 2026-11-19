using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Entry point: initialization, Meta AI registration, devices, permissions and session.
    /// Every event is raised on the Unity main thread.
    /// </summary>
    public static class MetaWearables
    {
        static readonly Dictionary<string, DeviceInfo> s_Devices = new Dictionary<string, DeviceInfo>();
        static readonly Dictionary<MetaPermission, PermissionResult> s_Permissions = new Dictionary<MetaPermission, PermissionResult>();
        static string[] s_Modules = Array.Empty<string>();

        public static bool IsInitialized { get; private set; }
        /// <summary>Running on the device with the native bridge (false in the Editor).</summary>
        public static bool IsNative => MetaNative.IsNative;
        public static bool IsDevMode { get; private set; }
        public static string SdkVersion { get; private set; } = "1.0.1";
        public static RegistrationState Registration { get; private set; } = RegistrationState.Unknown;
        public static bool IsRegistered => Registration == RegistrationState.Registered;
        public static SessionState Session { get; private set; } = SessionState.None;
        public static bool IsSessionStarted => Session == SessionState.Started;
        /// <summary>Glasses of the current session (updated live: battery, temperature...).</summary>
        public static DeviceInfo SessionDevice { get; private set; }
        public static IReadOnlyDictionary<string, DeviceInfo> Devices => s_Devices;
        /// <summary>The session failed asking for an update of the DAT app on the glasses.</summary>
        public static bool GlassesAppUpdateRequired { get; private set; }
        public static IReadOnlyList<string> AvailableModules => s_Modules;

        public static event Action Initialized;
        public static event Action<RegistrationState> RegistrationChanged;
        public static event Action<DeviceInfo> DeviceChanged;
        public static event Action<string> DeviceRemoved;
        public static event Action<SessionState> SessionStateChanged;
        public static event Action<DeviceInfo> SessionDeviceChanged;
        public static event Action<MetaPermission, PermissionResult> PermissionChanged;
        /// <summary>All errors (session, display, camera, ...).</summary>
        public static event Action<MetaError> Error;

        // ───────────── initialization and registration ─────────────

        /// <summary>Initializes the SDK. Safe to call more than once. Requests BLUETOOTH_CONNECT on Android 12+.</summary>
        public static void Initialize()
        {
            MetaDispatcher.EnsureExists();
            if (MetaNative.IsNative)
                MetaAndroidPermissions.RequestBluetooth(granted =>
                {
                    if (granted) MetaNative.Call("MDCore", "initialize", MetaNative.Activity, MetaNative.CreateListener());
                    else RaiseError(new MetaError("permission", "BLUETOOTH_DENIED", "Bluetooth (Nearby devices) permission denied."));
                });
            else
                MetaNative.Call("MDCore", "initialize", null, null);
        }

        /// <summary>Opens the Meta AI app so the user approves this app (only once).</summary>
        public static void StartRegistration() => MetaNative.Call("MDCore", "startRegistration", MetaNative.Activity);

        public static void StartUnregistration() => MetaNative.Call("MDCore", "startUnregistration", MetaNative.Activity);

        public static void OpenFirmwareUpdate() => MetaNative.Call("MDCore", "openFirmwareUpdate", MetaNative.Activity);

        /// <summary>Updates the DAT app installed on the glasses (when the session asks for it).</summary>
        public static void OpenGlassesAppUpdate() => MetaNative.Call("MDCore", "openGlassesAppUpdate", MetaNative.Activity);

        public static bool HasModule(string module) => Array.IndexOf(s_Modules, module) >= 0;

        // ───────────── DAT permissions ─────────────

        public static PermissionResult GetPermission(MetaPermission permission) =>
            s_Permissions.TryGetValue(permission, out var r) ? r : PermissionResult.Unknown;

        /// <summary>Checks the permission (result in <see cref="PermissionChanged"/>).</summary>
        public static void CheckPermission(MetaPermission permission) =>
            MetaNative.Call("MDCore", "checkPermission", MetaEnum.ToSdk(permission));

        /// <summary>Opens the Meta AI permission flow. Call it from a user action.</summary>
        public static void RequestPermission(MetaPermission permission) =>
            MetaNative.Call("MDCore", "requestPermission", MetaNative.Activity, MetaEnum.ToSdk(permission));

        // ───────────── session ─────────────

        /// <summary>Connects to the glasses. Requested capabilities (Display, Camera...) are attached when the session starts.</summary>
        public static void StartSession(DeviceSelection selection = DeviceSelection.DisplayCapable, string deviceId = "")
        {
            GlassesAppUpdateRequired = false;
            MetaNative.Call("MDCore", "startSession", MetaEnum.ToSdk(selection) == "DISPLAY_CAPABLE" ? "DISPLAY" : MetaEnum.ToSdk(selection), deviceId ?? "");
        }

        public static void StopSession() => MetaNative.Call("MDCore", "stopSession");

        // ───────────── bridge events ─────────────

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

    /// <summary>Every possible field of the bridge events (JsonUtility ignores missing ones).</summary>
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

    /// <summary>Routes each bridge event to the class responsible for it.</summary>
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

    /// <summary>Android Bluetooth (Nearby devices) permission, required on Android 12+.</summary>
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
            // Callbacks arrive off the main thread: hand them back through the dispatcher queue.
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
