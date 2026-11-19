using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MetaDisplay.Editor
{
    /// <summary>
    /// Writes a ready-to-host Three.js Web App that renders the avatar on the glasses, plus a tiny WebSocket relay
    /// so the Unity app can drive it (<see cref="GlassesWebAppLink"/>). Templates live in Editor/WebAppTemplate.
    /// </summary>
    public static class Glasses3DWebAppExporter
    {
        const string TemplatePrefix = "MDWebApp_";

        public class Options
        {
            public string Title = "Glasses Avatar";
            public string RelayUrl = "";
            public string Room = "meta-display-avatar";
            public string IdleAction = "idle";
            /// <summary>Empty = every animation of the model.</summary>
            public List<string> Actions = new List<string>();
            /// <summary>Existing .glb to copy as model.glb. Empty = try to export the target with UnityGLTF, else the built-in robot.</summary>
            public string GlbPath = "";
            /// <summary>Object exported with UnityGLTF when no .glb is given (skipped for the sample robot).</summary>
            public Transform ExportTarget;
        }

        public static Options OptionsFor(Glasses3DView view)
        {
            var o = new Options();
            if (view == null) return o;
            o.Title = string.IsNullOrEmpty(Application.productName) ? o.Title : Application.productName + " Avatar";
            o.IdleAction = string.IsNullOrEmpty(view.idleAction) ? "idle" : view.idleAction;
            if (view.webAppLink != null)
            {
                o.RelayUrl = view.webAppLink.relayUrl;
                o.Room = view.webAppLink.room;
            }
            bool robot = view.target != null && view.target.GetComponentInChildren<SampleAvatar>(true) != null;
            if (robot) o.Actions = view.Actions.ToList();
            else o.ExportTarget = view.target;
            return o;
        }

        /// <summary>Writes the Web App into <paramref name="folder"/>. Returns a short report.</summary>
        public static string Export(string folder, Options options)
        {
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();

            string modelUrl = "";
            if (!string.IsNullOrEmpty(options.GlbPath))
            {
                if (!File.Exists(options.GlbPath)) throw new FileNotFoundException("GLB not found", options.GlbPath);
                File.Copy(options.GlbPath, Path.Combine(folder, "model.glb"), true);
                modelUrl = "model.glb";
                report.AppendLine("Model: copied " + Path.GetFileName(options.GlbPath) + " as model.glb.");
            }
            else if (options.ExportTarget != null)
            {
                if (TryExportWithUnityGltf(options.ExportTarget, folder, "model", out var error))
                {
                    modelUrl = "model.glb";
                    report.AppendLine("Model: exported '" + options.ExportTarget.name + "' with UnityGLTF.");
                }
                else
                {
                    report.AppendLine("Model: built-in robot (" + error + "). Export your avatar as GLB and choose it, or set modelUrl in config.js.");
                }
            }
            else
            {
                report.AppendLine("Model: built-in robot (same as the Unity sample avatar).");
            }

            var actions = "[" + string.Join(", ", options.Actions.Where(a => !string.IsNullOrEmpty(a)).Select(JsString)) + "]";
            int written = 0;
            foreach (var template in LoadTemplates())
            {
                string relative = template.name.Substring(TemplatePrefix.Length).Replace("__", "/");
                string text = template.text;
                bool js = relative.EndsWith(".js");
                bool html = relative.EndsWith(".html");
                Func<string, string> escape = s => js ? JsEscape(s) : html ? HtmlEscape(s) : s;
                text = text.Replace("{{TITLE}}", escape(options.Title))
                           .Replace("{{RELAY_URL}}", escape(options.RelayUrl ?? ""))
                           .Replace("{{ROOM}}", escape(options.Room ?? ""))
                           .Replace("{{MODEL_URL}}", escape(modelUrl))
                           .Replace("{{IDLE}}", escape(options.IdleAction ?? "idle"))
                           .Replace("{{ACTIONS}}", actions);
                var path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, new UTF8Encoding(false));
                written++;
            }
            if (written == 0) throw new InvalidOperationException("Web App templates not found (Editor/WebAppTemplate).");
            report.Insert(0, $"Web App written to {folder} ({written} files).\n");
            if (string.IsNullOrEmpty(options.RelayUrl))
                report.AppendLine("Relay: none (the Web App works alone). Deploy relay/ and set the wss:// URL to link it with Unity.");
            return report.ToString();
        }

        static IEnumerable<TextAsset> LoadTemplates()
        {
            foreach (var guid in AssetDatabase.FindAssets(TemplatePrefix + " t:TextAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.name.StartsWith(TemplatePrefix, StringComparison.Ordinal)) yield return asset;
            }
        }

        /// <summary>Exports through UnityGLTF (org.khronos.unitygltf) by reflection, so it stays optional.</summary>
        public static bool TryExportWithUnityGltf(Transform root, string folder, string fileName, out string error)
        {
            error = null;
            var exporterType = FindType("UnityGLTF.GLTFSceneExporter");
            if (exporterType == null)
            {
                error = "UnityGLTF is not installed";
                return false;
            }
            try
            {
                var settingsType = FindType("UnityGLTF.GLTFSettings");
                var settings = settingsType?.GetMethod("GetOrCreateSettings", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                var contextType = FindType("UnityGLTF.ExportContext") ?? FindType("UnityGLTF.ExportOptions");
                if (contextType == null) throw new MissingMemberException("UnityGLTF.ExportContext");
                object context = settings != null && contextType.GetConstructor(new[] { settingsType }) != null
                    ? Activator.CreateInstance(contextType, settings)
                    : Activator.CreateInstance(contextType);
                var ctor = exporterType.GetConstructor(new[] { typeof(Transform[]), contextType });
                if (ctor == null) throw new MissingMethodException("GLTFSceneExporter(Transform[], " + contextType.Name + ")");
                var exporter = ctor.Invoke(new object[] { new[] { root }, context });
                var save = exporterType.GetMethod("SaveGLB", new[] { typeof(string), typeof(string) });
                if (save == null) throw new MissingMethodException("SaveGLB(string, string)");
                save.Invoke(exporter, new object[] { folder, fileName });
                return File.Exists(Path.Combine(folder, fileName + ".glb"));
            }
            catch (Exception e)
            {
                error = "UnityGLTF export failed: " + (e.InnerException ?? e).Message;
                return false;
            }
        }

        static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        static string JsString(string s) => "'" + JsEscape(s) + "'";

        static string JsEscape(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s ?? "")
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\'': sb.Append("\\'"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '<': sb.Append("\\u003c"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        static string HtmlEscape(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
