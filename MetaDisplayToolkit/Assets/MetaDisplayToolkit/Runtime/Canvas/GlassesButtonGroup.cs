#if METADISPLAY_UGUI
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


namespace MetaDisplay
{
    /// <summary>Agrupa os botões filhos (GlassesButton) com o espaçamento padrão dos óculos.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Óculos - Grupo de Botões")]
    public class GlassesButtonGroup : GlassesElement
    {
        public GlassesButtonGroupAlignment alignment = GlassesButtonGroupAlignment.Center;
    }
}
#endif
