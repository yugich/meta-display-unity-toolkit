#if METADISPLAY_UGUI
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDisplay
{
    /// <summary>
    /// Uma tela dos óculos desenhada no Canvas (área de 600x600). Tudo que estiver dentro deste objeto
    /// vira UI dos óculos ao chamar <see cref="Show"/>. Escolha se ela também aparece no celular.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Meta Display/Tela dos Óculos (Glasses Screen)")]
    public class GlassesScreen : MonoBehaviour
    {
        public enum PhoneVisibility
        {
            [InspectorName("Só nos óculos (escondida no celular)")] GlassesOnly,
            [InspectorName("Óculos + espelho no celular")] Mirror,
            [InspectorName("Óculos + miniatura no canto do celular")] MirrorThumbnail,
        }

        public enum ContentKind
        {
            [InspectorName("Layout (elementos do Canvas)")] Layout,
            [InspectorName("Vídeo MP4 (URL HTTPS)")] Video,
        }

        [Tooltip("Nome para chamar por código: GlassesScreen.Find(\"home\").Show()")]
        public string screenId = "";
        [Tooltip("Onde esta tela aparece no Play/Build. Ela sempre é enviada aos óculos quando mostrada.")]
        public PhoneVisibility phoneVisibility = PhoneVisibility.GlassesOnly;
        public ContentKind content = ContentKind.Layout;

        [Tooltip("MP4 por HTTPS, até 400 px por lado e 70.000 px no total.")]
        public string videoUrl = "";
        [Tooltip("Tela a mostrar quando o vídeo terminar.")]
        public GlassesScreen nextScreenWhenVideoEnds;

        [Tooltip("Reenvia sozinha quando algum texto/elemento mudar (compara a cada intervalo).")]
        public bool autoRefresh = true;
        [Min(0.1f)] public float autoRefreshInterval = 0.5f;

        public UnityEvent onShown = new UnityEvent();

        static readonly List<GlassesScreen> s_All = new List<GlassesScreen>();

        /// <summary>Tela que está nos óculos agora.</summary>
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

        /// <summary>Converte o conteúdo do Canvas em UI dos óculos.</summary>
        public GlassesNode BuildNode() => GlassesCanvasConverter.Convert(this);

        /// <summary>JSON que seria enviado (útil para depurar).</summary>
        public string BuildJson() => GlassesJson.Serialize(BuildNode(), new GlassesSendContext { DryRun = true });

        /// <summary>Mostra esta tela nos óculos (e atualiza o espelho no celular).</summary>
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
            if (previous != null && previous != this) previous.ApplyPhoneVisibility();
            ApplyPhoneVisibility();
            onShown?.Invoke();
            CurrentChanged?.Invoke(this);
        }

        /// <summary>Reenvia se esta tela estiver nos óculos.</summary>
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

        /// <summary>Aplica a visibilidade no celular (no Editor fora do Play, tudo fica visível para editar).</summary>
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
                // Espelho: centraliza a tela atual (no Editor as telas podem estar lado a lado).
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
