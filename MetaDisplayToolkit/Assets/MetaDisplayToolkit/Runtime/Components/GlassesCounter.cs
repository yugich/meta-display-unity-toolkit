#if METADISPLAY_UGUI
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetaDisplay
{
    /// <summary>
    /// Exemplo de estado dinâmico: escreve um número num texto e reenvia a tela dos óculos.
    /// Ligue um botão ao <see cref="Increment"/> pelo OnClick.
    /// </summary>
    [AddComponentMenu("Meta Display/Exemplo - Contador")]
    public class GlassesCounter : MonoBehaviour
    {
        [Tooltip("Objeto com TextMeshPro ou Text.")]
        public GameObject target;
        public string format = "Toques: {0}";
        public int value;

        void Start() => Write();

        public void Increment()
        {
            value++;
            Write();
        }

        public void ResetCounter()
        {
            value = 0;
            Write();
        }

        void Write()
        {
            var go = target != null ? target : gameObject;
            var text = string.Format(format, value);
            var tmp = go.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = text;
            var legacy = go.GetComponent<Text>();
            if (legacy != null) legacy.text = text;
            var screen = go.GetComponentInParent<GlassesScreen>(true);
            if (screen != null && Application.isPlaying) screen.Refresh();
        }
    }
}
#endif
