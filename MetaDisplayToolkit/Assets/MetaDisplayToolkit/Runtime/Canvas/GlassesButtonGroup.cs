#if METADISPLAY_UGUI
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>Groups the child buttons (GlassesButton) with the glasses' default spacing.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses - Button Group")]
    public class GlassesButtonGroup : GlassesElement
    {
        public GlassesButtonGroupAlignment alignment = GlassesButtonGroupAlignment.Center;
    }
}
#endif
