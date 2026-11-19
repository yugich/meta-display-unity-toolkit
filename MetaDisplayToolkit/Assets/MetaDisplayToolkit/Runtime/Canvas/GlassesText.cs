#if METADISPLAY_UGUI
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>Text on the glasses. Reads the content of a TextMeshPro or Text on the same object.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses - Text")]
    public class GlassesText : GlassesElement
    {
        public GlassesTextStyle style = GlassesTextStyle.Body;
        public GlassesTextColor color = GlassesTextColor.Primary;
        [Tooltip("Empty = uses the text of the TextMeshPro/Text component on this object.")]
        [TextArea] public string overrideText;
        [Tooltip("Adjusts the text size/color on the Canvas to look like the glasses (Editor only).")]
        public bool previewStyle = true;

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!previewStyle || Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) GlassesCanvasPreview.StyleText(this);
            };
        }
#endif
    }
}
#endif
