#if METADISPLAY_UGUI
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>Base of the glasses UI components: options as a child of a flexbox.</summary>
    public abstract class GlassesElement : MonoBehaviour
    {
        [Header("As a container child")]
        [Tooltip("How much it grows to fill free space (0 = natural size).")]
        public float flexGrow;
        [Tooltip("How much it shrinks when space is short.")]
        public float flexShrink;
        [Tooltip("Overrides the cross-axis alignment of the parent container.")]
        public bool overrideAlignSelf;
        public GlassesAlignment alignSelf = GlassesAlignment.Start;

        internal void ApplyFlexChild(GlassesNode node)
        {
            node.FlexGrow = flexGrow;
            node.FlexShrink = flexShrink;
            if (overrideAlignSelf) node.AlignSelf = alignSelf;
        }

        /// <summary>Requests a re-send of the screen that contains this element (if it is on the glasses).</summary>
        public void RefreshScreen()
        {
            var screen = GetComponentInParent<GlassesScreen>(true);
            if (screen != null) screen.Refresh();
        }
    }
}
#endif
