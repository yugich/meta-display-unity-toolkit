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

        static readonly string[] Tabs = { "Connection", "Glasses", "3D", "Camera", "Sensors", "Mock" };
        static readonly string[] Strategies = { "Live frames", "Baked frames", "Video clips", "Web App" };
        static readonly int[] Resolutions = { 150, 200, 300, 400, 600 };
        static readonly int[] Qualities = { 30, 50, 70, 90 };
        static readonly float[] FpsLimits = { 2, 5, 10, 20 };

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
                case 2: ThreeDTab(); break;
                case 3: CameraTab(); break;
                case 4: SensorsTab(); break;
                case 5: MockTab(); break;
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

        static string[] Labels<T>(T[] values)
        {
            var labels = new string[values.Length];
            for (int i = 0; i < values.Length; i++) labels[i] = values[i].ToString();
            return labels;
        }

        int Pick<T>(T[] values, T current) => GUILayout.Toolbar(System.Array.IndexOf(values, current), Labels(values), m_Button);

        void ThreeDTab()
        {
            var view = Glasses3DView.All.Count > 0 ? Glasses3DView.All[0] : null;
            if (view == null)
            {
                GUILayout.Label("No Glasses 3D View in this scene. In the Editor: Meta Display > 3D > Create Avatar Sample Scene.", m_Small);
                return;
            }
            GUILayout.Label("<b>How the 3D object reaches the glasses</b>", m_Label);
            int strategy = GUILayout.SelectionGrid((int)view.strategy, Strategies, 2, m_Button);
            if (strategy != (int)view.strategy) view.SetStrategy((Glasses3DStrategy)strategy);

            var m = view.Metrics;
            GUILayout.Label($"<b>{m.Fps:0.0} fps</b> · latency {m.LastLatencyMs:0} ms (avg {m.AvgLatencyMs:0}) · {m.LastBytes / 1024f:0.0} KB/frame\n" +
                            $"capture {m.CaptureMs:0.0} ms · sent {m.FramesSent} · skipped {m.FramesSkipped} · failed {m.FramesFailed} · action '{view.CurrentAction}'", m_Small);
            GUILayout.BeginHorizontal();
            if (view.IsShowing)
            {
                if (Btn("Stop sending")) view.Hide();
            }
            else if (Btn("Show on the glasses")) view.Show();
            if (Btn("Reset metrics")) view.ResetMetrics();
            GUILayout.EndHorizontal();

            if (view.Preview != null)
            {
                float side = m_Unit * 8;
                var rect = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(rect, view.Preview, ScaleMode.ScaleToFit);
            }

            GUILayout.Label("<b>Actions</b>", m_Label);
            var actions = view.Actions;
            for (int i = 0; i < actions.Count; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 3, actions.Count); j++)
                    if (Btn(actions[j])) view.Play(actions[j]);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (Btn("⟲ Rotate")) view.Rotate(-30);
            if (Btn("Rotate ⟳")) view.Rotate(30);
            if (Btn("Say hello")) view.Say("Hello from the phone!", 4f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Speech: " + MetaSpeech.State)) MetaSpeech.Start();
            if (Btn("Inputs: " + MetaInputs.State)) MetaInputs.Start(false);
            GUILayout.EndHorizontal();

            switch (view.strategy)
            {
                case Glasses3DStrategy.LiveFrames:
                case Glasses3DStrategy.BakedFrames:
                {
                    bool changed = false;
                    GUILayout.Label("Image size (px)", m_Small);
                    int r = Pick(Resolutions, view.resolution);
                    if (r >= 0 && Resolutions[r] != view.resolution) { view.resolution = Resolutions[r]; changed = true; }
                    GUILayout.Label("JPEG quality", m_Small);
                    int q = Pick(Qualities, view.jpegQuality);
                    if (q >= 0 && Qualities[q] != view.jpegQuality) { view.jpegQuality = Qualities[q]; changed = true; }
                    GUILayout.Label("Max frames per second", m_Small);
                    int f = Pick(FpsLimits, view.maxFps);
                    if (f >= 0 && !Mathf.Approximately(FpsLimits[f], view.maxFps)) { view.maxFps = FpsLimits[f]; changed = true; }
                    GUILayout.Label("Encoding", m_Small);
                    int e = GUILayout.Toolbar((int)view.encoding, new[] { "Direct JPEG", "SDK bitmap" }, m_Button);
                    if (e != (int)view.encoding) { view.encoding = (Glasses3DEncoding)e; changed = true; }
                    if (view.strategy == Glasses3DStrategy.LiveFrames)
                    {
                        GUILayout.Label("Send", m_Small);
                        int u = GUILayout.Toolbar((int)view.updateMode, new[] { "Always", "On change", "Manual" }, m_Button);
                        if (u != (int)view.updateMode) { view.updateMode = (Glasses3DUpdateMode)u; changed = true; }
                        if (view.updateMode == Glasses3DUpdateMode.Manual && Btn("Send one frame")) view.SendFrameNow();
                    }
                    else if (Btn(view.IsBaking ? $"Baking… {view.BakeProgress:P0}" : "Bake again")) view.Bake();
                    if (changed) view.ApplySettings();
                    break;
                }
                case Glasses3DStrategy.VideoClips:
                    GUILayout.Label(string.IsNullOrEmpty(view.videoBaseUrl)
                        ? "Set Video base URL (HTTPS folder with <action>.mp4). Record the clips in the Editor: Meta Display > 3D > Record MP4 Clips."
                        : "Clips: " + view.videoBaseUrl + "/<action>.mp4 · video " + MetaGlassesDisplay.Video, m_Small);
                    break;
                case Glasses3DStrategy.WebApp:
                {
                    var link = view.webAppLink;
                    GUILayout.Label(link == null ? "Add a Glasses Web App Link to the view." :
                        $"Relay: {(string.IsNullOrEmpty(link.relayUrl) ? "(not set)" : link.relayUrl)} · room '{link.room}'\n{link.Status}" +
                        (link.WebAppOnline ? " · the Web App is open on the glasses" : ""), m_Small);
                    GUILayout.Label("Open the Web App from the glasses app grid (export it in the Editor: Meta Display > 3D > Export Web App).", m_Small);
                    break;
                }
            }
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
