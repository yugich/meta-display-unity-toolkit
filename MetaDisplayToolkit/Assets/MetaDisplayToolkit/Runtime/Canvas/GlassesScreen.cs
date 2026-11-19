#if METADISPLAY_UGUI
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDisplay
{
    /// <summary>
    /// A glasses screen drawn on the Canvas (a 600x600 area). Everything inside this object becomes
    /// glasses UI when <see cref="Show"/> is called. Choose whether it also appears on the phone.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Meta Display/Glasses Screen")]
    public class GlassesScreen : MonoBehaviour
    {
        public enum PhoneVisibility
        {
            [InspectorName("Glasses only (hidden on the phone)")] GlassesOnly,
            [InspectorName("Glasses + mirror on the phone")] Mirror,
            [InspectorName("Glasses + thumbnail in the phone corner")] MirrorThumbnail,
        }

        public enum ContentKind
        {
            [InspectorName("Layout (Canvas elements)")] Layout,
            [InspectorName("MP4 video (HTTPS URL)")] Video,
        }

        [Tooltip("Name used from code: GlassesScreen.Find(\"home\").Show()")]
        public string screenId = "";
        [Tooltip("Where this screen appears in Play/Build. It is always sent to the glasses when shown.")]
        public PhoneVisibility phoneVisibility = PhoneVisibility.GlassesOnly;
        public ContentKind content = ContentKind.Layout;

        [Tooltip("MP4 over HTTPS, up to 400 px per side and 70,000 px total.")]
        public string videoUrl = "";
        [Tooltip("Screen to show when the video ends.")]
        public GlassesScreen nextScreenWhenVideoEnds;

        [Tooltip("Re-sends automatically when any text/element changes (compared every interval).")]
        public bool autoRefresh = true;
        [Min(0.1f)] public float autoRefreshInterval = 0.5f;

        public UnityEvent onShown = new UnityEvent();

        static readonly List<GlassesScreen> s_All = new List<GlassesScreen>();

        /// <summary>Screen currently on the glasses.</summary>
        public static GlassesScreen Current { get; private set; }
        public static IReadOnlyList<GlassesScreen> All => s_All;
        public static event System.Action<GlassesScreen> CurrentChanged;

        public bool IsShowing => Current == this;

        CanvasGroup m_Group;
        string m_LastJson;
        float m_NextCheck;
        Vector3 m_OriginalScale;
        Vector2 m_OriginalPivot, m_OriginalAnchorMin, m_OriginalAnchorMax, m_OriginalPos;
        bool m_SavedRect;

        public static GlassesScreen Find(string id)
        {
            foreach (var s in s_All)
                if (s != null && s.screenId == id) return s;
            return null;
        }

        void OnEnable()
        {
            if (!s_All.Contains(this)) s_All.Add(this);
            ApplyPhoneVisibility();
        }

        void OnDisable() => s_All.Remove(this);

        void OnDestroy()
        {
            if (Current == this) Current = null;
            MetaGlassesDisplay.VideoStateChanged -= OnVideoState;
        }

        /// <summary>Converts the Canvas content into glasses UI.</summary>
        public GlassesNode BuildNode() => GlassesCanvasConverter.Convert(this);

        /// <summary>JSON that would be sent (useful for debugging).</summary>
        public string BuildJson() => GlassesJson.Serialize(BuildNode(), new GlassesSendContext { DryRun = true });

        /// <summary>Shows this screen on the glasses (and updates the phone mirror).</summary>
        public void Show()
        {
            var previous = Current;
            Current = this;
            if (content == ContentKind.Video)
            {
                MetaGlassesDisplay.VideoStateChanged -= OnVideoState;
                MetaGlassesDisplay.VideoStateChanged += OnVideoState;
                MetaGlassesDisplay.PlayVideo(videoUrl);
                m_LastJson = null;
            }
            else
            {
                var node = BuildNode();
                m_LastJson = GlassesJson.Serialize(node, new GlassesSendContext { DryRun = true });
                MetaGlassesDisplay.Show(node);
            }
            m_NextCheck = Time.unscaledTime + autoRefreshInterval;
            // Re-apply on every screen: before the first Show, the first screen may be visible as the default mirror.
            foreach (var screen in s_All)
                if (screen != null) screen.ApplyPhoneVisibility();
            if (previous != null && !s_All.Contains(previous)) previous.ApplyPhoneVisibility();
            onShown?.Invoke();
            CurrentChanged?.Invoke(this);
        }

        /// <summary>Re-sends if this screen is on the glasses.</summary>
        public void Refresh()
        {
            if (IsShowing && content == ContentKind.Layout) Show();
        }

        void OnVideoState(VideoState state)
        {
            if (!IsShowing || state != VideoState.Ended) return;
            MetaGlassesDisplay.VideoStateChanged -= OnVideoState;
            if (nextScreenWhenVideoEnds != null) nextScreenWhenVideoEnds.Show();
        }

        void Update()
        {
            if (!autoRefresh || !IsShowing || content != ContentKind.Layout || !MetaGlassesDisplay.IsReady) return;
            if (Time.unscaledTime < m_NextCheck) return;
            m_NextCheck = Time.unscaledTime + autoRefreshInterval;
            var json = BuildJson();
            if (json != m_LastJson) Show();
        }

        /// <summary>Applies the phone visibility (in the Editor outside Play, everything stays visible for editing).</summary>
        public void ApplyPhoneVisibility()
        {
            if (!Application.isPlaying) return;
            if (m_Group == null && !TryGetComponent(out m_Group)) m_Group = gameObject.AddComponent<CanvasGroup>();

            bool visible = phoneVisibility != PhoneVisibility.GlassesOnly && (IsShowing || Current == null && s_All.IndexOf(this) == 0);
            m_Group.alpha = visible ? 1f : 0f;
            m_Group.blocksRaycasts = visible;
            m_Group.interactable = visible;

            var rt = (RectTransform)transform;
            if (!m_SavedRect)
            {
                m_SavedRect = true;
                m_OriginalScale = rt.localScale;
                m_OriginalPivot = rt.pivot;
                m_OriginalAnchorMin = rt.anchorMin;
                m_OriginalAnchorMax = rt.anchorMax;
                m_OriginalPos = rt.anchoredPosition;
            }
            if (visible && phoneVisibility == PhoneVisibility.MirrorThumbnail)
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-16, -16);
                rt.localScale = m_OriginalScale * 0.4f;
            }
            else if (visible)
            {
                // Mirror: centers the current screen (in the Editor the screens may sit side by side).
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = m_OriginalScale;
            }
            else
            {
                rt.localScale = m_OriginalScale;
                rt.pivot = m_OriginalPivot;
                rt.anchorMin = m_OriginalAnchorMin;
                rt.anchorMax = m_OriginalAnchorMax;
                rt.anchoredPosition = m_OriginalPos;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_All.Clear();
            Current = null;
            CurrentChanged = null;
        }
    }
}
#endif
