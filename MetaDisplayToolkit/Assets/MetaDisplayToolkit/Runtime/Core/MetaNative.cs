using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace MetaDisplay
{
    /// <summary>
    /// Low-level layer: calls the Kotlin bridge (MetaDisplayBridge.aar) through JNI and receives its
    /// events. Outside Android (Editor), calls become a simulation (see <see cref="MetaEditorSimulation"/>).
    /// </summary>
    internal static class MetaNative
    {
        internal const string Package = "com.metadisplay.bridge.";
        internal const string ListenerInterface = Package + "MetaDisplayListener";

        static readonly Dictionary<string, AndroidJavaClass> s_Classes = new Dictionary<string, AndroidJavaClass>();
        static AndroidJavaObject s_Activity;

        /// <summary>True when running on the Android device (the native bridge exists).</summary>
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

        /// <summary>Calls a static void method of the bridge (MDCore, MDDisplay, MDCamera... classes).</summary>
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
                Debug.LogError($"[MetaDisplay] Failed to call {cls}.{method}: {e.Message}\n" +
                               "Check that MetaDisplayBridge.aar is in Plugins/Android and that the build included the DAT dependencies.");
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
                Debug.LogError($"[MetaDisplay] Failed to call {cls}.{method}: {e.Message}");
                return default;
            }
        }

        /// <summary>Converts byte[] to sbyte[] (the type Unity's JNI maps to Java byte[]).</summary>
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

        /// <summary>Receives Kotlin events on an Android thread and queues them for the main thread.</summary>
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
