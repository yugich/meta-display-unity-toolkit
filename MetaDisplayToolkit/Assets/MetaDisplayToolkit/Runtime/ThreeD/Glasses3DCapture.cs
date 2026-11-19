using System;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Render target of a <see cref="Glasses3DView"/>: the camera draws into <see cref="Target"/>; frames are read
    /// back, compared (to skip unchanged ones) and encoded as JPEG.
    /// </summary>
    internal sealed class Glasses3DCapture : IDisposable
    {
        const int Grid = 16;

        public RenderTexture Target { get; private set; }
        public int Size { get; private set; }
        public int RenderScale { get; private set; } = 1;

        Texture2D m_Read;
        readonly int[] m_Signature = new int[Grid * Grid];
        readonly int[] m_Current = new int[Grid * Grid];
        bool m_HasSignature;

        /// <summary>(Re)creates the textures when the size or the supersampling changes. Returns true if they changed.</summary>
        public bool Ensure(int size, bool supersample)
        {
            int scale = supersample && size * 2 <= 2048 ? 2 : 1;
            if (Target != null && Size == size && RenderScale == scale && Target.IsCreated()) return false;
            Dispose();
            Size = size;
            RenderScale = scale;
            Target = new RenderTexture(size * scale, size * scale, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Glasses3D Render",
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            Target.Create();
            m_Read = new Texture2D(size, size, TextureFormat.RGB24, false) { name = "Glasses3D Readback", hideFlags = HideFlags.DontSave };
            m_HasSignature = false;
            return true;
        }

        /// <summary>Copies the last render of <paramref name="source"/> (default: <see cref="Target"/>) to the CPU.</summary>
        public void Read(RenderTexture source = null)
        {
            source = source != null ? source : Target;
            ReadInto(source, m_Read);
        }

        /// <summary>Reads a render texture into a CPU texture of the same or smaller size (downsampling on the GPU).</summary>
        public static void ReadInto(RenderTexture source, Texture2D destination)
        {
            int w = destination.width, h = destination.height;
            var previous = RenderTexture.active;
            RenderTexture temp = null;
            try
            {
                var src = source;
                if (source.width != w || source.height != h)
                {
                    temp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(source, temp);
                    src = temp;
                }
                RenderTexture.active = src;
                destination.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            }
            finally
            {
                RenderTexture.active = previous;
                if (temp != null) RenderTexture.ReleaseTemporary(temp);
            }
        }

        /// <summary>
        /// Compares the last read with the last accepted one (16x16 average luminance blocks).
        /// Returns true and stores the new signature when any block changed more than <paramref name="threshold"/>.
        /// </summary>
        public bool Changed(int threshold)
        {
            var data = m_Read.GetRawTextureData<byte>();
            int size = Size;
            Array.Clear(m_Current, 0, m_Current.Length);
            int cell = Mathf.Max(1, size / Grid);
            for (int y = 0; y < size; y++)
            {
                int gy = Mathf.Min(Grid - 1, y / cell);
                int row = y * size * 3;
                for (int x = 0; x < size; x++)
                {
                    int i = row + x * 3;
                    // Integer luma approximation (BT.601).
                    int luma = (data[i] * 77 + data[i + 1] * 150 + data[i + 2] * 29) >> 8;
                    m_Current[gy * Grid + Mathf.Min(Grid - 1, x / cell)] += luma;
                }
            }
            int area = cell * cell;
            bool changed = !m_HasSignature;
            for (int i = 0; i < m_Current.Length; i++)
            {
                m_Current[i] /= area;
                if (!changed && Mathf.Abs(m_Current[i] - m_Signature[i]) > threshold) changed = true;
            }
            if (changed)
            {
                Array.Copy(m_Current, m_Signature, m_Current.Length);
                m_HasSignature = true;
            }
            return changed;
        }

        /// <summary>Forgets the last signature: the next <see cref="Changed"/> returns true.</summary>
        public void InvalidateSignature() => m_HasSignature = false;

        public byte[] EncodeJpg(int quality) => m_Read.EncodeToJPG(Mathf.Clamp(quality, 1, 100));

        public static string ToDataUri(byte[] jpg) => "data:image/jpeg;base64," + Convert.ToBase64String(jpg);

        public void Dispose()
        {
            if (Target != null)
            {
                Target.Release();
                Destroy(Target);
                Target = null;
            }
            if (m_Read != null)
            {
                Destroy(m_Read);
                m_Read = null;
            }
        }

        static void Destroy(UnityEngine.Object obj)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
