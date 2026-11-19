using System.Collections.Generic;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Panel on the phone screen to test everything without building UI: status, registration, connection,
    /// glasses screens, camera (with preview), IMU, inputs, speech, voice and MockDeviceKit controls.
    /// Tap "Meta Display" to collapse/expand.
    /// </summary>
    [AddComponentMenu("Meta Display/Debug Panel (phone)")]
    public class MetaPhoneDebugPanel : MonoBehaviour
    {
        [Tooltip("Starts collapsed (only the 'Meta Display' button).")]
        public bool startCollapsed;
        [Range(0.5f, 3f)] public float scale = 1f;

        bool m_Open = true;
        int m_Tab;
        Vector2 m_Scroll;
        readonly List<string> m_Log = new List<string>();
        GUIStyle m_Box, m_Button, m_Label, m_Title, m_Small;
        float m_Unit;
        Texture2D m_Photo;
        string m_LastInput = "-", m_LastSpeech = "-";

        static readonly string[] Tabs = { "Connection", "Glasses", "Camera", "Sensors", "Mock" };

        void Awake() => m_Open = !startCollapsed;

        void OnEnable()
        {
            MetaWearables.Error += OnError;
            MetaWearables.RegistrationChanged += OnRegistration;
            MetaWearables.SessionStateChanged += OnSession;
            MetaGlassesDisplay.StateChanged += OnDisplay;
            MetaGlassesDisplay.Clicked += OnClicked;
            MetaInputs.InputReceived += OnInput;
            MetaSpeech.TranscriptionReceived += OnSpeech;
            MetaCamera.StateChanged += OnCameraState;
            MetaCamera.Info += OnCameraInfo;
            MetaWearables.PermissionChanged += OnPermission;
        }

        void OnDisable()
        {
            MetaWearables.Error -= OnError;
            MetaWearables.RegistrationChanged -= OnRegistration;
            MetaWearables.SessionStateChanged -= OnSession;
            MetaGlassesDisplay.StateChanged -= OnDisplay;
            MetaGlassesDisplay.Clicked -= OnClicked;
            MetaInputs.InputReceived -= OnInput;
            MetaSpeech.TranscriptionReceived -= OnSpeech;
            MetaCamera.StateChanged -= OnCameraState;
            MetaCamera.Info -= OnCameraInfo;
            MetaWearables.PermissionChanged -= OnPermission;
        }

        void OnCameraState(CameraStreamState s) => Log("Camera: " + s);
        void OnCameraInfo(string message) => Log("Camera: " + message);
        void OnPermission(MetaPermission p, PermissionResult r) => Log($"Permission {p}: {r}");

        void OnError(MetaError e) => Log("<color=#ff8080>" + e + "</color>");
        void OnRegistration(RegistrationState s) => Log("Registration: " + s);
        void OnSession(SessionState s) => Log("Session: " + s);
        void OnDisplay(DisplayState s) => Log("Display: " + s);
        void OnClicked(string id) => Log("Click on the glasses: " + id);
        void OnInput(GlassesInputEvent e) => m_LastInput = e.ToString();
        void OnSpeech(SpeechResult r) => m_LastSpeech = (r.IsFinal ? "✔ " : "… ") + r.Text;

        void Log(string line)
        {
            m_Log.Insert(0, System.DateTime.Now.ToString("HH:mm:ss ") + line);
            if (m_Log.Count > 40) m_Log.RemoveAt(m_Log.Count - 1);
        }

        void BuildStyles()
        {
            float dpiScale = Mathf.Max(1f, Screen.dpi / 160f);
            m_Unit = 18f * dpiScale * scale;
            int font = Mathf.RoundToInt(m_Unit * 0.8f);
            m_Box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 8, 8) };
            m_Button = new GUIStyle(GUI.skin.button) { fontSize = font, fixedHeight = m_Unit * 2.2f, wordWrap = true };
            m_Label = new GUIStyle(GUI.skin.label) { fontSize = font, wordWrap = true, richText = true };
            m_Small = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(font * 0.8f) };
            m_Title = new GUIStyle(m_Label) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(font * 1.1f) };
        }

        void OnGUI()
        {
            if (m_Button == null) BuildStyles();
            float w = Mathf.Min(Screen.width - 16, m_Unit * 24);
            var safe = Screen.safeArea;
            float x = safe.x + 8, y = Screen.height - safe.yMax + 8;

            if (!m_Open)
            {
                if (GUI.Button(new Rect(x, y, m_Unit * 9, m_Unit * 2.2f), "Meta Display ▸", m_Button)) m_Open = true;
                return;
            }

            float h = Mathf.Min(safe.height - 16, m_Unit * 34);
            GUILayout.BeginArea(new Rect(x, y, w, h), m_Box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◂", m_Button, GUILayout.Width(m_Unit * 2.5f))) m_Open = false;
            GUILayout.Label("Meta Display", m_Title);
            GUILayout.EndHorizontal();
            GUILayout.Label(Status(), m_Small);
            m_Tab = GUILayout.Toolbar(m_Tab, Tabs, m_Button);
            m_Scroll = GUILayout.BeginScrollView(m_Scroll);
            switch (m_Tab)
            {
                case 0: ConnectionTab(); break;
                case 1: GlassesTab(); break;
                case 2: CameraTab(); break;
                case 3: SensorsTab(); break;
                case 4: MockTab(); break;
            }
            GUILayout.Space(m_Unit * 0.5f);
            GUILayout.Label("<b>Log</b>", m_Label);
            foreach (var line in m_Log) GUILayout.Label(line, m_Small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static string Status()
        {
            var dev = MetaWearables.SessionDevice;
            return $"SDK {MetaWearables.SdkVersion} {(MetaWearables.IsNative ? "" : "(Editor)")} {(MetaWearables.IsDevMode ? "· Dev Mode" : "")}\n" +
                   $"Registration: <b>{MetaWearables.Registration}</b> · Session: <b>{MetaWearables.Session}</b> · Display: <b>{MetaGlassesDisplay.State}</b>" +
                   (dev != null ? $"\n{dev.name} · battery {dev.battery}% · {dev.thermal}" : "");
        }

        bool Btn(string label) => GUILayout.Button(label, m_Button);

        void ConnectionTab()
        {
            var manager = MetaDisplayManager.Instance;
            if (!MetaWearables.IsInitialized && Btn("Initialize SDK")) MetaWearables.Initialize();
            if (!MetaWearables.IsRegistered)
            {
                if (Btn("1. Register in Meta AI")) MetaWearables.StartRegistration();
            }
            else if (Btn("Unregister")) MetaWearables.StartUnregistration();

            if (MetaWearables.Session != SessionState.Started)
            {
                if (Btn("2. Connect to the glasses"))
                {
                    if (manager != null) manager.Connect();
                    else
                    {
                        MetaGlassesDisplay.Enable();
                        MetaWearables.StartSession();
                    }
                }
            }
            else if (Btn("Disconnect")) MetaWearables.StopSession();

            if (MetaWearables.GlassesAppUpdateRequired && Btn("Update the DAT app on the glasses")) MetaWearables.OpenGlassesAppUpdate();
            if (Btn("Update the glasses firmware")) MetaWearables.OpenFirmwareUpdate();

            GUILayout.Label("<b>Devices</b>", m_Label);
            if (MetaWearables.Devices.Count == 0) GUILayout.Label("None. Pair the glasses in the Meta AI app.", m_Small);
            foreach (var d in MetaWearables.Devices.Values) GUILayout.Label("• " + d, m_Small);

            GUILayout.Label("<b>DAT permissions</b>", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Camera: " + MetaWearables.GetPermission(MetaPermission.Camera))) MetaWearables.RequestPermission(MetaPermission.Camera);
            if (Btn("Microphone: " + MetaWearables.GetPermission(MetaPermission.Microphone))) MetaWearables.RequestPermission(MetaPermission.Microphone);
            GUILayout.EndHorizontal();
        }

        void GlassesTab()
        {
#if METADISPLAY_UGUI
            GUILayout.Label("<b>Screens (GlassesScreen)</b>", m_Label);
            if (GlassesScreen.All.Count == 0) GUILayout.Label("No screens in the scene. Create one in GameObject > Meta Display > Glasses Screen.", m_Small);
            foreach (var screen in GlassesScreen.All)
            {
                if (screen == null) continue;
                string label = (screen.IsShowing ? "▶ " : "") + (string.IsNullOrEmpty(screen.screenId) ? screen.name : screen.screenId);
                if (Btn(label)) screen.Show();
            }
#endif
            GUILayout.Label("<b>Quick test</b>", m_Label);
            if (Btn("Send Hello World"))
                MetaGlassesDisplay.Show(Glasses.Column(12, 24).Add(
                    Glasses.Card().Add(Glasses.Heading("Hello, World!"), Glasses.Meta("Sent from Unity")),
                    Glasses.Button("Tap here", () => Log("Button tapped on the glasses"), icon: GlassesIconName.Checkmark, primary: true)));
            if (Btn("Re-send last screen")) MetaGlassesDisplay.Resend();
            if (Btn("Clear display")) MetaGlassesDisplay.Clear();
            if (Btn("Sample video (MP4)"))
                MetaGlassesDisplay.PlayVideo("https://github.com/facebook/meta-wearables-dat-android/raw/refs/heads/assets/video_266x150_faststart.mp4");
            GUILayout.Label("Video: " + MetaGlassesDisplay.Video, m_Small);
        }

        void CameraTab()
        {
            GUILayout.Label($"Stream: <b>{MetaCamera.State}</b> · {MetaCamera.FrameWidth}x{MetaCamera.FrameHeight} · frames {MetaCamera.FramesReceived} · YUV {MetaCamera.DetectedLayout}", m_Small);
            GUILayout.BeginHorizontal();
            if (Btn("Start (medium, 24 fps)")) MetaCamera.Start();
            if (Btn("Stop")) MetaCamera.Stop();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Quick photo (from stream)")) MetaCamera.CapturePhoto(t => { m_Photo = t; Log("Photo " + t.width + "x" + t.height); });
            if (Btn("HQ photo (medium)")) CaptureHq(PhotoResolution.Medium);
            GUILayout.EndHorizontal();
            if (Btn("HQ photo, full size (~1 min over Bluetooth)")) CaptureHq(PhotoResolution.Full);
            if (Btn("Colors swapped? Layout: " + MetaCamera.LayoutOverride))
                MetaCamera.LayoutOverride = (YuvLayout)(((int)MetaCamera.LayoutOverride + 1) % 4);
            var tex = MetaCamera.Texture;
            if (tex != null)
            {
                float pw = m_Unit * 10, ph = pw * tex.height / Mathf.Max(1, tex.width);
                var r = GUILayoutUtility.GetRect(pw, ph, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            }
            if (m_Photo != null)
            {
                float pw = m_Unit * 10, ph = pw * m_Photo.height / Mathf.Max(1, m_Photo.width);
                var r = GUILayoutUtility.GetRect(pw, ph, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(r, m_Photo, ScaleMode.ScaleToFit);
                if (Btn("Show photo on the glasses")) MetaGlassesDisplay.Show(Glasses.Column(8, 16).Add(Glasses.Image(m_Photo), Glasses.Meta("Photo from the glasses")));
            }
        }

        void CaptureHq(PhotoResolution resolution)
        {
            Log($"HQ photo ({resolution}) requested: the video pauses during the capture and comes back on its own.");
            MetaCamera.CaptureHighQualityPhoto(
                t => { m_Photo = t; Log("HQ photo " + t.width + "x" + t.height); },
                e => Log("<color=#ff8080>HQ photo failed: " + e + "</color>"),
                resolution,
                onProgress: f => Log($"Transferring photo: {f:P0}"));
        }

        void SensorsTab()
        {
            GUILayout.Label($"<b>IMU</b> ({MetaMotion.State})", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Start 30 Hz")) MetaMotion.Start();
            if (Btn("Stop")) MetaMotion.Stop();
            GUILayout.EndHorizontal();
            var g = MetaMotion.LatestGlasses;
            GUILayout.Label($"Glasses acc {g.Accelerometer:F2} gyro {g.Gyroscope:F2}\nori {g.Orientation.eulerAngles:F0}", m_Small);
            var b = MetaMotion.LatestNeuralBand;
            if (b.TimestampNs != 0) GUILayout.Label($"Neural Band ori {b.Orientation.eulerAngles:F0}", m_Small);

            GUILayout.Label($"<b>Inputs</b> ({MetaInputs.State})", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Start")) MetaInputs.Start();
            if (Btn("Stop")) MetaInputs.Stop();
            GUILayout.EndHorizontal();
            GUILayout.Label("Last: " + m_LastInput, m_Small);

            GUILayout.Label($"<b>Speech</b> ({MetaSpeech.State}) {MetaSpeech.Locale}", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Listen")) MetaSpeech.Start();
            if (Btn("Stop")) MetaSpeech.Stop();
            GUILayout.EndHorizontal();
            GUILayout.Label(m_LastSpeech, m_Small);

            GUILayout.Label($"<b>Hey Meta</b> ({MetaVoiceInvocations.State}) {(MetaVoiceInvocations.LaunchedByVoice ? "· opened by voice" : "")}", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Listen")) MetaVoiceInvocations.Start();
            if (Btn("Stop")) MetaVoiceInvocations.Stop();
            GUILayout.EndHorizontal();
        }

        void MockTab()
        {
            GUILayout.Label("Simulated glasses (MockDeviceKit), no hardware needed.", m_Small);
            GUILayout.BeginHorizontal();
            if (Btn("Enable + pair Display"))
            {
                MetaMockDevice.Enable();
                MetaMockDevice.PairGlasses();
            }
            if (Btn("Disable")) MetaMockDevice.Disable();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Show preview")) MetaMockDevice.ShowDisplayPreview();
            if (Btn("Hide preview")) MetaMockDevice.HideDisplayPreview();
            GUILayout.EndHorizontal();
            if (Btn("Preview in the PC's Chrome (port 9000)")) MetaMockDevice.StartTestServer();
            if (!string.IsNullOrEmpty(MetaMockDevice.TestServerUrl)) GUILayout.Label("adb forward tcp:9000 tcp:9000 → " + MetaMockDevice.TestServerUrl, m_Small);

            GUILayout.Label("<b>Inject</b>", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("▲")) MetaMockDevice.InjectInput(MockInput.NavUp);
            if (Btn("▼")) MetaMockDevice.InjectInput(MockInput.NavDown);
            if (Btn("Select")) MetaMockDevice.InjectInput(MockInput.Select);
            if (Btn("Back")) MetaMockDevice.InjectInput(MockInput.Back);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Speech: \"hello glasses\"")) MetaMockDevice.SimulateTranscription("hello glasses");
            if (Btn("\"Hey Meta, open\"")) MetaMockDevice.SimulateVoiceLaunch();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Phone camera as feed")) MetaMockDevice.SetCameraFeedPhoneCamera();
            if (Btn("Battery 15%")) MetaMockDevice.SetDeviceState(15);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Take off")) MetaMockDevice.Glasses(MockGlassesAction.Doff);
            if (Btn("Put on")) MetaMockDevice.Glasses(MockGlassesAction.Don);
            GUILayout.EndHorizontal();
        }
    }
}
