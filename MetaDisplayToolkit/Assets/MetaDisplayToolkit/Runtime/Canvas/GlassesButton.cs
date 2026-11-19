#if METADISPLAY_UGUI
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MetaDisplay
{
    /// <summary>
    /// Button on the glasses. Works with (or without) a UnityEngine.UI.Button on the same object:
    /// a tap on the glasses fires the Button's OnClick, <see cref="onClick"/> and the navigation.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses - Button")]
    public class GlassesButton : GlassesElement
    {
        [Tooltip("Empty = uses the first child text.")]
        public string label;
        public GlassesButtonStyle style = GlassesButtonStyle.Primary;
        public bool showIcon;
        public GlassesIconName icon = GlassesIconName.Checkmark;
        [Tooltip("Primary action: gets the focus when the screen appears.")]
        public bool primary;
        [Tooltip("Glasses screen to show when tapped (no-code navigation).")]
        public GlassesScreen navigateTo;
        public UnityEvent onClick = new UnityEvent();
        [Tooltip("Adjusts the button colors on the Canvas to look like the glasses (Editor only).")]
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

        /// <summary>Runs the click (this is what happens when the user taps the button on the glasses).</summary>
        public void Click()
        {
            Hook();
            if (m_UiButton != null) m_UiButton.onClick.Invoke(); // includes HandleClick
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
