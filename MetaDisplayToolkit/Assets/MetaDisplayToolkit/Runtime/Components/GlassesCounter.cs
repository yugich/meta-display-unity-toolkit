#if METADISPLAY_UGUI
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetaDisplay
{
    /// <summary>
    /// Dynamic state example: writes a number into a text and re-sends the glasses screen.
    /// Hook a button's OnClick to <see cref="Increment"/>.
    /// </summary>
    [AddComponentMenu("Meta Display/Example - Counter")]
    public class GlassesCounter : MonoBehaviour
    {
        [Tooltip("Object with a TextMeshPro or Text.")]
        public GameObject target;
        public string format = "Taps: {0}";
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
