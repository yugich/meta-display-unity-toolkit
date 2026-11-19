#if METADISPLAY_UGUI
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>This object (and its children) only appears on the phone Canvas, never on the glasses.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses - Ignore")]
    public class GlassesIgnore : MonoBehaviour
    {
    }
}
#endif
