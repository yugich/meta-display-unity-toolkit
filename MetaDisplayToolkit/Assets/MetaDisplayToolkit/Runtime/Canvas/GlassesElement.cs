#if METADISPLAY_UGUI
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


namespace MetaDisplay
{
    /// <summary>Base dos componentes de UI dos óculos: opções de filho de um flexbox.</summary>
    public abstract class GlassesElement : MonoBehaviour
    {
        [Header("Como filho de um container")]
        [Tooltip("Quanto cresce para ocupar espaço livre (0 = tamanho natural).")]
        public float flexGrow;
        [Tooltip("Quanto encolhe quando falta espaço.")]
        public float flexShrink;
        [Tooltip("Sobrescreve o alinhamento no eixo cruzado do container pai.")]
        public bool overrideAlignSelf;
        public GlassesAlignment alignSelf = GlassesAlignment.Start;

        internal void ApplyFlexChild(GlassesNode node)
        {
            node.FlexGrow = flexGrow;
            node.FlexShrink = flexShrink;
            if (overrideAlignSelf) node.AlignSelf = alignSelf;
        }

        /// <summary>Pede um reenvio da tela que contém este elemento (se ela estiver nos óculos).</summary>
        public void RefreshScreen()
        {
            var screen = GetComponentInParent<GlassesScreen>(true);
            if (screen != null) screen.Refresh();
        }
    }
}
#endif
