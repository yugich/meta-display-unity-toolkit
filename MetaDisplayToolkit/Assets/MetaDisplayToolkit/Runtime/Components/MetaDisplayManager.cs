using System;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDisplay
{
    /// <summary>
    /// Coloque um na cena e configure tudo pelo Inspector: conexão, telas dos óculos, o que aparece
    /// no celular, capacidades (câmera, IMU, inputs, fala, voz), simulação e eventos.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("Meta Display/Meta Display Manager")]
    public class MetaDisplayManager : MonoBehaviour
    {
        [Serializable] public class StringEvent : UnityEvent<string> { }

        // ── Conexão ──
        [Tooltip("Inicializa o SDK ao abrir a cena (pede a permissão de Bluetooth no Android 12+).")]
        public bool initializeOnStart = true;
        [Tooltip("Abre o Meta AI para registrar o app automaticamente se ainda não estiver registrado.\nRecomendado: deixar desligado e usar um botão (o painel de debug tem um).")]
        public bool registerAutomatically;
        [Tooltip("Conecta aos óculos assim que o app estiver registrado.")]
        public bool connectWhenRegistered = true;
        public DeviceSelection deviceSelection = DeviceSelection.DisplayCapable;
        public string specificDeviceId = "";

        // ── Óculos ──
        [Tooltip("Anexa o Display dos óculos ao conectar.")]
        public bool enableDisplay = true;
#if METADISPLAY_UGUI
        [Tooltip("Tela enviada aos óculos quando o display fica pronto.")]
        public GlassesScreen initialScreen;
        [Tooltip("Ao reconectar, reenvia a tela que estava aberta (em vez de voltar para a inicial).")]
        public bool restoreCurrentScreen = true;
#endif

        // ── Capacidades ──
        public bool startCamera;
        public CameraQuality cameraQuality = CameraQuality.Medium;
        public CameraFps cameraFps = CameraFps.Fps24;
        public CameraAudio cameraAudio = CameraAudio.None;
        public bool startMotion;
        public MotionRate motionRate = MotionRate.Hz30;
        public bool startInputs;
        [Tooltip("O app recebe o gesto Voltar em vez do sistema.")]
        public bool inputsConsumeBack = true;
        public bool startSpeech;
        public bool startVoiceInvocations;

        // ── Celular ──
        [Tooltip("Painel na tela do celular com status e botões (registrar, conectar, telas, câmera, sensores...).")]
        public bool showPhoneDebugPanel = true;

        // ── Testes ──
        [Tooltip("No Editor (Play Mode), simula óculos conectados para testar o fluxo e as telas.")]
        public bool simulateInEditor = true;
        [Tooltip("No celular, usa óculos SIMULADOS (MockDeviceKit) em vez dos reais.")]
        public bool useMockDevice;
        public bool mockShowDisplayPreview = true;
        public int mockPreviewSize = 480;
        public ScreenCorner mockPreviewCorner = ScreenCorner.TopRight;

        // ── Eventos ──
        public UnityEvent onInitialized = new UnityEvent();
        public UnityEvent onRegistered = new UnityEvent();
        public UnityEvent onConnected = new UnityEvent();
        public UnityEvent onDisplayReady = new UnityEvent();
        public UnityEvent onDisconnected = new UnityEvent();
        public StringEvent onError = new StringEvent();

        public static MetaDisplayManager Instance { get; private set; }

        bool m_ConnectRequested;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[MetaDisplay] Já existe um MetaDisplayManager na cena; este será ignorado.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnEnable()
        {
            if (Instance != this) return;
            MetaWearables.Initialized += OnInitialized;
            MetaWearables.RegistrationChanged += OnRegistration;
            MetaWearables.SessionStateChanged += OnSession;
            MetaWearables.Error += OnErrorRaised;
            MetaGlassesDisplay.StateChanged += OnDisplay;
        }

        void OnDisable()
        {
            MetaWearables.Initialized -= OnInitialized;
            MetaWearables.RegistrationChanged -= OnRegistration;
            MetaWearables.SessionStateChanged -= OnSession;
            MetaWearables.Error -= OnErrorRaised;
            MetaGlassesDisplay.StateChanged -= OnDisplay;
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (Instance != this) return;
            MetaEditorSimulation.Enabled = simulateInEditor;
            if (showPhoneDebugPanel && GetComponent<MetaPhoneDebugPanel>() == null) gameObject.AddComponent<MetaPhoneDebugPanel>();
            if (initializeOnStart) MetaWearables.Initialize();
        }

        /// <summary>Conecta aos óculos e pede as capacidades marcadas no Inspector.</summary>
        public void Connect()
        {
            m_ConnectRequested = true;
            if (enableDisplay) MetaGlassesDisplay.Enable();
            if (startCamera) MetaCamera.Start(cameraQuality, cameraFps, cameraAudio);
            if (startMotion) MetaMotion.Start(motionRate);
            if (startInputs) MetaInputs.Start(inputsConsumeBack);
            if (startSpeech) MetaSpeech.Start();
            MetaWearables.StartSession(deviceSelection, specificDeviceId);
        }

        public void Disconnect()
        {
            m_ConnectRequested = false;
            MetaWearables.StopSession();
        }

        public void Register() => MetaWearables.StartRegistration();

        public void Unregister() => MetaWearables.StartUnregistration();

        void OnInitialized()
        {
            if (useMockDevice && MetaWearables.IsNative)
            {
                MetaMockDevice.Enable();
                MetaMockDevice.PairGlasses();
                if (mockShowDisplayPreview) MetaMockDevice.ShowDisplayPreview(mockPreviewSize, mockPreviewCorner);
            }
            if (startVoiceInvocations) MetaVoiceInvocations.Start();
            onInitialized?.Invoke();
        }

        void OnRegistration(RegistrationState state)
        {
            if (state == RegistrationState.Registered)
            {
                onRegistered?.Invoke();
                if (connectWhenRegistered && !m_ConnectRequested && MetaWearables.Session != SessionState.Started) Connect();
            }
            else if (state == RegistrationState.Available && registerAutomatically)
            {
                MetaWearables.StartRegistration();
            }
        }

        void OnSession(SessionState state)
        {
            if (state == SessionState.Started) onConnected?.Invoke();
            else if (state == SessionState.Stopped)
            {
                m_ConnectRequested = false;
                onDisconnected?.Invoke();
            }
        }

        void OnDisplay(DisplayState state)
        {
            if (state != DisplayState.Started) return;
            onDisplayReady?.Invoke();
#if METADISPLAY_UGUI
            var target = restoreCurrentScreen && GlassesScreen.Current != null ? GlassesScreen.Current : initialScreen;
            if (target != null) target.Show();
#endif
        }

        void OnErrorRaised(MetaError error) => onError?.Invoke(error.ToString());

        void OnApplicationQuit()
        {
            if (MetaWearables.IsSessionStarted) MetaWearables.StopSession();
        }
    }
}
