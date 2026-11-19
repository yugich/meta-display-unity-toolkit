#if METADISPLAY_UGUI
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


namespace MetaDisplay
{
    /// <summary>Este objeto (e filhos) aparece só no Canvas do celular, nunca nos óculos.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Óculos - Ignorar")]
    public class GlassesIgnore : MonoBehaviour
    {
    }
}
#endif
