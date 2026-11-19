using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Leva os eventos da ponte para a main thread do Unity e faz o polling de frames, IMU e áudio.
    /// Criado automaticamente (objeto oculto, DontDestroyOnLoad).
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-1000)]
    internal sealed class MetaDispatcher : MonoBehaviour
    {
        static readonly ConcurrentQueue<KeyValuePair<string, string>> s_Queue = new ConcurrentQueue<KeyValuePair<string, string>>();
        static MetaDispatcher s_Instance;

        /// <summary>Chamado a cada frame (main thread), depois dos eventos.</summary>
        internal static event Action Tick;

        internal static void Enqueue(string type, string payload) =>
            s_Queue.Enqueue(new KeyValuePair<string, string>(type, payload));

        internal static void EnsureExists()
        {
            if (s_Instance != null || !Application.isPlaying) return;
            var go = new GameObject("[MetaDisplay Dispatcher]") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<MetaDispatcher>();
        }

        void Update()
        {
            int guard = 0;
            while (guard++ < 500 && s_Queue.TryDequeue(out var e))
            {
                try
                {
                    MetaEvents.Route(e.Key, e.Value);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
            try
            {
                Tick?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        void OnApplicationFocus(bool focus)
        {
            if (focus && MetaWearables.IsInitialized) MetaVoiceInvocations.CheckLaunchIntent();
        }

        void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
        }
    }
}
