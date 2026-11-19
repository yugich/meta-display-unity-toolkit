#if METADISPLAY_UGUI
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>One of the 116 native glasses icons.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses - Icon")]
    public class GlassesIcon : GlassesElement
    {
        public GlassesIconName icon = GlassesIconName.Star;
        public GlassesIconStyle style = GlassesIconStyle.Filled;
    }
}
#endif
