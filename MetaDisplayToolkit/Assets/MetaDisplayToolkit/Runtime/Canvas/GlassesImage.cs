#if METADISPLAY_UGUI
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>Image on the glasses: HTTPS URL, Unity texture/sprite or the image of the Image/RawImage component.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses - Image")]
    public class GlassesImage : GlassesElement
    {
        public enum ImageSource
        {
            [InspectorName("Automatic (Image/RawImage on this object)")] Auto,
            [InspectorName("Texture")] Texture,
            [InspectorName("HTTPS URL")] Url,
        }

        public ImageSource source = ImageSource.Auto;
        public Texture texture;
        public string url;
        public GlassesImageSize size = GlassesImageSize.Fill;
        public GlassesCornerRadius cornerRadius = GlassesCornerRadius.Medium;
        [Tooltip("Largest side when sending (the display is 600x600). 0 = automatic.")]
        [Min(0)] public int maxSize;
    }
}
#endif
