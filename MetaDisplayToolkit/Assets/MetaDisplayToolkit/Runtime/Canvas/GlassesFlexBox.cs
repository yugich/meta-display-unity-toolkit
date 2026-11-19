#if METADISPLAY_UGUI
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


namespace MetaDisplay
{
    /// <summary>
    /// Container flexbox dos óculos. No Canvas, mantém um Horizontal/VerticalLayoutGroup sincronizado
    /// para o preview ficar parecido com o resultado nos óculos.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Óculos - Container (FlexBox)")]
    public class GlassesFlexBox : GlassesElement
    {
        [Header("Layout")]
        public GlassesDirection direction = GlassesDirection.Column;
        [Min(0)] public int gap = 12;
        [Tooltip("Alinhamento no eixo principal (vertical em coluna, horizontal em linha).")]
        public GlassesAlignment alignment = GlassesAlignment.Start;
        [Tooltip("Alinhamento no eixo cruzado.")]
        public GlassesAlignment crossAlignment = GlassesAlignment.Start;
        public bool wrap;

        [Header("Espaçamento interno")]
        [Min(0)] public int padding = 0;
        [Tooltip("Usa valores diferentes para cada lado.")]
        public bool perEdgePadding;
        [Min(0)] public int paddingTop, paddingBottom, paddingStart, paddingEnd;

        [Header("Aparência")]
        public GlassesBackground background = GlassesBackground.None;

        [Header("Toque")]
        [Tooltip("O container inteiro vira clicável nos óculos.")]
        public bool clickable;
        public GlassesScreen navigateTo;
        public UnityEvent onClick = new UnityEvent();

        [Header("Preview no Canvas")]
        [Tooltip("Mantém um LayoutGroup do Unity igual a este flexbox (só no Editor).")]
        public bool syncCanvasLayout = true;

        internal void InvokeClick()
        {
            onClick?.Invoke();
            if (navigateTo != null) navigateTo.Show();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!syncCanvasLayout || Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) GlassesCanvasPreview.SyncLayout(this);
            };
        }
#endif
    }
}
#endif
