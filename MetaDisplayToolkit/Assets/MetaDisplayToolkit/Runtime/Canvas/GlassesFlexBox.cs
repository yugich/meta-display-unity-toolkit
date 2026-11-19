#if METADISPLAY_UGUI
using UnityEngine;
using UnityEngine.Events;

namespace MetaDisplay
{
    /// <summary>
    /// Glasses flexbox container. On the Canvas, keeps a Horizontal/VerticalLayoutGroup in sync
    /// so the preview looks like the result on the glasses.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses - Container (FlexBox)")]
    public class GlassesFlexBox : GlassesElement
    {
        [Header("Layout")]
        public GlassesDirection direction = GlassesDirection.Column;
        [Min(0)] public int gap = 12;
        [Tooltip("Main-axis alignment (vertical in a column, horizontal in a row).")]
        public GlassesAlignment alignment = GlassesAlignment.Start;
        [Tooltip("Cross-axis alignment.")]
        public GlassesAlignment crossAlignment = GlassesAlignment.Start;
        public bool wrap;

        [Header("Padding")]
        [Min(0)] public int padding = 0;
        [Tooltip("Uses a different value for each side.")]
        public bool perEdgePadding;
        [Min(0)] public int paddingTop, paddingBottom, paddingStart, paddingEnd;

        [Header("Appearance")]
        public GlassesBackground background = GlassesBackground.None;

        [Header("Tap")]
        [Tooltip("The whole container becomes clickable on the glasses.")]
        public bool clickable;
        public GlassesScreen navigateTo;
        public UnityEvent onClick = new UnityEvent();

        [Header("Canvas preview")]
        [Tooltip("Keeps a Unity LayoutGroup matching this flexbox (Editor only).")]
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
