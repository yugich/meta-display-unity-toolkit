using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace MetaDisplay
{
    /// <summary>
    /// Connects the Unity app to a Web App running on the glasses (Three.js), through a WebSocket relay that both
    /// join with the same room name. Unity sends commands (play, say, rotate, look); the Web App sends back what the
    /// wearer does (input, action). Export the Web App with Meta Display > 3D > Export Web App.
    /// Messages are JSON objects with a "type" field; anything from the relay is untrusted input.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Meta Display/Glasses Web App Link")]
    public class GlassesWebAppLink : MonoBehaviour
    {
        [Serializable] public class MessageEvent : UnityEvent<string> { }

        /// <summary>Fields the toolkit understands (unknown fields are ignored).</summary>
        [Serializable]
        public class Message
        {
            public string type;
            public string name;
            public string key;
            public string text;
            public string from;
            public float degrees;
            public float yaw;
            public float pitch;
            public string[] actions;
            /// <summary>The original JSON.</summary>
            [NonSerialized] public string json;
        }

        [Tooltip("WebSocket relay (wss://…). Put the same URL in the Web App's config.js. Run the relay from the exported 'relay' folder.")]
        public string relayUrl = "";
        [Tooltip("Shared room name: the Unity app and the Web App with the same room talk to each other.")]
        public string room = "meta-display-avatar";
        public bool connectOnStart = true;
        [Tooltip("Raw JSON of every message from the Web App.")]
        public MessageEvent onMessage = new MessageEvent();

        public event Action<Message> MessageReceived;
        public event Action<bool> ConnectionChanged;

        public bool IsConnected => m_Socket != null && m_Socket.State == WebSocketState.Open;
        public string Status { get; private set; } = "Not connected";
        /// <summary>True after the Web App said hello (it is open on the glasses).</summary>
        public bool WebAppOnline { get; private set; }
        /// <summary>Actions reported by the Web App (animation names of its model).</summary>
        public string[] WebAppActions { get; private set; } = Array.Empty<string>();

        ClientWebSocket m_Socket;
        CancellationTokenSource m_Cancel;
        readonly ConcurrentQueue<string> m_Incoming = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<Action> m_MainThread = new ConcurrentQueue<Action>();
        readonly SemaphoreSlim m_SendLock = new SemaphoreSlim(1, 1);
        bool m_Wanted;

        void Start()
        {
            if (connectOnStart && !string.IsNullOrEmpty(relayUrl)) Connect();
        }

        void OnDisable() => Disconnect();

        /// <summary>Connects (and keeps reconnecting) to the relay.</summary>
        public void Connect()
        {
            if (m_Wanted) return;
            if (string.IsNullOrEmpty(relayUrl))
            {
                Status = "Set the relay URL (wss://…) to talk to the Web App";
                return;
            }
            m_Wanted = true;
            m_Cancel = new CancellationTokenSource();
            _ = RunAsync(m_Cancel.Token);
        }

        public void Disconnect()
        {
            m_Wanted = false;
            m_Cancel?.Cancel();
            m_Cancel = null;
            var socket = m_Socket;
            m_Socket = null;
            if (socket != null)
            {
                try { socket.Abort(); } catch { /* already closed */ }
                socket.Dispose();
            }
            WebAppOnline = false;
            Status = "Not connected";
        }

        // ───────────── commands ─────────────

        public void SendPlay(string action) => SendRaw("{\"type\":\"play\",\"name\":" + Quote(action) + "}");

        public void SendSay(string text, float seconds = 0f) =>
            SendRaw("{\"type\":\"say\",\"text\":" + Quote(text) + ",\"seconds\":" + seconds.ToString("0.###", CultureInfo.InvariantCulture) + "}");

        public void SendRotate(float degrees) =>
            SendRaw("{\"type\":\"rotate\",\"degrees\":" + degrees.ToString("0.###", CultureInfo.InvariantCulture) + "}");

        public void SendLook(float yaw, float pitch) =>
            SendRaw("{\"type\":\"look\",\"yaw\":" + yaw.ToString("0.##", CultureInfo.InvariantCulture) +
                    ",\"pitch\":" + pitch.ToString("0.##", CultureInfo.InvariantCulture) + "}");

        /// <summary>Sends any JSON object to the Web App (handle it in app.js, onMessage).</summary>
        public void SendRaw(string json)
        {
            var socket = m_Socket;
            if (socket == null || socket.State != WebSocketState.Open) return;
            _ = SendAsync(socket, json);
        }

        async Task SendAsync(ClientWebSocket socket, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await m_SendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                m_MainThread.Enqueue(() => Debug.LogWarning("[MetaDisplay] Web App link: send failed: " + e.Message));
            }
            finally
            {
                m_SendLock.Release();
            }
        }

        // ───────────── connection loop ─────────────

        async Task RunAsync(CancellationToken token)
        {
            int backoff = 1;
            while (!token.IsCancellationRequested)
            {
                var socket = new ClientWebSocket();
                try
                {
                    SetStatus("Connecting…");
                    var uri = new Uri(relayUrl + (relayUrl.Contains("?") ? "&" : "?") + "room=" + Uri.EscapeDataString(room) + "&role=unity");
                    await socket.ConnectAsync(uri, token).ConfigureAwait(false);
                    m_Socket = socket;
                    backoff = 1;
                    SetStatus("Connected to the relay (waiting for the Web App)");
                    m_MainThread.Enqueue(() => ConnectionChanged?.Invoke(true));
                    await SendAsync(socket, "{\"type\":\"hello\",\"from\":\"unity\"}").ConfigureAwait(false);
                    await ReceiveLoop(socket, token).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    SetStatus("Relay error: " + e.Message);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                finally
                {
                    if (m_Socket == socket) m_Socket = null;
                    socket.Dispose();
                    m_MainThread.Enqueue(() =>
                    {
                        WebAppOnline = false;
                        ConnectionChanged?.Invoke(false);
                    });
                }
                if (token.IsCancellationRequested) break;
                try { await Task.Delay(backoff * 1000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                backoff = Math.Min(backoff * 2, 15);
            }
        }

        async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            var text = new StringBuilder();
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    SetStatus("Relay closed the connection");
                    return;
                }
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (text.Length > 256 * 1024)
                {
                    text.Clear();
                    continue;
                }
                if (!result.EndOfMessage) continue;
                m_Incoming.Enqueue(text.ToString());
                text.Clear();
            }
        }

        void SetStatus(string status) => m_MainThread.Enqueue(() => Status = status);

        void Update()
        {
            while (m_MainThread.TryDequeue(out var action)) action();
            while (m_Incoming.TryDequeue(out var json))
            {
                Message message;
                try
                {
                    message = JsonUtility.FromJson<Message>(json);
                }
                catch (Exception)
                {
                    continue;
                }
                if (message == null || string.IsNullOrEmpty(message.type)) continue;
                message.json = json;
                if (message.type == "hello" && message.from == "glasses")
                {
                    WebAppOnline = true;
                    if (message.actions != null) WebAppActions = message.actions;
                    Status = "Web App online";
                }
                else if (message.type == "bye")
                {
                    WebAppOnline = false;
                    Status = "Web App closed";
                }
                try
                {
                    onMessage?.Invoke(json);
                    MessageReceived?.Invoke(message);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        static string Quote(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
