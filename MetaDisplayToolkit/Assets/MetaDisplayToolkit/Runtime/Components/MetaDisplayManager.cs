using System;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDisplay
{
    /// <summary>
    /// Put one in the scene and configure everything in the Inspector: connection, glasses screens, what
    /// appears on the phone, capabilities (camera, IMU, inputs, speech, voice), simulation and events.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-500)]
    [AddComponentMenu("Meta Display/Meta Display Manager")]
    public class MetaDisplayManager : MonoBehaviour
    {
        [Serializable] public class StringEvent : UnityEvent<string> { }

        // ── Connection ──
        [Tooltip("Initializes the SDK when the scene opens (requests the Bluetooth permission on Android 12+).")]
        public bool initializeOnStart = true;
        [Tooltip("Opens Meta AI to register the app automatically if it is not registered yet.\nRecommended: leave it off and use a button (the debug panel has one).")]
        public bool registerAutomatically;
        [Tooltip("Connects to the glasses as soon as the app is registered.")]
        public bool connectWhenRegistered = true;
        public DeviceSelection deviceSelection = DeviceSelection.DisplayCapable;
        public string specificDeviceId = "";

        // ── Glasses ──
        [Tooltip("Attaches the glasses Display when connecting.")]
        public bool enableDisplay = true;
#if METADISPLAY_UGUI
        [Tooltip("Screen sent to the glasses when the display is ready.")]
        public GlassesScreen initialScreen;
        [Tooltip("When reconnecting, re-sends the screen that was open (instead of going back to the initial one).")]
        public bool restoreCurrentScreen = true;
#endif

        // ── Capabilities ──
        public bool startCamera;
        public CameraQuality cameraQuality = CameraQuality.Medium;
        public CameraFps cameraFps = CameraFps.Fps24;
        public CameraAudio cameraAudio = CameraAudio.None;
        public bool startMotion;
        public MotionRate motionRate = MotionRate.Hz30;
        public bool startInputs;
        [Tooltip("The app receives the Back gesture instead of the system.")]
        public bool inputsConsumeBack = true;
        public bool startSpeech;
        public bool startVoiceInvocations;

        // ── Phone ──
        [Tooltip("Panel on the phone screen with status and buttons (register, connect, screens, camera, sensors...).")]
        public bool showPhoneDebugPanel = true;

        // ── Testing ──
        [Tooltip("In the Editor (Play Mode), simulates connected glasses to test the flow and the screens.")]
        public bool simulateInEditor = true;
        [Tooltip("On the phone, uses SIMULATED glasses (MockDeviceKit) instead of the real ones.")]
        public bool useMockDevice;
        public bool mockShowDisplayPreview = true;
        public int mockPreviewSize = 480;
        public ScreenCorner mockPreviewCorner = ScreenCorner.TopRight;

        // ── Events ──
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
                Debug.LogWarning("[MetaDisplay] There is already a MetaDisplayManager in the scene; this one will be ignored.", this);
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
            // Scene loaded while already connected: show this scene's initial screen right away.
            if (MetaGlassesDisplay.IsReady) OnDisplay(DisplayState.Started);
        }

        /// <summary>Connects to the glasses and requests the capabilities checked in the Inspector.</summary>
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
