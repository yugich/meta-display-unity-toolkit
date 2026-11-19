using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Display do Meta Ray-Ban Display: envia telas, recebe cliques, mostra imagens e vídeos.
    /// Lembre: cada envio substitui a tela inteira (600x600), o display dorme após ~25 s sem interação
    /// e dois toques com dois dedos na haste encerram a sessão na tela raiz.
    /// </summary>
    public static class MetaGlassesDisplay
    {
        static Dictionary<string, Action> s_Clicks = new Dictionary<string, Action>();
        static readonly HashSet<string> s_UploadedImages = new HashSet<string>();
        static int s_NextRequest = 1;

        public static DisplayState State { get; private set; } = DisplayState.Stopped;
        public static bool IsReady => State == DisplayState.Started;
        /// <summary>O app pediu o Display (ele é anexado assim que a sessão começar).</summary>
        public static bool IsEnabled { get; private set; }
        public static VideoState Video { get; private set; } = VideoState.Idle;
        /// <summary>JSON da última tela enviada (útil para depurar).</summary>
        public static string LastLayoutJson { get; private set; }

        public static event Action<DisplayState> StateChanged;
        public static event Action<int> Sent;
        public static event Action<int, MetaError> SendFailed;
        /// <summary>Id do elemento clicado (os callbacks do nó já são chamados automaticamente).</summary>
        public static event Action<string> Clicked;
        public static event Action<VideoState> VideoStateChanged;

        /// <summary>Pede o Display. Pode ser chamado antes da sessão começar.</summary>
        public static void Enable()
        {
            IsEnabled = true;
            MetaNative.Call("MDDisplay", "enable");
        }

        public static void Disable()
        {
            IsEnabled = false;
            MetaNative.Call("MDDisplay", "disable");
        }

        /// <summary>Envia uma tela. Retorna o id do pedido (ver <see cref="Sent"/> e <see cref="SendFailed"/>).</summary>
        public static int Show(GlassesNode root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var ctx = new GlassesSendContext();
            var json = GlassesJson.Serialize(root, ctx);
            s_Clicks = ctx.Clicks;
            return ShowJson(json);
        }

        /// <summary>Envia um layout já serializado (formato do GlassesNode).</summary>
        public static int ShowJson(string layoutJson)
        {
            LastLayoutJson = layoutJson;
            int id = s_NextRequest++;
            MetaNative.Call("MDDisplay", "sendLayout", layoutJson, id);
            return id;
        }

        /// <summary>Reenvia a última tela (ex.: depois que o display acordou ou reconectou).</summary>
        public static int Resend() => string.IsNullOrEmpty(LastLayoutJson) ? -1 : ShowJson(LastLayoutJson);

        public static void Clear()
        {
            LastLayoutJson = null;
            MetaNative.Call("MDDisplay", "clear");
        }

        // ───────────── vídeo ─────────────

        /// <summary>Toca um MP4 por URL HTTPS (até 400 px por lado e 70.000 px). O vídeo ocupa a tela inteira.</summary>
        public static int PlayVideo(string httpsUrl)
        {
            int id = s_NextRequest++;
            MetaNative.Call("MDDisplay", "playVideo", httpsUrl, id);
            return id;
        }

        public static void StopVideo() => MetaNative.Call("MDDisplay", "stopVideo");

        // ───────────── imagens ─────────────

        // Id estável por objeto, sem GetInstanceID (obsoleto no Unity 6.6) nem GetEntityId (inexistente no 6.0).
        sealed class ObjectId { public string Value; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UnityEngine.Object, ObjectId> s_Ids =
            new System.Runtime.CompilerServices.ConditionalWeakTable<UnityEngine.Object, ObjectId>();
        static int s_NextObjectId;

        static string IdOf(UnityEngine.Object obj) =>
            s_Ids.GetValue(obj, _ => new ObjectId { Value = (++s_NextObjectId).ToString(CultureInfo.InvariantCulture) }).Value;

        public static string ImageKeyFor(Texture texture, int maxSize) =>
            texture == null ? null : "tex_" + IdOf(texture) + "_" + maxSize;

        public static string ImageKeyFor(Sprite sprite, int maxSize) =>
            sprite == null ? null : "spr_" + IdOf(sprite) + "_" + maxSize;

        /// <summary>Registra (uma vez) uma textura e devolve a chave. RenderTextures são reenviadas sempre.</summary>
        public static string RegisterImage(Texture texture, int maxSize = 600)
        {
            if (texture == null) return null;
            // Texture2D é estática (envia uma vez); RenderTexture/WebCam/vídeo mudam e são reenviadas.
            // (Não citar WebCamTexture aqui: o Unity adicionaria a permissão CAMERA do celular ao APK.)
            bool dynamic = !(texture is Texture2D);
            string key = ImageKeyFor(texture, maxSize);
            if (!dynamic && s_UploadedImages.Contains(key)) return key;
            var png = GlassesTextureEncoder.Encode(texture, null, maxSize);
            if (png == null) return null;
            RegisterImage(key, png, maxSize);
            return key;
        }

        public static string RegisterImage(Sprite sprite, int maxSize = 600)
        {
            if (sprite == null) return null;
            string key = ImageKeyFor(sprite, maxSize);
            if (s_UploadedImages.Contains(key)) return key;
            var png = GlassesTextureEncoder.Encode(sprite.texture, sprite.textureRect, maxSize);
            if (png == null) return null;
            RegisterImage(key, png, maxSize);
            return key;
        }

        /// <summary>Registra bytes PNG/JPG sob uma chave (use em <see cref="ImageNode.ImageKey"/>).</summary>
        public static void RegisterImage(string key, byte[] pngOrJpg, int maxSize = 600)
        {
            if (string.IsNullOrEmpty(key) || pngOrJpg == null) return;
            s_UploadedImages.Add(key);
            MetaNative.Call("MDDisplay", "setImage", key, MetaNative.ToJava(pngOrJpg), maxSize);
        }

        public static void ForgetImage(string key)
        {
            s_UploadedImages.Remove(key);
            MetaNative.Call("MDDisplay", "removeImage", key);
        }

        public static void ClearImages()
        {
            s_UploadedImages.Clear();
            MetaNative.Call("MDDisplay", "clearImages");
        }

        // ───────────── eventos ─────────────

        internal static void Handle(string type, MetaPayload p)
        {
            switch (type)
            {
                case "display.state":
                    State = MetaEnum.Parse(p.state, DisplayState.Stopped);
                    StateChanged?.Invoke(State);
                    break;
                case "display.sent":
                    Sent?.Invoke(p.requestId);
                    break;
                case "display.sendFailed":
                {
                    var err = new MetaError("display", p.error, p.message);
                    Debug.LogWarning($"[MetaDisplay] Envio {p.requestId} falhou: {err}");
                    SendFailed?.Invoke(p.requestId, err);
                    break;
                }
                case "display.error":
                    MetaWearables.RaiseError(new MetaError("display", p.error, p.message));
                    break;
                case "display.click":
                    if (p.id != null && s_Clicks.TryGetValue(p.id, out var action))
                    {
                        try { action?.Invoke(); }
                        catch (Exception e) { Debug.LogException(e); }
                    }
                    Clicked?.Invoke(p.id);
                    break;
                case "video.state":
                    Video = MetaEnum.Parse(p.state, VideoState.Idle);
                    VideoStateChanged?.Invoke(Video);
                    break;
                case "video.error":
                    MetaWearables.RaiseError(new MetaError("video", p.error, p.message));
                    break;
            }
        }

        /// <summary>Simula um clique (Editor / testes).</summary>
        public static void SimulateClick(string clickId) => Handle("display.click", new MetaPayload { id = clickId });

        internal static IEnumerable<string> ClickIds => s_Clicks.Keys;
    }

    /// <summary>Converte texturas (mesmo as sem Read/Write) e sprites de atlas em PNG.</summary>
    public static class GlassesTextureEncoder
    {
        /// <param name="sourceRect">Área em pixels (para sprites de atlas) ou null para a textura toda.</param>
        public static byte[] Encode(Texture source, Rect? sourceRect, int maxSize)
        {
            if (source == null) return null;
            var rect = sourceRect ?? new Rect(0, 0, source.width, source.height);
            if (rect.width < 1 || rect.height < 1) return null;

            float scale = Mathf.Min(1f, maxSize / Mathf.Max(rect.width, rect.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(rect.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(rect.height * scale));

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                var uvScale = new Vector2(rect.width / source.width, rect.height / source.height);
                var uvOffset = new Vector2(rect.x / source.width, rect.y / source.height);
                Graphics.Blit(source, rt, uvScale, uvOffset);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                var png = tex.EncodeToPNG();
                if (Application.isPlaying) UnityEngine.Object.Destroy(tex);
                else UnityEngine.Object.DestroyImmediate(tex);
                return png;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaDisplay] Não foi possível converter '{source.name}' em PNG: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
