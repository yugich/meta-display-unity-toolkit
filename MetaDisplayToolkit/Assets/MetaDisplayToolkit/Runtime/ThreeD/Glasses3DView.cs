using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDisplay
{
    /// <summary>
    /// Shows a 3D object (e.g. an avatar) on the glasses. Pick the <see cref="strategy"/> in the Inspector or switch it
    /// at runtime to compare them: live frames, baked frames, MP4 clips or a Three.js Web App. The same calls
    /// (<see cref="Play"/>, <see cref="Say"/>, <see cref="Rotate"/>) work in every strategy.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses 3D View")]
    public class Glasses3DView : MonoBehaviour
    {
        [Serializable] public class ActionEvent : UnityEvent<string> { }

        // ── What ──
        [Tooltip("Object shown on the glasses (e.g. the avatar root). Used for framing, rotation and poses.")]
        public Transform target;
        [Tooltip("Camera that renders the object for the glasses. Empty = one is created and framed on the target.")]
        public Camera renderCamera;
        [Tooltip("Layers drawn by the auto-created camera (put the object on its own layer to hide the rest of the scene).")]
        public LayerMask cullingMask = ~0;
        [Tooltip("Black is transparent on the glasses (the display only adds light). Keep it black so only the object appears.")]
        public Color background = Color.black;

        // ── How ──
        public Glasses3DStrategy strategy = Glasses3DStrategy.LiveFrames;
        [Tooltip("Starts showing as soon as the glasses display is ready.")]
        public bool showWhenDisplayReady = true;
        [Tooltip("Action played at the start and after a one-shot action ends.")]
        public string idleAction = "idle";
        [Tooltip("Actions of the object. Used to bake frames and record clips. Empty = taken from IGlasses3DPoseSource.")]
        public List<Glasses3DPose> poses = new List<Glasses3DPose>();

        // ── Image (Live and Baked) ──
        [Tooltip("Side of the square image sent to the glasses (the display is 600x600). Smaller = more frames per second.")]
        [Range(64, 600)] public int resolution = 300;
        [Tooltip("Renders at 2x and downsamples: smoother edges for a small GPU cost.")]
        public bool supersample = true;
        public Glasses3DEncoding encoding = Glasses3DEncoding.DirectJpeg;
        [Tooltip("JPEG quality (Direct JPEG). Lower = smaller frames = faster.")]
        [Range(10, 100)] public int jpegQuality = 60;

        // ── Live frames ──
        public Glasses3DUpdateMode updateMode = Glasses3DUpdateMode.OnChange;
        [Tooltip("Upper limit. The Bluetooth link usually delivers less: check the metrics.")]
        [Range(0.5f, 30f)] public float maxFps = 10f;
        [Tooltip("Frames sent before waiting for a confirmation. 1 = always the newest frame, no queue.")]
        [Range(1, 3)] public int maxInFlight = 1;
        [Tooltip("Sensitivity of 'only when the image changes' (average brightness change per 16x16 block, 0-255).")]
        [Range(1, 64)] public int changeThreshold = 4;

        // ── Baked frames ──
        [Tooltip("Bakes every pose when the scene starts (otherwise the first time the Baked strategy is used).")]
        public bool bakeOnStart;

        // ── Video clips ──
        [Tooltip("Base HTTPS URL of the MP4 clips: <base>/<action>.mp4 (when the pose has no Video URL).")]
        public string videoBaseUrl = "";

        // ── Web App ──
        [Tooltip("Link to the Three.js Web App (Web App strategy).")]
        public GlassesWebAppLink webAppLink;

        // ── Layout on the glasses (Live and Baked) ──
        [Tooltip("Text below the object. Say(text) changes it for a while.")]
        public string caption = "";
        public GlassesTextStyle captionStyle = GlassesTextStyle.Body;
        [Tooltip("Buttons below the object; tapping one on the glasses plays its action.")]
        public List<Glasses3DButton> buttons = new List<Glasses3DButton>();

        // ── Editor ──
        [Tooltip("Editor Play Mode only: simulated Bluetooth speed in KB/s (0 = instant), to feel the frame rate before using the glasses.")]
        [Min(0)] public float editorLinkKBps;
        [Tooltip("Editor Play Mode only: simulated latency per send.")]
        [Min(0)] public float editorLinkLatencyMs;

        // ── Events ──
        [Tooltip("Every action played (from code, glasses buttons, inputs or the Web App).")]
        public ActionEvent onAction = new ActionEvent();

        /// <summary>Raised a few times per second while showing.</summary>
        public event Action<Glasses3DMetrics> MetricsChanged;
        /// <summary>Raised when baking finishes.</summary>
        public event Action Baked;

        /// <summary>Optional: build your own glasses layout around the image node (caption and buttons are then up to you).</summary>
        public Func<ImageNode, GlassesNode> LayoutBuilder;

        static readonly List<Glasses3DView> s_All = new List<Glasses3DView>();
        public static IReadOnlyList<Glasses3DView> All => s_All;

        public bool IsShowing { get; private set; }
        public bool IsBaking { get; private set; }
        public float BakeProgress { get; private set; }
        public string CurrentAction { get; private set; }
        public string CurrentCaption => m_Caption;
        public Glasses3DMetrics Metrics => m_Metrics;
        /// <summary>What the glasses camera sees (phone preview). Null before Start.</summary>
        public Texture Preview => m_Capture?.Target;
        public IGlasses3DPoseSource PoseSource => m_PoseSource;
        public Animator TargetAnimator => m_Animator;

        Glasses3DCapture m_Capture;
        IGlasses3DPoseSource m_PoseSource;
        IGlasses3DLookTarget m_LookTarget;
        Animator m_Animator;
        bool m_OwnsCamera;

        string m_Caption;
        float m_CaptionUntil;
        bool m_LayoutDirty;
        bool m_SendRequested;
        float m_NextSend;
        float m_TargetYaw, m_Yaw;
        bool m_RotationWarned;
        float m_ReturnToIdleAt;

        // Baked frames
        sealed class BakedClip
        {
            public readonly List<byte[]> Jpgs = new List<byte[]>();
            public readonly List<string> DataUris = new List<string>();
            public float Fps;
            public bool Loop;
        }
        readonly Dictionary<string, BakedClip> m_Baked = new Dictionary<string, BakedClip>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> m_RegisteredKeys = new HashSet<string>();
        float m_ActionStart;
        int m_BakedResolution, m_BakedQuality;
        int m_LastBakedFrame = -1;
        string m_LastBakedAction;
        Coroutine m_BakeRoutine;

        // Metrics
        struct Pending { public float Time; public int Bytes; }
        readonly Dictionary<int, Pending> m_InFlight = new Dictionary<int, Pending>();
        readonly Queue<float> m_Acks = new Queue<float>();
        Glasses3DMetrics m_Metrics;
        float m_LatencySum, m_BytesSum;
        int m_LatencyCount, m_BytesCount;
        float m_NextMetricsEvent;
        const float InFlightTimeout = 15f;

        // ───────────────────────── Lifecycle ─────────────────────────

        void Awake()
        {
            m_Caption = caption;
            ResolveTarget();
        }

        void OnEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
            MetaGlassesDisplay.Sent += OnSent;
            MetaGlassesDisplay.SendFailed += OnSendFailed;
            MetaGlassesDisplay.StateChanged += OnDisplayState;
            MetaGlassesDisplay.VideoStateChanged += OnVideoState;
            if (webAppLink != null) webAppLink.MessageReceived += OnWebAppMessage;
        }

        void OnDisable()
        {
            s_All.Remove(this);
            MetaGlassesDisplay.Sent -= OnSent;
            MetaGlassesDisplay.SendFailed -= OnSendFailed;
            MetaGlassesDisplay.StateChanged -= OnDisplayState;
            MetaGlassesDisplay.VideoStateChanged -= OnVideoState;
            if (webAppLink != null) webAppLink.MessageReceived -= OnWebAppMessage;
            if (IsShowing && strategy == Glasses3DStrategy.VideoClips) MetaGlassesDisplay.StopVideo();
            IsShowing = false;
        }

        void Start()
        {
            EnsureRendering();
            if (poses.Count == 0) FillPosesFromSource();
            ApplyEditorLink();
            if (!string.IsNullOrEmpty(idleAction)) PlayLocal(idleAction);
            CurrentAction = idleAction;
            if (bakeOnStart) Bake();
            if (showWhenDisplayReady && MetaGlassesDisplay.IsReady) Show();
        }

        void OnDestroy()
        {
            if (m_Capture != null)
            {
                if (renderCamera != null && renderCamera.targetTexture == m_Capture.Target) renderCamera.targetTexture = null;
                m_Capture.Dispose();
                m_Capture = null;
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying && m_Capture != null) ApplySettings();
        }

        void ResolveTarget()
        {
            if (target == null) return;
            m_PoseSource = target.GetComponentInChildren<IGlasses3DPoseSource>(true);
            m_LookTarget = target.GetComponentInChildren<IGlasses3DLookTarget>(true);
            m_Animator = target.GetComponentInChildren<Animator>(true);
        }

        /// <summary>Changes the object shown (re-reads its pose source, Animator and framing).</summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            m_TargetYaw = m_Yaw = 0f;
            ResolveTarget();
            if (m_OwnsCamera) FrameTarget();
            m_Baked.Clear();
            m_LayoutDirty = true;
        }

        void FillPosesFromSource()
        {
            if (m_PoseSource?.Actions == null) return;
            foreach (var action in m_PoseSource.Actions)
            {
                bool loop = action == idleAction || action == "talk" || action == "listen";
                poses.Add(new Glasses3DPose { name = action, frames = loop ? 8 : 10, fps = 6, loop = loop });
            }
        }

        void ApplyEditorLink()
        {
#if UNITY_EDITOR
            MetaEditorSimulation.LinkBytesPerSecond = editorLinkKBps * 1024f;
            MetaEditorSimulation.LinkLatencyMs = editorLinkLatencyMs;
#endif
        }

        void EnsureRendering()
        {
            if (m_Capture == null) m_Capture = new Glasses3DCapture();
            bool recreated = m_Capture.Ensure(resolution, supersample);
            if (renderCamera == null)
            {
                var go = new GameObject("Glasses 3D Camera");
                go.transform.SetParent(transform, false);
                renderCamera = go.AddComponent<Camera>();
                renderCamera.fieldOfView = 30f;
                renderCamera.cullingMask = cullingMask;
                m_OwnsCamera = true;
                FrameTarget();
            }
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = new Color(background.r, background.g, background.b, 1f);
            renderCamera.aspect = 1f;
            if (recreated || renderCamera.targetTexture != m_Capture.Target) renderCamera.targetTexture = m_Capture.Target;
        }

        /// <summary>Places the camera in front of the target so its whole bounds fit (auto-created camera).</summary>
        public void FrameTarget(float margin = 1.15f)
        {
            if (renderCamera == null || target == null) return;
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            float radius = bounds.extents.magnitude * margin;
            float halfFov = renderCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = radius / Mathf.Sin(halfFov);
            var forward = target.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            renderCamera.transform.position = bounds.center + forward.normalized * distance;
            renderCamera.transform.LookAt(bounds.center);
            renderCamera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            renderCamera.farClipPlane = distance + radius * 2f;
        }

        // ───────────────────────── Public API ─────────────────────────

        /// <summary>Starts showing on the glasses with the current strategy.</summary>
        public void Show()
        {
            EnsureRendering();
            IsShowing = true;
            m_LayoutDirty = true;
            m_SendRequested = true;
            m_LastBakedFrame = -1;
            m_Capture.InvalidateSignature();
            switch (strategy)
            {
                case Glasses3DStrategy.BakedFrames:
                    if (!IsBaking && !HasBaked(CurrentAction ?? idleAction)) Bake();
                    break;
                case Glasses3DStrategy.VideoClips:
                    PlayVideoFor(CurrentAction ?? idleAction);
                    break;
                case Glasses3DStrategy.WebApp:
                    if (webAppLink != null) webAppLink.Connect();
                    else Debug.LogWarning("[MetaDisplay] Glasses3DView: the Web App strategy needs a GlassesWebAppLink.", this);
                    break;
            }
        }

        /// <summary>Stops sending (the glasses keep the last image until another screen is sent).</summary>
        public void Hide()
        {
            if (IsShowing && strategy == Glasses3DStrategy.VideoClips) MetaGlassesDisplay.StopVideo();
            IsShowing = false;
        }

        /// <summary>Switches the strategy at runtime (to compare them on the glasses).</summary>
        public void SetStrategy(Glasses3DStrategy newStrategy)
        {
            if (newStrategy == strategy) return;
            bool wasShowing = IsShowing;
            Hide();
            strategy = newStrategy;
            ResetMetrics();
            if (wasShowing || (showWhenDisplayReady && MetaGlassesDisplay.IsReady)) Show();
        }

        /// <summary>
        /// Plays an action in every strategy: the phone object, the baked flipbook, the MP4 clip or the Web App.
        /// "rotate:30" rotates the object by 30 degrees.
        /// </summary>
        public void Play(string action)
        {
            if (string.IsNullOrEmpty(action)) return;
            if (action.StartsWith("rotate:", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(action.Substring(7), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var deg))
                    Rotate(deg);
                return;
            }
            CurrentAction = action;
            m_ActionStart = Time.unscaledTime;
            m_LastBakedFrame = -1;
            m_ReturnToIdleAt = 0f;
            PlayLocal(action);
            // The Web App does not use the DAT display: forward even when nothing is shown through DAT.
            if (strategy == Glasses3DStrategy.WebApp)
            {
                if (webAppLink != null) webAppLink.SendPlay(action);
            }
            else if (IsShowing)
            {
                if (strategy == Glasses3DStrategy.VideoClips) PlayVideoFor(action);
                else if (strategy == Glasses3DStrategy.BakedFrames && !HasBaked(action) && !IsBaking)
                    Debug.LogWarning($"[MetaDisplay] Glasses3DView: '{action}' is not baked. Add it to Poses and call Bake().", this);
            }
            onAction?.Invoke(action);
        }

        /// <summary>Plays an action (e.g. a looping "talk") for a while, then goes back to the idle action.</summary>
        public void PlayFor(string action, float seconds)
        {
            Play(action);
            if (seconds > 0f) m_ReturnToIdleAt = Time.unscaledTime + seconds;
        }

        /// <summary>Shows a text below the object (Live/Baked) or in the Web App. seconds = 0 keeps it.</summary>
        public void Say(string text, float seconds = 0f)
        {
            m_Caption = text ?? "";
            m_CaptionUntil = seconds > 0 ? Time.unscaledTime + seconds : 0f;
            m_LayoutDirty = true;
            if (strategy == Glasses3DStrategy.WebApp && webAppLink != null) webAppLink.SendSay(m_Caption, seconds);
        }

        /// <summary>Rotates the object around the vertical axis (Live frames on the glasses, Web App, and the phone).</summary>
        public void Rotate(float degrees)
        {
            m_TargetYaw += degrees;
            if (strategy == Glasses3DStrategy.WebApp && webAppLink != null) webAppLink.SendRotate(degrees);
            else if (IsShowing && strategy != Glasses3DStrategy.LiveFrames && !m_RotationWarned)
            {
                m_RotationWarned = true;
                Debug.Log("[MetaDisplay] Glasses3DView: rotation only changes the glasses image with Live frames or the Web App (baked frames and clips are fixed).", this);
            }
        }

        /// <summary>Makes the object look somewhere (IGlasses3DLookTarget), e.g. towards the wearer's head movement.</summary>
        public void Look(float yaw, float pitch)
        {
            m_LookTarget?.SetLook(yaw, pitch);
            if (strategy == Glasses3DStrategy.WebApp && webAppLink != null) webAppLink.SendLook(yaw, pitch);
        }

        /// <summary>Sends a frame now (Manual mode, or to force a refresh).</summary>
        public void SendFrameNow() => m_SendRequested = true;

        public bool HasBaked(string action) => !string.IsNullOrEmpty(action) && m_Baked.ContainsKey(action);

        public IReadOnlyList<string> Actions
        {
            get
            {
                var list = new List<string>();
                foreach (var p in poses)
                    if (!string.IsNullOrEmpty(p.name) && !list.Contains(p.name)) list.Add(p.name);
                if (m_PoseSource?.Actions != null)
                    foreach (var a in m_PoseSource.Actions)
                        if (!list.Contains(a)) list.Add(a);
                return list;
            }
        }

        public Glasses3DPose FindPose(string action)
        {
            foreach (var p in poses)
                if (string.Equals(p.name, action, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>
        /// Applies resolution, supersampling, quality and link settings changed at runtime (e.g. from the debug panel).
        /// Baked frames are re-baked when the resolution or the JPEG quality changed.
        /// </summary>
        public void ApplySettings()
        {
            EnsureRendering();
            ApplyEditorLink();
            m_Capture.InvalidateSignature();
            m_LayoutDirty = true;
            m_SendRequested = true;
            if (m_Baked.Count > 0 && (m_BakedResolution != resolution || m_BakedQuality != jpegQuality))
            {
                m_Baked.Clear();
                if (strategy == Glasses3DStrategy.BakedFrames) Bake();
            }
        }

        public void ResetMetrics()
        {
            m_InFlight.Clear();
            m_Acks.Clear();
            m_Metrics = new Glasses3DMetrics { Strategy = strategy };
            m_LatencySum = m_BytesSum = 0;
            m_LatencyCount = m_BytesCount = 0;
        }

        // ───────────────────────── Baking and recording ─────────────────────────

        /// <summary>Pre-renders every pose (Baked frames strategy). Runs over a few frames.</summary>
        public void Bake()
        {
            if (!isActiveAndEnabled) return;
            if (m_BakeRoutine != null) StopCoroutine(m_BakeRoutine);
            m_BakeRoutine = StartCoroutine(BakeRoutine());
        }

        IEnumerator BakeRoutine()
        {
            IsBaking = true;
            BakeProgress = 0f;
            EnsureRendering();
            m_Baked.Clear();
            m_RegisteredKeys.Clear();
            int total = 0;
            foreach (var p in poses) total += Mathf.Max(1, p.frames);
            int done = 0;
            foreach (var pose in poses)
            {
                if (pose == null || string.IsNullOrEmpty(pose.name)) continue;
                var clip = new BakedClip { Fps = pose.fps, Loop = pose.loop };
                var frames = RenderFrames(pose, resolution, resolution, Mathf.Max(1, pose.frames));
                foreach (var tex in frames)
                {
                    var jpg = tex.EncodeToJPG(jpegQuality);
                    clip.Jpgs.Add(jpg);
                    clip.DataUris.Add(Glasses3DCapture.ToDataUri(jpg));
                    Destroy(tex);
                }
                m_Baked[pose.name] = clip;
                done += frames.Count;
                BakeProgress = total > 0 ? (float)done / total : 1f;
                yield return null;
            }
            m_BakedResolution = resolution;
            m_BakedQuality = jpegQuality;
            IsBaking = false;
            BakeProgress = 1f;
            m_BakeRoutine = null;
            m_LastBakedFrame = -1;
            if (!string.IsNullOrEmpty(CurrentAction)) PlayLocal(CurrentAction);
            Baked?.Invoke();
        }

        /// <summary>
        /// Renders the frames of a pose synchronously (used by baking and by the MP4 recorder in the Editor).
        /// The caller owns the returned RGBA32 textures.
        /// </summary>
        public List<Texture2D> RenderFrames(Glasses3DPose pose, int width, int height, int frameCount)
        {
            if (m_PoseSource == null && m_Animator == null) ResolveTarget(); // Edit Mode: Awake has not run
            EnsureRendering();
            var result = new List<Texture2D>(frameCount);
            if (renderCamera == null) return result;
            int scale = supersample ? 2 : 1;
            var rt = RenderTexture.GetTemporary(width * scale, height * scale, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previousTarget = renderCamera.targetTexture;
            float previousAspect = renderCamera.aspect;
            float animatorSpeed = m_Animator != null ? m_Animator.speed : 1f;
            bool animatorEnabled = m_Animator != null && m_Animator.enabled;
            m_PoseSource?.SetFrozen(true);
            try
            {
                renderCamera.targetTexture = rt;
                renderCamera.aspect = (float)width / height;
                if (m_Animator != null)
                {
                    m_Animator.speed = 0f;
                    if (pose.clip != null) m_Animator.enabled = false;
                }
                for (int i = 0; i < frameCount; i++)
                {
                    float t = frameCount <= 1 ? 0f : pose.loop ? (float)i / frameCount : (float)i / (frameCount - 1);
                    ApplyPose(pose, t);
                    renderCamera.Render();
                    var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = pose.name + "_" + i };
                    Glasses3DCapture.ReadInto(rt, tex);
                    tex.Apply(false);
                    result.Add(tex);
                }
            }
            finally
            {
                renderCamera.targetTexture = previousTarget;
                renderCamera.aspect = previousAspect;
                RenderTexture.ReleaseTemporary(rt);
                if (m_Animator != null)
                {
                    m_Animator.speed = animatorSpeed;
                    m_Animator.enabled = animatorEnabled;
                }
                m_PoseSource?.SetFrozen(false);
            }
            return result;
        }

        void ApplyPose(Glasses3DPose pose, float t)
        {
            if (m_PoseSource != null)
            {
                m_PoseSource.ApplyPose(pose.name, t);
                return;
            }
            if (pose.clip != null && target != null)
            {
                pose.clip.SampleAnimation(target.gameObject, t * pose.clip.length);
                return;
            }
            if (m_Animator != null && !string.IsNullOrEmpty(pose.animatorState))
            {
                m_Animator.Play(pose.animatorState, 0, t);
                m_Animator.Update(0f);
            }
        }

        void PlayLocal(string action)
        {
            if (m_PoseSource != null)
            {
                m_PoseSource.Play(action);
                return;
            }
            var pose = FindPose(action);
            string state = pose != null && !string.IsNullOrEmpty(pose.animatorState) ? pose.animatorState : action;
            if (m_Animator != null && m_Animator.runtimeAnimatorController != null && m_Animator.HasState(0, Animator.StringToHash(state)))
                m_Animator.CrossFadeInFixedTime(state, 0.15f);
        }

        // ───────────────────────── Frame loop ─────────────────────────

        void Update()
        {
            UpdateRotation();
            if (m_ReturnToIdleAt > 0f && Time.unscaledTime >= m_ReturnToIdleAt)
            {
                m_ReturnToIdleAt = 0f;
                Play(idleAction);
            }
            if (m_CaptionUntil > 0 && Time.unscaledTime >= m_CaptionUntil)
            {
                m_CaptionUntil = 0;
                m_Caption = caption;
                m_LayoutDirty = true;
            }
            ExpireInFlight();

            if (IsShowing && MetaGlassesDisplay.IsReady)
            {
                switch (strategy)
                {
                    case Glasses3DStrategy.LiveFrames: TickLive(); break;
                    case Glasses3DStrategy.BakedFrames: TickBaked(); break;
                }
            }

            if (Time.unscaledTime >= m_NextMetricsEvent)
            {
                m_NextMetricsEvent = Time.unscaledTime + 0.25f;
                UpdateFps();
                m_Metrics.Strategy = strategy;
                m_Metrics.InFlight = m_InFlight.Count;
                MetricsChanged?.Invoke(m_Metrics);
            }
        }

        void UpdateRotation()
        {
            if (target == null || Mathf.Approximately(m_Yaw, m_TargetYaw)) return;
            float next = Mathf.MoveTowards(m_Yaw, m_TargetYaw, 360f * Time.unscaledDeltaTime);
            target.Rotate(0f, next - m_Yaw, 0f, Space.World);
            m_Yaw = next;
        }

        bool CanSendNow()
        {
            if (m_InFlight.Count >= maxInFlight) return false;
            return Time.unscaledTime >= m_NextSend || m_SendRequested;
        }

        void TickLive()
        {
            if (updateMode == Glasses3DUpdateMode.Manual && !m_SendRequested && !m_LayoutDirty) return;
            if (!CanSendNow()) return;

            float t0 = Time.realtimeSinceStartup;
            m_Capture.Read();
            bool force = m_SendRequested || m_LayoutDirty || updateMode != Glasses3DUpdateMode.OnChange;
            bool changed = m_Capture.Changed(changeThreshold);
            if (!changed && !force)
            {
                m_Metrics.FramesSkipped++;
                m_NextSend = Time.unscaledTime + 1f / maxFps;
                return;
            }
            var jpg = m_Capture.EncodeJpg(jpegQuality);
            m_Metrics.CaptureMs = (Time.realtimeSinceStartup - t0) * 1000f;
            SendFrame(jpg, encoding == Glasses3DEncoding.DirectJpeg ? Glasses3DCapture.ToDataUri(jpg) : null, "g3d_live");
        }

        void TickBaked()
        {
            if (IsBaking) return;
            string action = CurrentAction ?? idleAction;
            if (!m_Baked.TryGetValue(action, out var clip) || clip.Jpgs.Count == 0)
            {
                if (!string.IsNullOrEmpty(idleAction) && action != idleAction && m_Baked.ContainsKey(idleAction)) CurrentAction = idleAction;
                return;
            }
            // Frames are picked by time: if the link is slow, frames are skipped instead of slowing the animation down.
            int index = Mathf.FloorToInt((Time.unscaledTime - m_ActionStart) * Mathf.Max(0.1f, clip.Fps));
            if (clip.Loop) index %= clip.Jpgs.Count;
            else if (index >= clip.Jpgs.Count)
            {
                if (!string.IsNullOrEmpty(idleAction) && action != idleAction && m_Baked.ContainsKey(idleAction))
                {
                    CurrentAction = idleAction;
                    m_ActionStart = Time.unscaledTime;
                    m_LastBakedFrame = -1;
                    return;
                }
                index = clip.Jpgs.Count - 1;
            }
            bool sameFrame = index == m_LastBakedFrame && action == m_LastBakedAction;
            if (sameFrame && !m_LayoutDirty && !m_SendRequested) return;
            if (!CanSendNow()) return;
            m_LastBakedFrame = index;
            m_LastBakedAction = action;
            string key = "g3d_bake_" + action + "_" + index;
            if (encoding == Glasses3DEncoding.SdkBitmap && m_RegisteredKeys.Add(key))
                MetaGlassesDisplay.RegisterImage(key, clip.Jpgs[index], resolution);
            SendFrame(clip.Jpgs[index], clip.DataUris[index], key, registered: true);
        }

        void SendFrame(byte[] jpg, string dataUri, string key, bool registered = false)
        {
            var image = new ImageNode { Size = GlassesImageSize.Fill, CornerRadius = GlassesCornerRadius.None, FlexGrow = 1f, FlexShrink = 1f };
            if (encoding == Glasses3DEncoding.DirectJpeg) image.Url = dataUri ?? Glasses3DCapture.ToDataUri(jpg);
            else
            {
                if (!registered) MetaGlassesDisplay.RegisterImage(key, jpg, resolution);
                image.ImageKey = key;
            }
            var root = LayoutBuilder != null ? LayoutBuilder(image) : BuildLayout(image);
            int id = MetaGlassesDisplay.Show(root);
            m_InFlight[id] = new Pending { Time = Time.realtimeSinceStartup, Bytes = jpg.Length };
            m_Metrics.LastBytes = jpg.Length;
            m_BytesSum += jpg.Length;
            m_BytesCount++;
            m_Metrics.AvgBytes = m_BytesSum / m_BytesCount;
            m_NextSend = Time.unscaledTime + 1f / Mathf.Max(0.1f, maxFps);
            m_SendRequested = false;
            m_LayoutDirty = false;
        }

        GlassesNode BuildLayout(ImageNode image)
        {
            bool hasCaption = !string.IsNullOrEmpty(m_Caption);
            bool hasButtons = buttons != null && buttons.Count > 0;
            var root = Glasses.Column(hasCaption || hasButtons ? 8 : 0, 0, alignment: GlassesAlignment.Center, crossAlignment: GlassesAlignment.Center);
            root.Add(image);
            if (hasCaption) root.Add(Glasses.Text(m_Caption, captionStyle));
            if (hasButtons)
            {
                var group = new ButtonGroupNode();
                foreach (var b in buttons)
                {
                    if (b == null || string.IsNullOrEmpty(b.label)) continue;
                    var button = b;
                    group.Add(Glasses.Button(button.label, () => OnLayoutButton(button), button.style));
                }
                root.Add(group);
            }
            return root;
        }

        void OnLayoutButton(Glasses3DButton button)
        {
            button.onClick?.Invoke();
            if (!string.IsNullOrEmpty(button.action)) Play(button.action);
        }

        // ───────────────────────── Video clips ─────────────────────────

        string VideoUrlFor(string action)
        {
            var pose = FindPose(action);
            if (pose != null && !string.IsNullOrEmpty(pose.videoUrl)) return pose.videoUrl;
            if (string.IsNullOrEmpty(videoBaseUrl)) return null;
            return videoBaseUrl.TrimEnd('/') + "/" + action + ".mp4";
        }

        void PlayVideoFor(string action)
        {
            var url = VideoUrlFor(action);
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogWarning($"[MetaDisplay] Glasses3DView: no MP4 URL for '{action}'. Set Video base URL or the pose's Video URL.", this);
                return;
            }
            int id = MetaGlassesDisplay.PlayVideo(url);
            m_InFlight[id] = new Pending { Time = Time.realtimeSinceStartup, Bytes = 0 };
        }

        void OnVideoState(VideoState state)
        {
            if (!IsShowing || strategy != Glasses3DStrategy.VideoClips || state != VideoState.Ended) return;
            var action = CurrentAction ?? idleAction;
            var pose = FindPose(action);
            if (pose != null && pose.loop) PlayVideoFor(action);
            else if (!string.IsNullOrEmpty(idleAction))
            {
                CurrentAction = idleAction;
                PlayLocal(idleAction);
                PlayVideoFor(idleAction);
            }
        }

        // ───────────────────────── Events ─────────────────────────

        void OnDisplayState(DisplayState state)
        {
            if (state == DisplayState.Started)
            {
                m_InFlight.Clear();
                if (showWhenDisplayReady || IsShowing) Show();
            }
        }

        void OnSent(int id)
        {
            if (!m_InFlight.TryGetValue(id, out var pending)) return;
            m_InFlight.Remove(id);
            float now = Time.realtimeSinceStartup;
            m_Metrics.FramesSent++;
            m_Metrics.LastLatencyMs = (now - pending.Time) * 1000f;
            m_LatencySum += m_Metrics.LastLatencyMs;
            m_LatencyCount++;
            m_Metrics.AvgLatencyMs = m_LatencySum / m_LatencyCount;
            m_Acks.Enqueue(now);
        }

        void OnSendFailed(int id, MetaError error)
        {
            if (!m_InFlight.Remove(id)) return;
            m_Metrics.FramesFailed++;
        }

        void ExpireInFlight()
        {
            if (m_InFlight.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            List<int> expired = null;
            foreach (var kv in m_InFlight)
                if (now - kv.Value.Time > InFlightTimeout) (expired ??= new List<int>()).Add(kv.Key);
            if (expired == null) return;
            foreach (var id in expired)
            {
                m_InFlight.Remove(id);
                m_Metrics.FramesFailed++;
            }
        }

        void UpdateFps()
        {
            float now = Time.realtimeSinceStartup;
            while (m_Acks.Count > 0 && now - m_Acks.Peek() > 3f) m_Acks.Dequeue();
            m_Metrics.Fps = m_Acks.Count / 3f;
        }

        void OnWebAppMessage(GlassesWebAppLink.Message message)
        {
            // The wearer played an action in the Web App: mirror it on the phone object.
            if (message.type == "action" && !string.IsNullOrEmpty(message.name))
            {
                CurrentAction = message.name;
                PlayLocal(message.name);
                onAction?.Invoke(message.name);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_All.Clear();
    }
}
