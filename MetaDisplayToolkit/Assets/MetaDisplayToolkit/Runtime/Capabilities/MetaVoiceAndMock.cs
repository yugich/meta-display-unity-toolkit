using System;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// "Hey Meta, start {app name}" opens the app or brings it to the foreground (experimental).
    /// The spoken name and the approval are configured in the Wearables Developer Center.
    /// </summary>
    public static class MetaVoiceInvocations
    {
        const string Cls = "MDVoice";

        public static VoiceInvocationState State { get; private set; } = VoiceInvocationState.Stopped;
        /// <summary>Automatically answers success to every invocation (default: true).</summary>
        public static bool AutoAcknowledge = true;
        /// <summary>The app was opened by a voice command.</summary>
        public static bool LaunchedByVoice { get; private set; }

        public static event Action<VoiceInvocationState> StateChanged;
        /// <summary>"Open app" request. If AutoAcknowledge = false, call <see cref="Respond"/> with the id.</summary>
        public static event Action<int> LaunchAppRequested;
        /// <summary>The app was opened/brought to the foreground by voice (voice invocation intent).</summary>
        public static event Action LaunchedFromVoiceIntent;

        public static void Start()
        {
            MetaDispatcher.EnsureExists();
            MetaNative.Call(Cls, "start");
        }

        public static void Stop() => MetaNative.Call(Cls, "stop");

        /// <summary>Every invocation must be answered exactly once.</summary>
        public static void Respond(int requestId, bool success, string message = "") =>
            MetaNative.Call(Cls, "respond", requestId, success, message ?? "");

        internal static void CheckLaunchIntent()
        {
            if (!MetaNative.IsNative) return;
            if (MetaNative.Call<bool>(Cls, "isVoiceLaunch", MetaNative.Activity) && !LaunchedByVoice)
                Handle("voice.launchIntent", new MetaPayload());
        }

        internal static void Handle(string type, MetaPayload p)
        {
            switch (type)
            {
                case "voice.state":
                    State = MetaEnum.Parse(p.state, VoiceInvocationState.Stopped);
                    StateChanged?.Invoke(State);
                    break;
                case "voice.error":
                    MetaWearables.RaiseError(new MetaError("voice", p.error, p.message));
                    break;
                case "voice.launchApp":
                    LaunchAppRequested?.Invoke(p.requestId);
                    if (AutoAcknowledge) Respond(p.requestId, true);
                    break;
                case "voice.launchIntent":
                    LaunchedByVoice = true;
                    LaunchedFromVoiceIntent?.Invoke();
                    break;
            }
        }
    }

    public enum MockGlassesModel { MetaRaybanDisplay, RaybanMeta, OakleyMetaHstn, OakleyMetaVanguard, RaybanMetaOptics, MetaGlasses }

    public enum MockGlassesAction { PowerOn, PowerOff, Don, Doff, Fold, Unfold }

    public enum MockInput { NavUp, NavDown, NavLeft, NavRight, Select, Back, Button, CaptureShort, CaptureHold, CaptureDouble, Tap, TapHold }

    public enum ScreenCorner { TopRight, TopLeft, BottomRight, BottomLeft, Center }

    /// <summary>
    /// Simulated glasses (MockDeviceKit) to test on the phone without hardware. Includes a preview of the
    /// glasses display drawn on top of Unity, plus input, speech and voice injection.
    /// </summary>
    public static class MetaMockDevice
    {
        const string Cls = "MDMock";

        public static bool IsEnabled { get; private set; }
        public static string PairedId { get; private set; }
        public static string TestServerUrl { get; private set; }

        public static event Action<string> Paired;
        /// <summary>(url, adb forward command)</summary>
        public static event Action<string, string> TestServerStarted;

        /// <summary>Turns the simulated environment on. Call it before (or right after) MetaWearables.Initialize.</summary>
        public static void Enable(bool initiallyRegistered = true, bool permissionsGranted = true) =>
            MetaNative.Call(Cls, "enable", initiallyRegistered, permissionsGranted);

        public static void Disable() => MetaNative.Call(Cls, "disable");

        /// <summary>Creates simulated glasses (default: Meta Ray-Ban Display, powered on and worn).</summary>
        public static void PairGlasses(MockGlassesModel model = MockGlassesModel.MetaRaybanDisplay, bool powerOnAndDon = true) =>
            MetaNative.Call(Cls, "pairGlasses", MetaEnum.ToSdk(model), powerOnAndDon);

        public static void UnpairAll() => MetaNative.Call(Cls, "unpairAll");

        public static void Glasses(MockGlassesAction action) => MetaNative.Call(Cls, "glassesAction", MetaEnum.ToSdk(action));

        /// <summary>battery -1 = do not change.</summary>
        public static void SetDeviceState(int battery = -1, ChargingState charging = ChargingState.Unknown, ThermalLevel thermal = ThermalLevel.Unknown) =>
            MetaNative.Call(Cls, "setDeviceState", battery, charging == ChargingState.Unknown ? "" : MetaEnum.ToSdk(charging),
                thermal == ThermalLevel.Unknown ? "" : MetaEnum.ToSdk(thermal));

        public static void SimulateRegistration(bool success) => MetaNative.Call(Cls, "simulateRegistration", success);

        public static void SetPermission(MetaPermission permission, bool granted, bool asRequestResult = false) =>
            MetaNative.Call(Cls, "setPermission", MetaEnum.ToSdk(permission), granted ? "GRANTED" : "DENIED", asRequestResult);

        /// <summary>H.264/H.265 video on the device (absolute path) as the simulated camera feed.</summary>
        public static void SetCameraFeedFile(string absolutePath) => MetaNative.Call(Cls, "setCameraFeedFile", absolutePath);

        /// <summary>Uses the phone camera as the feed (the app needs the android.permission.CAMERA permission).</summary>
        public static void SetCameraFeedPhoneCamera(bool frontCamera = false) =>
            MetaNative.Call(Cls, "setCameraFeedPhoneCamera", frontCamera ? "FRONT" : "BACK");

        public static void SetCapturedImageFile(string absolutePath) => MetaNative.Call(Cls, "setCapturedImageFile", absolutePath);

        public static void InjectInput(MockInput input) => MetaNative.Call(Cls, "injectInput", MetaEnum.ToSdk(input));

        public static void InjectDrag(DragAction action, Vector2 position, Vector2 delta) =>
            MetaNative.Call(Cls, "injectDrag", MetaEnum.ToSdk(action), position.x, position.y, delta.x, delta.y);

        public static void SimulateTranscription(string text, bool isFinal = true, float confidence = 0.9f) =>
            MetaNative.Call(Cls, "simulateTranscription", text, isFinal, confidence);

        public static void SimulateVoiceLaunch() => MetaNative.Call(Cls, "simulateVoiceLaunch");

        /// <summary>Draws the simulated display on top of Unity (a real view of what the glasses would show).</summary>
        public static void ShowDisplayPreview(int sizePx = 600, ScreenCorner corner = ScreenCorner.TopRight) =>
            MetaNative.Call(Cls, "showDisplayPreview", MetaNative.Activity, sizePx, MetaEnum.ToSdk(corner));

        public static void HideDisplayPreview() => MetaNative.Call(Cls, "hideDisplayPreview", MetaNative.Activity);

        /// <summary>Preview in the PC's Chrome: run the adb command from the event and open the URL.</summary>
        public static void StartTestServer(int port = 9000) => MetaNative.Call(Cls, "startTestServer", port);

        public static void StopTestServer() => MetaNative.Call(Cls, "stopTestServer");

        internal static void Handle(string type, MetaPayload p)
        {
            switch (type)
            {
                case "mock.state":
                    IsEnabled = p.enabled;
                    break;
                case "mock.paired":
                    PairedId = p.id;
                    Paired?.Invoke(p.id);
                    break;
                case "mock.testServer":
                    TestServerUrl = p.url;
                    Debug.Log($"[MetaDisplay] PC preview: run '{p.adb}' and open {p.url}");
                    TestServerStarted?.Invoke(p.url, p.adb);
                    break;
            }
        }
    }
}
