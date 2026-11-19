#if METADISPLAY_UGUI
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


namespace MetaDisplay
{
    /// <summary>Um dos 116 ícones nativos dos óculos.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Óculos - Ícone")]
    public class GlassesIcon : GlassesElement
    {
        public GlassesIconName icon = GlassesIconName.Star;
        public GlassesIconStyle style = GlassesIconStyle.Filled;
    }
}
#endif
