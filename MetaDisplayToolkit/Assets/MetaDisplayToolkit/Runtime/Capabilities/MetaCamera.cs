using System;
using System.Collections.Generic;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Câmera dos óculos: vídeo ao vivo como <see cref="Texture"/> (RenderTexture RGB atualizada a cada frame),
    /// fotos e áudio do microfone junto com o vídeo. Pede a permissão de câmera no Meta AI automaticamente.
    /// O vídeo vem por Bluetooth: resoluções/fps menores costumam dar imagem mais nítida.
    /// </summary>
    public static class MetaCamera
    {
        const string Cls = "MDCamera";

        static bool s_Active;
        static int s_LastCounter = -1;
        static Texture2D s_Y, s_U, s_V, s_UV;
        static Material s_Material;
        static int s_NextPhoto = 1;
        static readonly Dictionary<int, PhotoRequest> s_Photos = new Dictionary<int, PhotoRequest>();
        static AndroidJavaClass s_Class;

        struct PhotoRequest
        {
            public Action<Texture2D> OnPhoto;
            public Action<MetaError> OnError;
            public Action<float> OnProgress;
        }

        public static CameraStreamState State { get; private set; } = CameraStreamState.Stopped;
        public static bool IsStreaming => State == CameraStreamState.Streaming;
        /// <summary>Último frame em RGB (RenderTexture). Use em RawImage, material, etc.</summary>
        public static RenderTexture Texture { get; private set; }
        public static int FrameWidth { get; private set; }
        public static int FrameHeight { get; private set; }
        public static long FramesReceived { get; private set; }
        /// <summary>Layout YUV forçado. Auto detecta; troque para NV21 se as cores vierem trocadas (azul/laranja).</summary>
        public static YuvLayout LayoutOverride = YuvLayout.Auto;
        public static YuvLayout DetectedLayout { get; private set; } = YuvLayout.Auto;
        public static int AudioSampleRate { get; private set; }
        public static int AudioChannels { get; private set; } = 1;

        public static event Action<CameraStreamState> StateChanged;
        /// <summary>Novo frame disponível em <see cref="Texture"/>.</summary>
        public static event Action<RenderTexture> FrameReady;
        /// <summary>Amostras PCM em float (-1..1), intercaladas por canal: (amostras, sampleRate, canais).</summary>
        public static event Action<float[], int, int> AudioReceived;
        /// <summary>Mensagens informativas da câmera (ex.: aguardando foto HQ, reiniciando stream sem frames).</summary>
        public static event Action<string> Info;

        /// <summary>Inicia o vídeo (e, opcionalmente, o áudio). Pode ser chamado antes da sessão começar.</summary>
        public static void Start(CameraQuality quality = CameraQuality.Medium, CameraFps fps = CameraFps.Fps24,
            CameraAudio audio = CameraAudio.None, int audioChannels = 1)
        {
            MetaDispatcher.EnsureExists();
            Activate();
            AudioSampleRate = (int)audio;
            AudioChannels = Mathf.Clamp(audioChannels, 1, 2);
            MetaNative.Call(Cls, "startStream", MetaEnum.ToSdk(quality), (int)fps, (int)audio, AudioChannels);
        }

        public static void Stop()
        {
            MetaNative.Call(Cls, "stopStream");
            Deactivate();
        }

        /// <summary>Foto rápida durante o stream ativo.</summary>
        public static void CapturePhoto(Action<Texture2D> onPhoto, Action<MetaError> onError = null)
        {
            int id = s_NextPhoto++;
            s_Photos[id] = new PhotoRequest { OnPhoto = onPhoto, OnError = onError };
            MetaNative.Call(Cls, "capturePhoto", id);
        }

        /// <summary>
        /// Foto standalone em alta qualidade (experimental). Pausa o stream, captura e religa.
        /// </summary>
        public static void CaptureHighQualityPhoto(Action<Texture2D> onPhoto, Action<MetaError> onError = null,
            PhotoResolution resolution = PhotoResolution.Full, PhotoQuality quality = PhotoQuality.High, Action<float> onProgress = null)
        {
            Activate();
            int id = s_NextPhoto++;
            s_Photos[id] = new PhotoRequest { OnPhoto = onPhoto, OnError = onError, OnProgress = onProgress };
            MetaNative.Call(Cls, "captureHighQualityPhoto", MetaEnum.ToSdk(resolution), MetaEnum.ToSdk(quality), id);
        }

        /// <summary>Frame atual como JPEG (ex.: para mandar a uma API de visão). Null se não houver frame.</summary>
        public static byte[] GetFrameJpeg(int quality = 85) =>
            MetaNative.FromJava(MetaNative.Call<sbyte[]>(Cls, "frameAsJpeg", quality));

        // ───────────── polling (main thread) ─────────────

        static void Activate()
        {
            if (s_Active) return;
            s_Active = true;
            MetaDispatcher.Tick += Poll;
        }

        static void Deactivate()
        {
            if (!s_Active) return;
            s_Active = false;
            MetaDispatcher.Tick -= Poll;
        }

        static void Poll()
        {
            if (!MetaNative.IsNative) return;
            PollFrame();
            if (AudioSampleRate > 0) PollAudio();
        }

        static void PollFrame()
        {
            int counter = MetaNative.Call<int>(Cls, "frameCounter");
            if (counter == s_LastCounter || counter == 0) return;
            var info = MetaNative.Call<int[]>(Cls, "frameInfo"); // w, h, size, counter, layout
            if (info == null || info.Length < 5 || info[0] <= 0) return;
            int w = info[0], h = info[1], size = info[2];
            DetectedLayout = (YuvLayout)info[4];
            var layout = LayoutOverride != YuvLayout.Auto ? LayoutOverride
                : DetectedLayout == YuvLayout.Auto ? YuvLayout.I420 : DetectedLayout;

            if (s_Class == null) s_Class = new AndroidJavaClass(MetaNative.Package + Cls);
            using (var buffer = s_Class.CallStatic<AndroidJavaObject>("acquireFrame"))
            {
                if (buffer == null) return;
                try
                {
                    IntPtr address = DirectAddress(buffer.GetRawObject());
                    if (address == IntPtr.Zero || size < w * h * 3 / 2) return;
                    Upload(address, w, h, layout);
                }
                finally
                {
                    s_Class.CallStatic("releaseFrame");
                }
            }
            s_LastCounter = info[3];
            FramesReceived++;
            FrameReady?.Invoke(Texture);
        }

        static unsafe IntPtr DirectAddress(IntPtr javaBuffer) => (IntPtr)AndroidJNI.GetDirectBufferAddress(javaBuffer);

        static void Upload(IntPtr data, int w, int h, YuvLayout layout)
        {
            int cw = w / 2, ch = h / 2;
            EnsureTexture(ref s_Y, w, h, TextureFormat.R8);
            s_Y.LoadRawTextureData(data, w * h);
            s_Y.Apply(false);

            if (layout == YuvLayout.I420)
            {
                EnsureTexture(ref s_U, cw, ch, TextureFormat.R8);
                EnsureTexture(ref s_V, cw, ch, TextureFormat.R8);
                s_U.LoadRawTextureData(data + w * h, cw * ch);
                s_V.LoadRawTextureData(data + w * h + cw * ch, cw * ch);
                s_U.Apply(false);
                s_V.Apply(false);
            }
            else
            {
                EnsureTexture(ref s_UV, cw, ch, TextureFormat.RG16);
                s_UV.LoadRawTextureData(data + w * h, cw * ch * 2);
                s_UV.Apply(false);
            }

            if (Texture == null || Texture.width != w || Texture.height != h)
            {
                if (Texture != null) Texture.Release();
                Texture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "MetaCamera" };
                Texture.Create();
                FrameWidth = w;
                FrameHeight = h;
            }

            if (s_Material == null)
            {
                var shader = Resources.Load<Shader>("MetaDisplay/YuvToRgb");
                if (shader == null)
                {
                    Debug.LogError("[MetaDisplay] Shader Resources/MetaDisplay/YuvToRgb não encontrado.");
                    return;
                }
                s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            s_Material.SetTexture("_UTex", s_U);
            s_Material.SetTexture("_VTex", s_V);
            s_Material.SetTexture("_UVTex", s_UV);
            s_Material.SetFloat("_Layout", (float)(int)layout);
            Graphics.Blit(s_Y, Texture, s_Material);
        }

        static void EnsureTexture(ref Texture2D tex, int w, int h, TextureFormat format)
        {
            if (tex != null && tex.width == w && tex.height == h && tex.format == format) return;
            if (tex != null) UnityEngine.Object.Destroy(tex);
            tex = new Texture2D(w, h, format, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        }

        static void PollAudio()
        {
            var raw = MetaNative.Call<sbyte[]>(Cls, "drainAudio");
            if (raw == null || raw.Length < 2 || AudioReceived == null) return;
            int count = raw.Length / 2;
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                int lo = (byte)raw[i * 2];
                int hi = raw[i * 2 + 1];
                samples[i] = (short)((hi << 8) | lo) / 32768f;
            }
            AudioReceived(samples, AudioSampleRate, AudioChannels);
        }

        // ───────────── eventos ─────────────

        internal static void Handle(string type, MetaPayload p)
        {
            switch (type)
            {
                case "camera.state":
                    State = MetaEnum.Parse(p.state, CameraStreamState.Stopped);
                    StateChanged?.Invoke(State);
                    break;
                case "camera.error":
                    MetaWearables.RaiseError(new MetaError("camera", p.error, p.message));
                    break;
                case "camera.info":
                    Debug.Log("[MetaDisplay] Câmera: " + p.message);
                    Info?.Invoke(p.message);
                    break;
                case "camera.photoProgress":
                    if (s_Photos.TryGetValue(p.requestId, out var progressReq)) progressReq.OnProgress?.Invoke(p.fraction);
                    break;
                case "camera.photo":
                    if (s_Photos.TryGetValue(p.requestId, out var req))
                    {
                        s_Photos.Remove(p.requestId);
                        var bytes = MetaNative.FromJava(MetaNative.Call<sbyte[]>(Cls, "takePhoto", p.requestId));
                        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                        if (bytes != null && tex.LoadImage(bytes)) req.OnPhoto?.Invoke(tex);
                        else req.OnError?.Invoke(new MetaError("camera", "DECODE_FAILED", "Não foi possível ler a foto"));
                    }
                    break;
                case "camera.photoFailed":
                {
                    var err = new MetaError("camera", p.error, p.message);
                    if (s_Photos.TryGetValue(p.requestId, out var failed))
                    {
                        s_Photos.Remove(p.requestId);
                        if (failed.OnError != null) failed.OnError(err);
                        else MetaWearables.RaiseError(err);
                    }
                    else MetaWearables.RaiseError(err);
                    break;
                }
            }
        }
    }
}
