using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MetaDisplay.Editor
{
    /// <summary>Meta Display > Project Setup: checklist with automatic fixes + credentials and modules.</summary>
    public class MetaDisplaySetupWindow : EditorWindow
    {
        struct Check
        {
            public string Title;
            public string Help;
            public Func<bool> IsOk;
            public Action Fix;
            public bool Required;
        }

        Vector2 m_Scroll;
        List<Check> m_Checks;

        [MenuItem("Meta Display/Project Setup", false, 0)]
        public static void Open()
        {
            var w = GetWindow<MetaDisplaySetupWindow>("Meta Display Setup");
            w.minSize = new Vector2(460, 520);
        }

        [MenuItem("Meta Display/SDK Documentation (Display Android)", false, 100)]
        static void OpenDocs() => Application.OpenURL("https://wearables.developer.meta.com/docs/develop/dat/display-android");

        [MenuItem("Meta Display/Wearables Developer Center", false, 101)]
        static void OpenDevCenter() => Application.OpenURL("https://wearables.developer.meta.com/");

        void OnEnable() => m_Checks = BuildChecks();

        static List<Check> BuildChecks() => new List<Check>
        {
            new Check
            {
                Title = "Android platform active",
                Help = "The app runs on the Android phone and talks to the glasses through the Meta AI app.",
                IsOk = () => EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android,
                Fix = () => EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android),
                Required = true,
            },
            new Check
            {
                Title = "uGUI package (Canvas + TextMeshPro)",
                Help = "Required to draw the glasses UI on the Canvas.",
                IsOk = () =>
                {
#if METADISPLAY_UGUI
                    return true;
#else
                    return false;
#endif
                },
                Fix = () => UnityEditor.PackageManager.Client.Add("com.unity.ugui"),
                Required = false,
            },
            new Check
            {
                Title = "TextMeshPro Essentials imported",
                Help = "Default TextMeshPro fonts, used by the Canvas texts.",
                IsOk = () => AssetDatabase.FindAssets("t:TMP_Settings").Length > 0,
                Fix = () => EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources"),
                Required = false,
            },
            new Check
            {
                Title = "Minimum API Level ≥ 31 (Android 12)",
                Help = "The SDK accepts API 29+, but the official sample uses 31 (BLUETOOTH_CONNECT permission).",
                IsOk = () => (int)PlayerSettings.Android.minSdkVersion >= 31,
                Fix = () => PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)31,
                Required = true,
            },
            new Check
            {
                Title = "Target API Level ≥ 35 (or Automatic)",
                Help = "The SDK's AndroidX dependencies require compileSdk 35+ and Android Gradle Plugin 8.6+ (Unity 6.0.45+ / 6.3+).",
                IsOk = () =>
                {
                    int t = (int)PlayerSettings.Android.targetSdkVersion;
                    return t == 0 || t >= 35;
                },
                Fix = () => PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto,
                Required = true,
            },
            new Check
            {
                Title = "IL2CPP + ARM64",
                Help = "Current phones are ARM64; the Play Store requires 64-bit.",
                IsOk = () => PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP &&
                             (PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) != 0,
                Fix = () =>
                {
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                    PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                },
                Required = true,
            },
            new Check
            {
                Title = "Internet Access: Require",
                Help = "Meta AI registration and URL images/videos use the internet.",
                IsOk = () => PlayerSettings.Android.forceInternetPermission,
                Fix = () => PlayerSettings.Android.forceInternetPermission = true,
                Required = false,
            },
            new Check
            {
                Title = "MetaDisplayManager in the open scene",
                Help = "Centralizes connection, glasses screens, capabilities and events.",
                IsOk = () => FindAnyObjectByType<MetaDisplayManager>() != null,
                Fix = () => MetaDisplayMenus.CreateManager(),
                Required = false,
            },
        };

        void OnGUI()
        {
            var settings = MetaDisplaySettings.Load();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            EditorGUILayout.LabelField("Meta Display Toolkit for Unity", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The app runs on the PHONE and sends the interface to the Meta Ray-Ban Display over Bluetooth (through the Meta AI app).\n" +
                "Before testing: enable Developer Mode in Meta AI and install the Device Access Toolkit on the glasses (while wearing them).",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Project checklist", EditorStyles.boldLabel);
            bool allOk = true;
            foreach (var check in m_Checks)
            {
                bool ok = check.IsOk();
                allOk &= ok || !check.Required;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    var icon = EditorGUIUtility.IconContent(ok ? "TestPassed" : check.Required ? "TestFailed" : "console.warnicon.sml");
                    GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(check.Title, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(check.Help, EditorStyles.wordWrappedMiniLabel);
                    }
                    using (new EditorGUI.DisabledScope(ok))
                        if (GUILayout.Button(ok ? "OK" : "Fix", GUILayout.Width(80))) check.Fix();
                }
            }
            if (GUILayout.Button("Fix all", GUILayout.Height(28)))
                foreach (var check in m_Checks)
                    if (!check.IsOk()) check.Fix();
            if (allOk) EditorGUILayout.HelpBox("Required items OK. Use File > Build Profiles > Android > Build And Run.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Credentials (manifest)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            settings.applicationId = EditorGUILayout.TextField(new GUIContent("Application ID", "Developer Mode: 0"), settings.applicationId);
            settings.clientToken = EditorGUILayout.TextField(new GUIContent("Client Token", "Developer Mode: 0"), settings.clientToken);
            settings.callbackScheme = EditorGUILayout.TextField(new GUIContent("Callback scheme", "Empty = automatic"), settings.callbackScheme);
            EditorGUILayout.LabelField(" ", "Effective scheme: " + settings.EffectiveScheme + "://", EditorStyles.miniLabel);
            settings.analyticsOptOut = EditorGUILayout.Toggle(new GUIContent("Turn off SDK analytics"), settings.analyticsOptOut);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("SDK modules included in the APK", EditorStyles.boldLabel);
            settings.sdkVersion = EditorGUILayout.TextField("DAT version", settings.sdkVersion);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Core + Display", true);
            }
            settings.includeCamera = EditorGUILayout.Toggle("Camera (+ audio)", settings.includeCamera);
            settings.includeInputs = EditorGUILayout.Toggle("Inputs", settings.includeInputs);
            settings.includeMotion = EditorGUILayout.Toggle("Motion (IMU)", settings.includeMotion);
            settings.includeSpeech = EditorGUILayout.Toggle("Speech", settings.includeSpeech);
            settings.includeMockDevice = EditorGUILayout.Toggle(new GUIContent("Mock Device (simulated glasses)", "Uncheck for production"), settings.includeMockDevice);
            settings.injectBuildConfig = EditorGUILayout.Toggle(new GUIContent("Configure Gradle/manifest automatically"), settings.injectBuildConfig);
            if (EditorGUI.EndChangeCheck()) settings.Save();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shortcuts", EditorStyles.boldLabel);
            if (GUILayout.Button("Create sample scene (3 screens + debug panel)")) MetaDisplayMenus.CreateDemoScene();
            if (GUILayout.Button("Create a Glasses Screen in the scene")) MetaDisplayMenus.CreateScreenMenu();
            if (GUILayout.Button("Open the toolkit README"))
            {
                foreach (var guid in AssetDatabase.FindAssets("README t:TextAsset"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.Contains("MetaDisplayToolkit")) continue;
                    AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<TextAsset>(path));
                    break;
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void OnInspectorUpdate() => Repaint();
    }
}
