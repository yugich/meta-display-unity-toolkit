using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace MetaDisplay
{
    /// <summary>
    /// Camada de baixo nível: chama a ponte Kotlin (MetaDisplayBridge.aar) via JNI e recebe os
    /// eventos dela. Fora do Android (Editor), as chamadas viram simulação (ver <see cref="MetaEditorSimulation"/>).
    /// </summary>
    internal static class MetaNative
    {
        internal const string Package = "com.estudos.metadisplay.";
        internal const string ListenerInterface = Package + "MetaDisplayListener";

        static readonly Dictionary<string, AndroidJavaClass> s_Classes = new Dictionary<string, AndroidJavaClass>();
        static AndroidJavaObject s_Activity;

        /// <summary>True quando rodando no aparelho Android (a ponte nativa existe).</summary>
        internal static bool IsNative =>
#if UNITY_ANDROID && !UNITY_EDITOR
            true;
#else
            false;
#endif

        internal static AndroidJavaObject Activity
        {
            get
            {
                if (!IsNative) return null;
                if (s_Activity == null)
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        s_Activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                }
                return s_Activity;
            }
        }

        static AndroidJavaClass Class(string simpleName)
        {
            if (!s_Classes.TryGetValue(simpleName, out var cls))
            {
                cls = new AndroidJavaClass(Package + simpleName);
                s_Classes[simpleName] = cls;
            }
            return cls;
        }

        /// <summary>Chama um método estático void da ponte (classes MDCore, MDDisplay, MDCamera...).</summary>
        internal static void Call(string cls, string method, params object[] args)
        {
            if (!IsNative)
            {
                MetaEditorSimulation.OnNativeCall(cls, method, args);
                return;
            }
            try
            {
                Class(cls).CallStatic(method, args);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDisplay] Falha ao chamar {cls}.{method}: {e.Message}\n" +
                               "Confira se o MetaDisplayBridge.aar está em Plugins/Android e se o build incluiu as dependências do DAT.");
                MetaEvents.RaiseError(new MetaError("bridge", "JNI_CALL_FAILED", $"{cls}.{method}: {e.Message}"));
            }
        }

        internal static T Call<T>(string cls, string method, params object[] args)
        {
            if (!IsNative) return default;
            try
            {
                return Class(cls).CallStatic<T>(method, args);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MetaDisplay] Falha ao chamar {cls}.{method}: {e.Message}");
                return default;
            }
        }

        /// <summary>Converte byte[] para sbyte[] (tipo que o JNI do Unity mapeia para byte[] do Java).</summary>
        internal static sbyte[] ToJava(byte[] data)
        {
            if (data == null) return null;
            var result = new sbyte[data.Length];
            Buffer.BlockCopy(data, 0, result, 0, data.Length);
            return result;
        }

        internal static byte[] FromJava(sbyte[] data)
        {
            if (data == null) return null;
            var result = new byte[data.Length];
            Buffer.BlockCopy(data, 0, result, 0, data.Length);
            return result;
        }

        internal static AndroidJavaProxy CreateListener() => new Listener();

        /// <summary>Recebe eventos do Kotlin numa thread do Android e enfileira para a main thread.</summary>
        [Preserve]
        sealed class Listener : AndroidJavaProxy
        {
            [Preserve]
            public Listener() : base(ListenerInterface) { }

            [Preserve]
            public void onEvent(string type, string payload) => MetaDispatcher.Enqueue(type, payload);
        }
    }
}
