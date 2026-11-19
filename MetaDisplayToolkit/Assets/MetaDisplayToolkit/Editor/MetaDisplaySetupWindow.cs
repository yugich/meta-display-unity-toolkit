using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MetaDisplay.Editor
{
    /// <summary>Meta Display > Configurar Projeto: checklist com correção automática + credenciais e módulos.</summary>
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

        [MenuItem("Meta Display/Configurar Projeto (Setup)", false, 0)]
        public static void Open()
        {
            var w = GetWindow<MetaDisplaySetupWindow>("Meta Display Setup");
            w.minSize = new Vector2(460, 520);
        }

        [MenuItem("Meta Display/Documentação do SDK (Display Android)", false, 100)]
        static void OpenDocs() => Application.OpenURL("https://wearables.developer.meta.com/docs/develop/dat/display-android");

        [MenuItem("Meta Display/Wearables Developer Center", false, 101)]
        static void OpenDevCenter() => Application.OpenURL("https://wearables.developer.meta.com/");

        void OnEnable() => m_Checks = BuildChecks();

        static List<Check> BuildChecks() => new List<Check>
        {
            new Check
            {
                Title = "Plataforma Android ativa",
                Help = "O app roda no celular Android e conversa com os óculos via app Meta AI.",
                IsOk = () => EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android,
                Fix = () => EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android),
                Required = true,
            },
            new Check
            {
                Title = "Pacote uGUI (Canvas + TextMeshPro)",
                Help = "Necessário para desenhar a UI dos óculos no Canvas.",
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
                Title = "TextMeshPro Essentials importado",
                Help = "Fontes padrão do TextMeshPro, usadas nos textos do Canvas.",
                IsOk = () => AssetDatabase.FindAssets("t:TMP_Settings").Length > 0,
                Fix = () => EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources"),
                Required = false,
            },
            new Check
            {
                Title = "Minimum API Level ≥ 31 (Android 12)",
                Help = "O SDK aceita a partir da API 29, mas o sample oficial usa 31 (permissão BLUETOOTH_CONNECT).",
                IsOk = () => (int)PlayerSettings.Android.minSdkVersion >= 31,
                Fix = () => PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)31,
                Required = true,
            },
            new Check
            {
                Title = "Target API Level ≥ 35 (ou Automático)",
                Help = "As dependências AndroidX do SDK exigem compileSdk 35+ e Android Gradle Plugin 8.6+ (Unity 6.0.45+ / 6.3+).",
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
                Help = "Os celulares atuais são ARM64; a Play Store exige 64 bits.",
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
                Help = "O registro no Meta AI e imagens/vídeos por URL usam a internet.",
                IsOk = () => PlayerSettings.Android.forceInternetPermission,
                Fix = () => PlayerSettings.Android.forceInternetPermission = true,
                Required = false,
            },
            new Check
            {
                Title = "MetaDisplayManager na cena aberta",
                Help = "Centraliza conexão, telas dos óculos, capacidades e eventos.",
                IsOk = () => FindAnyObjectByType<MetaDisplayManager>() != null,
                Fix = () => MetaDisplayMenus.CreateManager(),
                Required = false,
            },
        };

        void OnGUI()
        {
            var settings = MetaDisplaySettings.Load();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

            EditorGUILayout.LabelField("Meta Display Toolkit para Unity", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "O app roda no CELULAR e envia a interface para o Meta Ray-Ban Display por Bluetooth (via app Meta AI).\n" +
                "Antes de testar: ative o Developer Mode no Meta AI e instale o Device Access Toolkit nos óculos (com eles no rosto).",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Checklist do projeto", EditorStyles.boldLabel);
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
                        if (GUILayout.Button(ok ? "OK" : "Corrigir", GUILayout.Width(80))) check.Fix();
                }
            }
            if (GUILayout.Button("Corrigir tudo", GUILayout.Height(28)))
                foreach (var check in m_Checks)
                    if (!check.IsOk()) check.Fix();
            if (allOk) EditorGUILayout.HelpBox("Itens obrigatórios OK. Use File > Build Profiles > Android > Build And Run.", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Credenciais (manifest)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            settings.applicationId = EditorGUILayout.TextField(new GUIContent("Application ID", "Developer Mode: 0"), settings.applicationId);
            settings.clientToken = EditorGUILayout.TextField(new GUIContent("Client Token", "Developer Mode: 0"), settings.clientToken);
            settings.callbackScheme = EditorGUILayout.TextField(new GUIContent("Esquema de callback", "Vazio = automático"), settings.callbackScheme);
            EditorGUILayout.LabelField(" ", "Esquema efetivo: " + settings.EffectiveScheme + "://", EditorStyles.miniLabel);
            settings.analyticsOptOut = EditorGUILayout.Toggle(new GUIContent("Desativar analytics do SDK"), settings.analyticsOptOut);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Módulos do SDK incluídos no APK", EditorStyles.boldLabel);
            settings.sdkVersion = EditorGUILayout.TextField("Versão do DAT", settings.sdkVersion);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle("Core + Display", true);
            }
            settings.includeCamera = EditorGUILayout.Toggle("Câmera (+ áudio)", settings.includeCamera);
            settings.includeInputs = EditorGUILayout.Toggle("Inputs", settings.includeInputs);
            settings.includeMotion = EditorGUILayout.Toggle("Motion (IMU)", settings.includeMotion);
            settings.includeSpeech = EditorGUILayout.Toggle("Speech", settings.includeSpeech);
            settings.includeMockDevice = EditorGUILayout.Toggle(new GUIContent("Mock Device (óculos simulados)", "Desmarque em produção"), settings.includeMockDevice);
            settings.injectBuildConfig = EditorGUILayout.Toggle(new GUIContent("Configurar Gradle/manifest automaticamente"), settings.injectBuildConfig);
            if (EditorGUI.EndChangeCheck()) settings.Save();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Atalhos", EditorStyles.boldLabel);
            if (GUILayout.Button("Criar cena de exemplo (3 telas + painel de debug)")) MetaDisplayMenus.CreateDemoScene();
            if (GUILayout.Button("Criar uma Tela dos Óculos na cena")) MetaDisplayMenus.CreateScreenMenu();
            if (GUILayout.Button("Abrir LEIAME do toolkit"))
            {
                var guids = AssetDatabase.FindAssets("LEIAME t:TextAsset");
                if (guids.Length > 0) AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guids[0])));
            }
            EditorGUILayout.EndScrollView();
        }

        void OnInspectorUpdate() => Repaint();
    }
}
