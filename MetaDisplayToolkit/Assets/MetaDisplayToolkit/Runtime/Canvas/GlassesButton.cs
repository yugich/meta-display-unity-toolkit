#if METADISPLAY_UGUI
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


namespace MetaDisplay
{
    /// <summary>
    /// Botão nos óculos. Funciona com (ou sem) um UnityEngine.UI.Button no mesmo objeto:
    /// o clique nos óculos dispara o OnClick do Button, o <see cref="onClick"/> e a navegação.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Óculos - Botão")]
    public class GlassesButton : GlassesElement
    {
        [Tooltip("Vazio = usa o primeiro texto filho.")]
        public string label;
        public GlassesButtonStyle style = GlassesButtonStyle.Primary;
        public bool showIcon;
        public GlassesIconName icon = GlassesIconName.Checkmark;
        [Tooltip("Ação principal: recebe o foco quando a tela aparece.")]
        public bool primary;
        [Tooltip("Tela dos óculos a mostrar ao tocar (navegação sem código).")]
        public GlassesScreen navigateTo;
        public UnityEvent onClick = new UnityEvent();
        [Tooltip("Ajusta as cores do botão no Canvas para parecer com os óculos (só no Editor).")]
        public bool previewStyle = true;

        Button m_UiButton;
        bool m_Hooked;

        void Awake() => Hook();

        void Hook()
        {
            if (m_Hooked) return;
            m_Hooked = true;
            m_UiButton = GetComponent<Button>();
            if (m_UiButton != null) m_UiButton.onClick.AddListener(HandleClick);
        }

        void HandleClick()
        {
            onClick?.Invoke();
            if (navigateTo != null) navigateTo.Show();
        }

        /// <summary>Executa o clique (é o que acontece quando o usuário toca no botão nos óculos).</summary>
        public void Click()
        {
            Hook();
            if (m_UiButton != null) m_UiButton.onClick.Invoke(); // inclui HandleClick
            else HandleClick();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!previewStyle || Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) GlassesCanvasPreview.StyleButton(this);
            };
        }
#endif
    }
}
#endif
