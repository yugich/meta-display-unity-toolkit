#if METADISPLAY_UGUI
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;


namespace MetaDisplay
{
    /// <summary>Imagem nos óculos: URL HTTPS, textura/sprite do Unity ou a imagem do componente Image/RawImage.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Óculos - Imagem")]
    public class GlassesImage : GlassesElement
    {
        public enum ImageSource
        {
            [InspectorName("Automático (Image/RawImage deste objeto)")] Auto,
            [InspectorName("Textura")] Texture,
            [InspectorName("URL HTTPS")] Url,
        }

        public ImageSource source = ImageSource.Auto;
        public Texture texture;
        public string url;
        public GlassesImageSize size = GlassesImageSize.Fill;
        public GlassesCornerRadius cornerRadius = GlassesCornerRadius.Medium;
        [Tooltip("Maior lado ao enviar (o display tem 600x600). 0 = automático.")]
        [Min(0)] public int maxSize;
    }
}
#endif
