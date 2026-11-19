using System.Globalization;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Outside Android there is no native bridge. So the flow can be tested in the Editor (Play Mode),
    /// this class answers calls as if a Meta Ray-Ban Display were connected:
    /// registration, session, display and screen sending. Camera/sensors only change state.
    /// Clicks are tested by clicking the screen mirror (GlassesScreen) or through the MetaDisplayManager.
    /// </summary>
    public static class MetaEditorSimulation
    {
        /// <summary>Turns the simulation on/off (the MetaDisplayManager controls this).</summary>
        public static bool Enabled = true;
        /// <summary>Starts as an app already registered in Meta AI (connects right away on Play).</summary>
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
                    Debug.Log("[MetaDisplay] Not on Android and the simulation is off: nothing to do.");
                return;
            }

            switch (cls + "." + method)
            {
                case "MDCore.initialize":
                    Emit("initialized", "{\"sdkVersion\":\"1.0.1\",\"bridgeVersion\":\"editor\",\"devMode\":true,\"modules\":[\"core\",\"display\",\"camera\",\"inputs\",\"motion\",\"speech\",\"mockdevice\"]}");
                    Emit("registration", "{\"state\":" + Q(s_Registered ? "REGISTERED" : "AVAILABLE") + "}");
                    Emit("devices", "{\"ids\":[\"editor-glasses\"]}");
                    Emit("device", "{\"id\":\"editor-glasses\",\"name\":\"Simulated glasses (Editor)\",\"type\":\"META_RAYBAN_DISPLAY\",\"linkState\":\"CONNECTED\",\"compatibility\":\"COMPATIBLE\",\"firmware\":\"editor\",\"battery\":87,\"charging\":\"NOT_CHARGING\",\"don\":\"DONNED\",\"hinge\":\"OPEN\",\"thermal\":\"NONE\",\"displayCapable\":true}");
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
                        Emit("session.error", "{\"error\":\"NO_ELIGIBLE_DEVICE\",\"message\":\"(Editor) Register the app before connecting\"}");
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
                        Emit("display.sendFailed", "{\"requestId\":" + idJson + ",\"error\":\"NOT_READY\",\"message\":\"(Editor) Display is not ready\"}");
                        break;
                    }
                    Emit("display.sent", "{\"requestId\":" + idJson + "}");
                    if (method == "playVideo") Emit("video.state", "{\"state\":\"PLAYING\"}");
                    break;
                }

                case "MDCamera.startStream":
                    Emit("camera.state", "{\"state\":\"STARTING\"}");
                    Emit("camera.error", "{\"error\":\"EDITOR\",\"message\":\"(Editor) The glasses camera only works on the device. Use MockDeviceKit on the phone.\"}");
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

        /// <summary>Simulates speech on the glasses (Editor).</summary>
        public static void SimulateTranscription(string text, bool isFinal = true) =>
            Emit("speech.transcription", "{\"text\":" + JsonString(text) + ",\"isFinal\":" + (isFinal ? "true" : "false") + ",\"confidence\":0.9}");

        /// <summary>Simulates a glasses input (Editor). kind: NAV, SELECT, BACK, CAPTURE...</summary>
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
