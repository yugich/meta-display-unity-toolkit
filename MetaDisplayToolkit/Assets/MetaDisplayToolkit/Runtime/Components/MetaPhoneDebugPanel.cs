using System.Collections.Generic;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Painel na tela do celular para testar tudo sem montar UI: status, registro, conexão, telas dos
    /// óculos, câmera (com preview), IMU, inputs, fala, voz e controles do MockDeviceKit.
    /// Toque em "Meta Display" para recolher/abrir.
    /// </summary>
    [AddComponentMenu("Meta Display/Painel de Debug (celular)")]
    public class MetaPhoneDebugPanel : MonoBehaviour
    {
        [Tooltip("Começa recolhido (só o botão 'Meta Display').")]
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

        static readonly string[] Tabs = { "Conexão", "Óculos", "Câmera", "Sensores", "Mock" };

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

        void OnCameraState(CameraStreamState s) => Log("Câmera: " + s);
        void OnCameraInfo(string message) => Log("Câmera: " + message);
        void OnPermission(MetaPermission p, PermissionResult r) => Log($"Permissão {p}: {r}");

        void OnError(MetaError e) => Log("<color=#ff8080>" + e + "</color>");
        void OnRegistration(RegistrationState s) => Log("Registro: " + s);
        void OnSession(SessionState s) => Log("Sessão: " + s);
        void OnDisplay(DisplayState s) => Log("Display: " + s);
        void OnClicked(string id) => Log("Clique nos óculos: " + id);
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
                   $"Registro: <b>{MetaWearables.Registration}</b> · Sessão: <b>{MetaWearables.Session}</b> · Display: <b>{MetaGlassesDisplay.State}</b>" +
                   (dev != null ? $"\n{dev.name} · bateria {dev.battery}% · {dev.thermal}" : "");
        }

        bool Btn(string label) => GUILayout.Button(label, m_Button);

        void ConnectionTab()
        {
            var manager = MetaDisplayManager.Instance;
            if (!MetaWearables.IsInitialized && Btn("Inicializar SDK")) MetaWearables.Initialize();
            if (!MetaWearables.IsRegistered)
            {
                if (Btn("1. Registrar no Meta AI")) MetaWearables.StartRegistration();
            }
            else if (Btn("Remover registro")) MetaWearables.StartUnregistration();

            if (MetaWearables.Session != SessionState.Started)
            {
                if (Btn("2. Conectar aos óculos"))
                {
                    if (manager != null) manager.Connect();
                    else
                    {
                        MetaGlassesDisplay.Enable();
                        MetaWearables.StartSession();
                    }
                }
            }
            else if (Btn("Desconectar")) MetaWearables.StopSession();

            if (MetaWearables.GlassesAppUpdateRequired && Btn("Atualizar app DAT nos óculos")) MetaWearables.OpenGlassesAppUpdate();
            if (Btn("Atualizar firmware dos óculos")) MetaWearables.OpenFirmwareUpdate();

            GUILayout.Label("<b>Dispositivos</b>", m_Label);
            if (MetaWearables.Devices.Count == 0) GUILayout.Label("Nenhum. Pareie os óculos no app Meta AI.", m_Small);
            foreach (var d in MetaWearables.Devices.Values) GUILayout.Label("• " + d, m_Small);

            GUILayout.Label("<b>Permissões do DAT</b>", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Câmera: " + MetaWearables.GetPermission(MetaPermission.Camera))) MetaWearables.RequestPermission(MetaPermission.Camera);
            if (Btn("Microfone: " + MetaWearables.GetPermission(MetaPermission.Microphone))) MetaWearables.RequestPermission(MetaPermission.Microphone);
            GUILayout.EndHorizontal();
        }

        void GlassesTab()
        {
#if METADISPLAY_UGUI
            GUILayout.Label("<b>Telas (GlassesScreen)</b>", m_Label);
            if (GlassesScreen.All.Count == 0) GUILayout.Label("Nenhuma tela na cena. Crie em GameObject > Meta Display > Tela dos Óculos.", m_Small);
            foreach (var screen in GlassesScreen.All)
            {
                if (screen == null) continue;
                string label = (screen.IsShowing ? "▶ " : "") + (string.IsNullOrEmpty(screen.screenId) ? screen.name : screen.screenId);
                if (Btn(label)) screen.Show();
            }
#endif
            GUILayout.Label("<b>Teste rápido</b>", m_Label);
            if (Btn("Enviar Hello World"))
                MetaGlassesDisplay.Show(Glasses.Column(12, 24).Add(
                    Glasses.Card().Add(Glasses.Heading("Hello, World!"), Glasses.Meta("Enviado pelo Unity")),
                    Glasses.Button("Toque aqui", () => Log("Botão tocado nos óculos"), icon: GlassesIconName.Checkmark, primary: true)));
            if (Btn("Reenviar última tela")) MetaGlassesDisplay.Resend();
            if (Btn("Limpar display")) MetaGlassesDisplay.Clear();
            if (Btn("Vídeo de exemplo (MP4)"))
                MetaGlassesDisplay.PlayVideo("https://github.com/facebook/meta-wearables-dat-android/raw/refs/heads/assets/video_266x150_faststart.mp4");
            GUILayout.Label("Vídeo: " + MetaGlassesDisplay.Video, m_Small);
        }

        void CameraTab()
        {
            GUILayout.Label($"Stream: <b>{MetaCamera.State}</b> · {MetaCamera.FrameWidth}x{MetaCamera.FrameHeight} · frames {MetaCamera.FramesReceived} · YUV {MetaCamera.DetectedLayout}", m_Small);
            GUILayout.BeginHorizontal();
            if (Btn("Iniciar (média, 24 fps)")) MetaCamera.Start();
            if (Btn("Parar")) MetaCamera.Stop();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Foto rápida (do stream)")) MetaCamera.CapturePhoto(t => { m_Photo = t; Log("Foto " + t.width + "x" + t.height); });
            if (Btn("Foto HQ (média)")) CaptureHq(PhotoResolution.Medium);
            GUILayout.EndHorizontal();
            if (Btn("Foto HQ máxima (~1 min pelo Bluetooth)")) CaptureHq(PhotoResolution.Full);
            if (Btn("Cores trocadas? Layout: " + MetaCamera.LayoutOverride))
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
                if (Btn("Mostrar foto nos óculos")) MetaGlassesDisplay.Show(Glasses.Column(8, 16).Add(Glasses.Image(m_Photo), Glasses.Meta("Foto dos óculos")));
            }
        }

        void CaptureHq(PhotoResolution resolution)
        {
            Log($"Foto HQ ({resolution}) pedida: o vídeo pausa durante a captura e volta sozinho.");
            MetaCamera.CaptureHighQualityPhoto(
                t => { m_Photo = t; Log("Foto HQ " + t.width + "x" + t.height); },
                e => Log("<color=#ff8080>Foto HQ falhou: " + e + "</color>"),
                resolution,
                onProgress: f => Log($"Transferindo foto: {f:P0}"));
        }

        void SensorsTab()
        {
            GUILayout.Label($"<b>IMU</b> ({MetaMotion.State})", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Iniciar 30 Hz")) MetaMotion.Start();
            if (Btn("Parar")) MetaMotion.Stop();
            GUILayout.EndHorizontal();
            var g = MetaMotion.LatestGlasses;
            GUILayout.Label($"Óculos acc {g.Accelerometer:F2} gyro {g.Gyroscope:F2}\nori {g.Orientation.eulerAngles:F0}", m_Small);
            var b = MetaMotion.LatestNeuralBand;
            if (b.TimestampNs != 0) GUILayout.Label($"Neural Band ori {b.Orientation.eulerAngles:F0}", m_Small);

            GUILayout.Label($"<b>Inputs</b> ({MetaInputs.State})", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Iniciar")) MetaInputs.Start();
            if (Btn("Parar")) MetaInputs.Stop();
            GUILayout.EndHorizontal();
            GUILayout.Label("Último: " + m_LastInput, m_Small);

            GUILayout.Label($"<b>Fala</b> ({MetaSpeech.State}) {MetaSpeech.Locale}", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Ouvir")) MetaSpeech.Start();
            if (Btn("Parar")) MetaSpeech.Stop();
            GUILayout.EndHorizontal();
            GUILayout.Label(m_LastSpeech, m_Small);

            GUILayout.Label($"<b>Hey Meta</b> ({MetaVoiceInvocations.State}) {(MetaVoiceInvocations.LaunchedByVoice ? "· aberto por voz" : "")}", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("Escutar")) MetaVoiceInvocations.Start();
            if (Btn("Parar")) MetaVoiceInvocations.Stop();
            GUILayout.EndHorizontal();
        }

        void MockTab()
        {
            GUILayout.Label("Óculos simulados (MockDeviceKit) — sem hardware.", m_Small);
            GUILayout.BeginHorizontal();
            if (Btn("Ativar + parear Display"))
            {
                MetaMockDevice.Enable();
                MetaMockDevice.PairGlasses();
            }
            if (Btn("Desativar")) MetaMockDevice.Disable();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Mostrar preview")) MetaMockDevice.ShowDisplayPreview();
            if (Btn("Esconder preview")) MetaMockDevice.HideDisplayPreview();
            GUILayout.EndHorizontal();
            if (Btn("Preview no Chrome do PC (porta 9000)")) MetaMockDevice.StartTestServer();
            if (!string.IsNullOrEmpty(MetaMockDevice.TestServerUrl)) GUILayout.Label("adb forward tcp:9000 tcp:9000 → " + MetaMockDevice.TestServerUrl, m_Small);

            GUILayout.Label("<b>Injetar</b>", m_Label);
            GUILayout.BeginHorizontal();
            if (Btn("▲")) MetaMockDevice.InjectInput(MockInput.NavUp);
            if (Btn("▼")) MetaMockDevice.InjectInput(MockInput.NavDown);
            if (Btn("Selecionar")) MetaMockDevice.InjectInput(MockInput.Select);
            if (Btn("Voltar")) MetaMockDevice.InjectInput(MockInput.Back);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Fala: \"olá óculos\"")) MetaMockDevice.SimulateTranscription("olá óculos");
            if (Btn("\"Hey Meta, abrir\"")) MetaMockDevice.SimulateVoiceLaunch();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Câmera do celular como feed")) MetaMockDevice.SetCameraFeedPhoneCamera();
            if (Btn("Bateria 15%")) MetaMockDevice.SetDeviceState(15);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Btn("Tirar do rosto")) MetaMockDevice.Glasses(MockGlassesAction.Doff);
            if (Btn("Colocar no rosto")) MetaMockDevice.Glasses(MockGlassesAction.Don);
            GUILayout.EndHorizontal();
        }
    }
}
