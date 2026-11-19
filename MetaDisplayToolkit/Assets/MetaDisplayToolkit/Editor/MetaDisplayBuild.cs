using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace MetaDisplay.Editor
{
    /// <summary>Toolkit settings, saved in ProjectSettings/MetaDisplaySettings.json.</summary>
    [Serializable]
    public class MetaDisplaySettings
    {
        const string FilePath = "ProjectSettings/MetaDisplaySettings.json";

        [Tooltip("Meta Wearables Device Access Toolkit version on Maven Central.")]
        public string sdkVersion = "1.0.1";
        [Tooltip("Developer Mode: 0. Production: value from the Wearables Developer Center.")]
        public string applicationId = "0";
        [Tooltip("Developer Mode: 0. Production: value from the Wearables Developer Center.")]
        public string clientToken = "0";
        [Tooltip("URL scheme Meta AI uses to return to the app. Empty = generated from the package name.")]
        public string callbackScheme = "";
        [Tooltip("Turns off the SDK usage data collection by Meta.")]
        public bool analyticsOptOut;
        public bool includeCamera = true;
        public bool includeInputs = true;
        public bool includeMotion = true;
        public bool includeSpeech = true;
        [Tooltip("Simulated glasses. Uncheck for production builds to get a smaller APK.")]
        public bool includeMockDevice = true;
        [Tooltip("Injects dependencies and manifest entries automatically into the Android build.")]
        public bool injectBuildConfig = true;

        static MetaDisplaySettings s_Cached;

        public static MetaDisplaySettings Load()
        {
            if (s_Cached != null) return s_Cached;
            s_Cached = new MetaDisplaySettings();
            try
            {
                if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), s_Cached);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MetaDisplay] Could not read " + FilePath + ": " + e.Message);
            }
            return s_Cached;
        }

        public void Save() => File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));

        public string EffectiveScheme
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(callbackScheme)) return callbackScheme.Trim().ToLowerInvariant();
                // Scheme = package name (letters, digits and dots are valid in URI schemes).
                var id = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
                var clean = Regex.Replace((id ?? "").ToLowerInvariant(), "[^a-z0-9.]", "").Trim('.');
                return string.IsNullOrEmpty(clean) || !char.IsLetter(clean[0]) ? "mwdat.unityapp" : clean;
            }
        }

        public string[] Artifacts()
        {
            var list = new System.Collections.Generic.List<string> { "mwdat-core", "mwdat-display" };
            if (includeCamera) list.Add("mwdat-camera");
            if (includeInputs) list.Add("mwdat-inputs");
            if (includeMotion) list.Add("mwdat-motion");
            if (includeSpeech) list.Add("mwdat-speech");
            if (includeMockDevice) list.Add("mwdat-mockdevice");
            return list.ToArray();
        }
    }

    /// <summary>
    /// After Unity generates the Gradle project: adds the DAT dependencies (Maven Central), the
    /// manifest permissions/meta-data and the Meta AI callback scheme on the Unity Activity.
    /// No need to edit mainTemplate.gradle or AndroidManifest.xml by hand.
    /// </summary>
    public class MetaDisplayGradlePostProcessor : IPostGenerateGradleAndroidProject
    {
        const string BeginMarker = "// >>> MetaDisplayToolkit";
        const string EndMarker = "// <<< MetaDisplayToolkit";
        const string AndroidNs = "http://schemas.android.com/apk/res/android";

        public int callbackOrder => 900;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            var settings = MetaDisplaySettings.Load();
            if (!settings.injectBuildConfig) return;
            try
            {
                PatchBuildGradle(unityLibraryPath, settings);
                PatchLauncherGradle(Path.Combine(unityLibraryPath, "..", "launcher"));
                PatchGradleProperties(Path.Combine(unityLibraryPath, "..", "gradle.properties"));
                PatchManifests(unityLibraryPath, settings);
                Debug.Log($"[MetaDisplay] Build configured: DAT {settings.sdkVersion} ({string.Join(", ", settings.Artifacts())}), scheme '{settings.EffectiveScheme}'.");
            }
            catch (Exception e)
            {
                Debug.LogError("[MetaDisplay] Failed to configure the Gradle project: " + e);
                throw;
            }
        }

        static void PatchBuildGradle(string unityLibraryPath, MetaDisplaySettings settings)
        {
            var path = Path.Combine(unityLibraryPath, "build.gradle");
            if (!File.Exists(path)) path = Path.Combine(unityLibraryPath, "build.gradle.kts");
            if (!File.Exists(path)) throw new FileNotFoundException("unityLibrary build.gradle not found", path);
            bool kts = path.EndsWith(".kts", StringComparison.Ordinal);

            var text = File.ReadAllText(path);
            text = Regex.Replace(text, Regex.Escape(BeginMarker) + ".*?" + Regex.Escape(EndMarker) + @"\r?\n?", "", RegexOptions.Singleline);

            var sb = new StringBuilder();
            sb.AppendLine().AppendLine(BeginMarker + " (generated automatically; do not edit)");
            sb.AppendLine("dependencies {");
            foreach (var artifact in settings.Artifacts())
            {
                var coord = $"com.meta.wearable:{artifact}:{settings.sdkVersion}";
                sb.AppendLine(kts ? $"    implementation(\"{coord}\")" : $"    implementation '{coord}'");
            }
            sb.AppendLine("}");
            sb.AppendLine(EndMarker);
            File.WriteAllText(path, text.TrimEnd() + Environment.NewLine + sb);
        }

        /// <summary>
        /// fbjni (a dependency of the Meta SDK) and Unity's IL2CPP both ship libc++_shared.so: without pickFirst
        /// the native library merge fails with "2 files found with path 'lib/arm64-v8a/libc++_shared.so'".
        /// Unity's copy comes first (and is the newest; libc++ is backward compatible).
        /// </summary>
        static void PatchLauncherGradle(string launcherPath)
        {
            var path = Path.Combine(launcherPath, "build.gradle");
            bool kts = false;
            if (!File.Exists(path))
            {
                path = Path.Combine(launcherPath, "build.gradle.kts");
                kts = true;
            }
            if (!File.Exists(path))
            {
                Debug.LogWarning("[MetaDisplay] launcher/build.gradle not found; if the build complains about libc++_shared.so, add pickFirsts manually.");
                return;
            }
            var text = File.ReadAllText(path);
            text = Regex.Replace(text, Regex.Escape(BeginMarker) + ".*?" + Regex.Escape(EndMarker) + @"\r?\n?", "", RegexOptions.Singleline);
            var sb = new StringBuilder();
            sb.AppendLine().AppendLine(BeginMarker + " (generated automatically; do not edit)");
            if (kts)
            {
                sb.AppendLine("android {");
                sb.AppendLine("    packaging {");
                sb.AppendLine("        jniLibs { pickFirsts += setOf(\"lib/*/libc++_shared.so\", \"lib/*/libfbjni.so\") }");
                sb.AppendLine("        resources { excludes += setOf(\"META-INF/{AL2.0,LGPL2.1}\") }");
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine("android {");
                sb.AppendLine("    packaging {");
                sb.AppendLine("        jniLibs { pickFirsts += ['lib/*/libc++_shared.so', 'lib/*/libfbjni.so'] }");
                sb.AppendLine("        resources { excludes += ['META-INF/{AL2.0,LGPL2.1}'] }");
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }
            sb.AppendLine(EndMarker);
            File.WriteAllText(path, text.TrimEnd() + Environment.NewLine + sb);
        }

        static void PatchGradleProperties(string path)
        {
            var text = File.Exists(path) ? File.ReadAllText(path) : "";
            if (Regex.IsMatch(text, @"^\s*android\.useAndroidX\s*=", RegexOptions.Multiline)) return;
            File.WriteAllText(path, text.TrimEnd() + Environment.NewLine + "android.useAndroidX=true" + Environment.NewLine);
        }

        static void PatchManifests(string unityLibraryPath, MetaDisplaySettings settings)
        {
            var libManifest = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
            var launcherManifest = Path.Combine(unityLibraryPath, "..", "launcher", "src", "main", "AndroidManifest.xml");

            bool activityPatched = PatchManifest(libManifest, settings, true);
            if (!activityPatched && File.Exists(launcherManifest)) activityPatched = PatchManifest(launcherManifest, settings, false);
            if (!activityPatched)
                Debug.LogWarning("[MetaDisplay] Could not find the main Unity Activity for the callback scheme. App-initiated registration may not return on its own.");
        }

        /// <returns>True if the main Activity was found and configured.</returns>
        static bool PatchManifest(string path, MetaDisplaySettings settings, bool addApplicationEntries)
        {
            if (!File.Exists(path)) return false;
            var doc = new XmlDocument { PreserveWhitespace = false };
            doc.Load(path);
            var manifest = doc.DocumentElement;
            var app = manifest["application"] ?? (XmlElement)manifest.AppendChild(doc.CreateElement("application"));

            if (addApplicationEntries)
            {
                foreach (var perm in new[] { "android.permission.BLUETOOTH", "android.permission.BLUETOOTH_CONNECT", "android.permission.INTERNET" })
                    if (!HasNamed(manifest, "uses-permission", perm))
                        manifest.InsertBefore(Named(doc, "uses-permission", perm), app);

                SetMetaData(doc, app, "com.meta.wearable.mwdat.APPLICATION_ID", settings.applicationId);
                SetMetaData(doc, app, "com.meta.wearable.mwdat.CLIENT_TOKEN", settings.clientToken);
                if (settings.analyticsOptOut) SetMetaData(doc, app, "com.meta.wearable.mwdat.ANALYTICS_OPT_OUT", "true");
            }

            var activity = FindMainActivity(app);
            if (activity == null)
            {
                Save(doc, path);
                return false;
            }

            var scheme = settings.EffectiveScheme;
            bool hasScheme = activity.SelectNodes("intent-filter/data").Cast<XmlElement>()
                .Any(d => d.GetAttribute("scheme", AndroidNs) == scheme);
            if (!hasScheme)
            {
                var filter = doc.CreateElement("intent-filter");
                filter.AppendChild(Named(doc, "action", "android.intent.action.VIEW"));
                filter.AppendChild(Named(doc, "category", "android.intent.category.DEFAULT"));
                filter.AppendChild(Named(doc, "category", "android.intent.category.BROWSABLE"));
                var data = doc.CreateElement("data");
                SetAndroid(data, "scheme", scheme);
                filter.AppendChild(data);
                activity.AppendChild(filter);
            }
            Save(doc, path);
            return true;
        }

        /// <summary>Saves as UTF-8 without BOM.</summary>
        static void Save(XmlDocument doc, string path)
        {
            var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
            using (var writer = XmlWriter.Create(path, settings)) doc.Save(writer);
        }

        static XmlElement FindMainActivity(XmlElement app)
        {
            var activities = app.GetElementsByTagName("activity").Cast<XmlElement>().ToList();
            foreach (var a in activities)
                foreach (XmlElement md in a.GetElementsByTagName("meta-data"))
                    if (md.GetAttribute("name", AndroidNs) == "unityplayer.UnityActivity") return a;
            foreach (var a in activities)
                foreach (XmlElement c in a.GetElementsByTagName("category"))
                    if (c.GetAttribute("name", AndroidNs) == "android.intent.category.LAUNCHER") return a;
            return null;
        }

        static bool HasNamed(XmlElement parent, string tag, string name) =>
            parent.ChildNodes.OfType<XmlElement>().Any(e => e.Name == tag && e.GetAttribute("name", AndroidNs) == name);

        static XmlElement Named(XmlDocument doc, string tag, string name)
        {
            var e = doc.CreateElement(tag);
            SetAndroid(e, "name", name);
            return e;
        }

        static void SetMetaData(XmlDocument doc, XmlElement app, string name, string value)
        {
            var existing = app.ChildNodes.OfType<XmlElement>()
                .FirstOrDefault(e => e.Name == "meta-data" && e.GetAttribute("name", AndroidNs) == name);
            if (existing == null)
            {
                existing = Named(doc, "meta-data", name);
                app.AppendChild(existing);
            }
            SetAndroid(existing, "value", string.IsNullOrEmpty(value) ? "0" : value);
        }

        /// <summary>android:xxx attribute with the explicit prefix (avoids generated prefixes such as d1p1).</summary>
        static void SetAndroid(XmlElement element, string localName, string value)
        {
            var attr = element.GetAttributeNode(localName, AndroidNs);
            if (attr == null)
            {
                attr = element.OwnerDocument.CreateAttribute("android", localName, AndroidNs);
                element.SetAttributeNode(attr);
            }
            attr.Value = value;
        }
    }
}
