using System;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// "Hey Meta, start {nome do app}" abre ou traz o app para frente (experimental).
    /// O nome falado e a aprovação são configurados no Wearables Developer Center.
    /// </summary>
    public static class MetaVoiceInvocations
    {
        const string Cls = "MDVoice";

        public static VoiceInvocationState State { get; private set; } = VoiceInvocationState.Stopped;
        /// <summary>Responde sucesso automaticamente a cada invocação (padrão: true).</summary>
        public static bool AutoAcknowledge = true;
        /// <summary>O app foi aberto por um comando de voz.</summary>
        public static bool LaunchedByVoice { get; private set; }

        public static event Action<VoiceInvocationState> StateChanged;
        /// <summary>Pedido "abrir app". Se AutoAcknowledge = false, chame <see cref="Respond"/> com o id.</summary>
        public static event Action<int> LaunchAppRequested;
        /// <summary>O app foi aberto/trazido para frente por voz (intent de voice invocation).</summary>
        public static event Action LaunchedFromVoiceIntent;

        public static void Start()
        {
            MetaDispatcher.EnsureExists();
            MetaNative.Call(Cls, "start");
        }

        public static void Stop() => MetaNative.Call(Cls, "stop");

        /// <summary>Toda invocação deve ser respondida exatamente uma vez.</summary>
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
    /// Óculos simulados (MockDeviceKit) para testar no celular sem hardware. Inclui um preview do
    /// display dos óculos desenhado por cima do Unity e injeção de inputs, fala e voz.
    /// </summary>
    public static class MetaMockDevice
    {
        const string Cls = "MDMock";

        public static bool IsEnabled { get; private set; }
        public static string PairedId { get; private set; }
        public static string TestServerUrl { get; private set; }

        public static event Action<string> Paired;
        /// <summary>(url, comando adb forward)</summary>
        public static event Action<string, string> TestServerStarted;

        /// <summary>Liga o ambiente simulado. Chame antes (ou logo depois) de MetaWearables.Initialize.</summary>
        public static void Enable(bool initiallyRegistered = true, bool permissionsGranted = true) =>
            MetaNative.Call(Cls, "enable", initiallyRegistered, permissionsGranted);

        public static void Disable() => MetaNative.Call(Cls, "disable");

        /// <summary>Cria óculos simulados (padrão: Meta Ray-Ban Display ligado e no rosto).</summary>
        public static void PairGlasses(MockGlassesModel model = MockGlassesModel.MetaRaybanDisplay, bool powerOnAndDon = true) =>
            MetaNative.Call(Cls, "pairGlasses", MetaEnum.ToSdk(model), powerOnAndDon);

        public static void UnpairAll() => MetaNative.Call(Cls, "unpairAll");

        public static void Glasses(MockGlassesAction action) => MetaNative.Call(Cls, "glassesAction", MetaEnum.ToSdk(action));

        /// <summary>battery -1 = não mudar.</summary>
        public static void SetDeviceState(int battery = -1, ChargingState charging = ChargingState.Unknown, ThermalLevel thermal = ThermalLevel.Unknown) =>
            MetaNative.Call(Cls, "setDeviceState", battery, charging == ChargingState.Unknown ? "" : MetaEnum.ToSdk(charging),
                thermal == ThermalLevel.Unknown ? "" : MetaEnum.ToSdk(thermal));

        public static void SimulateRegistration(bool success) => MetaNative.Call(Cls, "simulateRegistration", success);

        public static void SetPermission(MetaPermission permission, bool granted, bool asRequestResult = false) =>
            MetaNative.Call(Cls, "setPermission", MetaEnum.ToSdk(permission), granted ? "GRANTED" : "DENIED", asRequestResult);

        /// <summary>Vídeo H.264/H.265 no aparelho (caminho absoluto) como feed da câmera simulada.</summary>
        public static void SetCameraFeedFile(string absolutePath) => MetaNative.Call(Cls, "setCameraFeedFile", absolutePath);

        /// <summary>Usa a câmera do celular como feed (o app precisa da permissão android.permission.CAMERA).</summary>
        public static void SetCameraFeedPhoneCamera(bool frontCamera = false) =>
            MetaNative.Call(Cls, "setCameraFeedPhoneCamera", frontCamera ? "FRONT" : "BACK");

        public static void SetCapturedImageFile(string absolutePath) => MetaNative.Call(Cls, "setCapturedImageFile", absolutePath);

        public static void InjectInput(MockInput input) => MetaNative.Call(Cls, "injectInput", MetaEnum.ToSdk(input));

        public static void InjectDrag(DragAction action, Vector2 position, Vector2 delta) =>
            MetaNative.Call(Cls, "injectDrag", MetaEnum.ToSdk(action), position.x, position.y, delta.x, delta.y);

        public static void SimulateTranscription(string text, bool isFinal = true, float confidence = 0.9f) =>
            MetaNative.Call(Cls, "simulateTranscription", text, isFinal, confidence);

        public static void SimulateVoiceLaunch() => MetaNative.Call(Cls, "simulateVoiceLaunch");

        /// <summary>Desenha o display simulado por cima do Unity (vista real do que os óculos mostrariam).</summary>
        public static void ShowDisplayPreview(int sizePx = 600, ScreenCorner corner = ScreenCorner.TopRight) =>
            MetaNative.Call(Cls, "showDisplayPreview", MetaNative.Activity, sizePx, MetaEnum.ToSdk(corner));

        public static void HideDisplayPreview() => MetaNative.Call(Cls, "hideDisplayPreview", MetaNative.Activity);

        /// <summary>Preview no Chrome do PC: rode o comando adb do evento e abra a URL.</summary>
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
                    Debug.Log($"[MetaDisplay] Preview no PC: rode '{p.adb}' e abra {p.url}");
                    TestServerStarted?.Invoke(p.url, p.adb);
                    break;
            }
        }
    }
}
