using System.Globalization;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Fora do Android não existe ponte nativa. Para dar para testar o fluxo no Editor (Play Mode),
    /// esta classe responde às chamadas como se houvesse um Meta Ray-Ban Display conectado:
    /// registro, sessão, display e envio de telas. Câmera/sensores apenas mudam de estado.
    /// Os cliques são testados clicando no espelho da tela (GlassesScreen) ou pelo MetaDisplayManager.
    /// </summary>
    public static class MetaEditorSimulation
    {
        /// <summary>Liga/desliga a simulação (o MetaDisplayManager controla isso).</summary>
        public static bool Enabled = true;
        /// <summary>Começa como app já registrado no Meta AI (conecta direto ao dar Play).</summary>
        public static bool StartRegistered = true;

        static bool s_Registered = true;
        static bool s_SessionStarted;
        static bool s_DisplayWanted;

        static string Q(string s) => "\"" + s + "\"";

        static void Emit(string type, string json = "{}") => MetaDispatcher.Enqueue(type, json);

        internal static void OnNativeCall(string cls, string method, object[] args)
        {
            if (!Application.isPlaying) return;
            if (!Enabled)
            {
                if (cls == "MDCore" && method == "initialize")
                    Debug.Log("[MetaDisplay] Fora do Android e com a simulação desligada: nada a fazer.");
                return;
            }

            switch (cls + "." + method)
            {
                case "MDCore.initialize":
                    Emit("initialized", "{\"sdkVersion\":\"1.0.1\",\"bridgeVersion\":\"editor\",\"devMode\":true,\"modules\":[\"core\",\"display\",\"camera\",\"inputs\",\"motion\",\"speech\",\"mockdevice\"]}");
                    Emit("registration", "{\"state\":" + Q(s_Registered ? "REGISTERED" : "AVAILABLE") + "}");
                    Emit("devices", "{\"ids\":[\"editor-glasses\"]}");
                    Emit("device", "{\"id\":\"editor-glasses\",\"name\":\"Óculos simulados (Editor)\",\"type\":\"META_RAYBAN_DISPLAY\",\"linkState\":\"CONNECTED\",\"compatibility\":\"COMPATIBLE\",\"firmware\":\"editor\",\"battery\":87,\"charging\":\"NOT_CHARGING\",\"don\":\"DONNED\",\"hinge\":\"OPEN\",\"thermal\":\"NONE\",\"displayCapable\":true}");
                    break;

                case "MDCore.startRegistration":
                    s_Registered = true;
                    Emit("registration", "{\"state\":\"REGISTERING\"}");
                    Emit("registration", "{\"state\":\"REGISTERED\"}");
                    break;

                case "MDCore.startUnregistration":
                    s_Registered = false;
                    Emit("registration", "{\"state\":\"AVAILABLE\"}");
                    break;

                case "MDCore.checkPermission":
                case "MDCore.requestPermission":
                {
                    var permission = args[args.Length - 1] as string;
                    Emit("permission", "{\"permission\":" + Q(permission) + ",\"status\":\"GRANTED\"}");
                    break;
                }

                case "MDCore.startSession":
                    if (!s_Registered)
                    {
                        Emit("session.error", "{\"error\":\"NO_ELIGIBLE_DEVICE\",\"message\":\"(Editor) Registre o app antes de conectar\"}");
                        break;
                    }
                    if (s_SessionStarted) break;
                    s_SessionStarted = true;
                    Emit("session.state", "{\"state\":\"STARTING\"}");
                    Emit("session.state", "{\"state\":\"STARTED\"}");
                    if (s_DisplayWanted) EmitDisplayStarted();
                    break;

                case "MDCore.stopSession":
                    s_SessionStarted = false;
                    Emit("display.state", "{\"state\":\"STOPPED\"}");
                    Emit("session.state", "{\"state\":\"STOPPED\"}");
                    break;

                case "MDDisplay.enable":
                    s_DisplayWanted = true;
                    if (s_SessionStarted) EmitDisplayStarted();
                    break;

                case "MDDisplay.disable":
                    s_DisplayWanted = false;
                    Emit("display.state", "{\"state\":\"STOPPED\"}");
                    break;

                case "MDDisplay.sendLayout":
                case "MDDisplay.playVideo":
                {
                    int id = args.Length > 1 && args[1] is int i ? i : 0;
                    var idJson = id.ToString(CultureInfo.InvariantCulture);
                    if (!s_SessionStarted || !s_DisplayWanted)
                    {
                        Emit("display.sendFailed", "{\"requestId\":" + idJson + ",\"error\":\"NOT_READY\",\"message\":\"(Editor) Display não está pronto\"}");
                        break;
                    }
                    Emit("display.sent", "{\"requestId\":" + idJson + "}");
                    if (method == "playVideo") Emit("video.state", "{\"state\":\"PLAYING\"}");
                    break;
                }

                case "MDCamera.startStream":
                    Emit("camera.state", "{\"state\":\"STARTING\"}");
                    Emit("camera.error", "{\"error\":\"EDITOR\",\"message\":\"(Editor) A câmera dos óculos só funciona no aparelho. Use o MockDeviceKit no celular.\"}");
                    break;

                case "MDCamera.stopStream":
                    Emit("camera.state", "{\"state\":\"STOPPED\"}");
                    break;

                case "MDMotion.start":
                    Emit("motion.state", "{\"state\":\"STARTED\"}");
                    break;
                case "MDMotion.stop":
                    Emit("motion.state", "{\"state\":\"STOPPED\"}");
                    break;
                case "MDInputs.start":
                    Emit("inputs.state", "{\"state\":\"ACTIVE\"}");
                    break;
                case "MDInputs.stop":
                    Emit("inputs.state", "{\"state\":\"INACTIVE\"}");
                    break;
                case "MDSpeech.start":
                    Emit("speech.state", "{\"state\":\"STARTED\"}");
                    break;
                case "MDSpeech.stop":
                    Emit("speech.state", "{\"state\":\"STOPPED\"}");
                    break;
                case "MDVoice.start":
                    Emit("voice.state", "{\"state\":\"STARTED\"}");
                    break;
                case "MDVoice.stop":
                    Emit("voice.state", "{\"state\":\"STOPPED\"}");
                    break;
            }
        }

        static void EmitDisplayStarted()
        {
            Emit("display.state", "{\"state\":\"STARTING\"}");
            Emit("display.state", "{\"state\":\"STARTED\"}");
        }

        /// <summary>Simula uma fala nos óculos (Editor).</summary>
        public static void SimulateTranscription(string text, bool isFinal = true) =>
            Emit("speech.transcription", "{\"text\":" + JsonString(text) + ",\"isFinal\":" + (isFinal ? "true" : "false") + ",\"confidence\":0.9}");

        /// <summary>Simula um input dos óculos (Editor). kind: NAV, SELECT, BACK, CAPTURE...</summary>
        public static void SimulateInput(InputKind kind, NavDirection direction = NavDirection.Down) =>
            Emit("input", "{\"kind\":" + Q(MetaEnum.ToSdk(kind)) + ",\"direction\":" + Q(MetaEnum.ToSdk(direction)) + ",\"source\":\"CAPTOUCH\"}");

        static string JsonString(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Registered = StartRegistered;
            s_SessionStarted = false;
            s_DisplayWanted = false;
            Enabled = true;
        }
    }
}
