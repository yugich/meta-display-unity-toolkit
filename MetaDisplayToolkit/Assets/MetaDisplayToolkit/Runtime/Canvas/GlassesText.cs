#if METADISPLAY_UGUI
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


namespace MetaDisplay
{
    /// <summary>Texto nos óculos. Lê o conteúdo de um TextMeshPro ou Text no mesmo objeto.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Óculos - Texto")]
    public class GlassesText : GlassesElement
    {
        public GlassesTextStyle style = GlassesTextStyle.Body;
        public GlassesTextColor color = GlassesTextColor.Primary;
        [Tooltip("Vazio = usa o texto do componente TextMeshPro/Text deste objeto.")]
        [TextArea] public string overrideText;
        [Tooltip("Ajusta tamanho/cor do texto no Canvas para parecer com os óculos (só no Editor).")]
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
