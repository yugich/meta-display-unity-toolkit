using UnityEditor;
using UnityEngine;
#if METADISPLAY_UGUI
using System.Collections.Generic;
#endif

namespace MetaDisplay.Editor
{
    [CustomEditor(typeof(MetaDisplayManager))]
    public class MetaDisplayManagerEditor : UnityEditor.Editor
    {
        static bool s_Connection = true, s_Glasses = true, s_Capabilities, s_Testing, s_Events;
#if METADISPLAY_UGUI
        static bool s_UiMap = true;
#endif

        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        void Field(string name, string label = null)
        {
            var p = P(name);
            if (p != null) EditorGUILayout.PropertyField(p, label != null ? new GUIContent(label, p.tooltip) : null, true);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var manager = (MetaDisplayManager)target;

            if (Application.isPlaying) DrawStatus(manager);
            else EditorGUILayout.HelpBox("Dê Play para testar no Editor (óculos simulados) ou gere o APK para usar os óculos reais.", MessageType.None);

            s_Connection = Section(s_Connection, "Conexão com os óculos");
            if (s_Connection)
            {
                Field("initializeOnStart", "Inicializar ao abrir");
                Field("registerAutomatically", "Registrar automaticamente");
                Field("connectWhenRegistered", "Conectar quando registrado");
                Field("deviceSelection", "Quais óculos");
                if (manager.deviceSelection == DeviceSelection.Specific) Field("specificDeviceId", "Id do dispositivo");
            }

            s_Glasses = Section(s_Glasses, "Display dos óculos");
            if (s_Glasses)
            {
                Field("enableDisplay", "Usar o display");
#if METADISPLAY_UGUI
                Field("initialScreen", "Tela inicial");
                Field("restoreCurrentScreen", "Restaurar tela ao reconectar");
                if (manager.initialScreen == null)
                    EditorGUILayout.HelpBox("Escolha a tela inicial (GlassesScreen) ou crie uma em GameObject > Meta Display > Tela dos Óculos.", MessageType.Warning);
#else
                EditorGUILayout.HelpBox("Instale o pacote com.unity.ugui para desenhar telas no Canvas (Meta Display > Configurar Projeto).", MessageType.Info);
#endif
            }

#if METADISPLAY_UGUI
            s_UiMap = Section(s_UiMap, "Mapa de UIs: o que vai para o celular e o que vai para os óculos");
            if (s_UiMap) DrawUiMap();
#endif

            s_Capabilities = Section(s_Capabilities, "Capacidades iniciadas ao conectar");
            if (s_Capabilities)
            {
                Field("startCamera", "Câmera");
                if (manager.startCamera)
                {
                    EditorGUI.indentLevel++;
                    Field("cameraQuality", "Qualidade");
                    Field("cameraFps", "FPS");
                    Field("cameraAudio", "Áudio (experimental)");
                    EditorGUI.indentLevel--;
                }
                Field("startMotion", "IMU / Motion (experimental)");
                if (manager.startMotion)
                {
                    EditorGUI.indentLevel++;
                    Field("motionRate", "Taxa");
                    EditorGUI.indentLevel--;
                }
                Field("startInputs", "Inputs (experimental)");
                if (manager.startInputs)
                {
                    EditorGUI.indentLevel++;
                    Field("inputsConsumeBack", "Capturar Voltar");
                    EditorGUI.indentLevel--;
                }
                Field("startSpeech", "Fala (experimental)");
                Field("startVoiceInvocations", "\"Hey Meta\" (experimental)");
                EditorGUILayout.HelpBox("Recursos experimentais funcionam em Developer Mode e canais beta; ainda não podem ir para produção.", MessageType.None);
            }

            s_Testing = Section(s_Testing, "Celular e testes");
            if (s_Testing)
            {
                Field("showPhoneDebugPanel", "Painel de debug no celular");
                Field("simulateInEditor", "Simular óculos no Editor");
                Field("useMockDevice", "Óculos simulados no celular");
                if (manager.useMockDevice)
                {
                    EditorGUI.indentLevel++;
                    Field("mockShowDisplayPreview", "Preview do display");
                    Field("mockPreviewSize", "Tamanho (px)");
                    Field("mockPreviewCorner", "Canto");
                    EditorGUI.indentLevel--;
                    EditorGUILayout.HelpBox("Com óculos simulados, o app não usa os óculos reais. Desligue antes de testar com o hardware.", MessageType.Warning);
                }
            }

            s_Events = Section(s_Events, "Eventos (sem código)");
            if (s_Events)
            {
                Field("onInitialized", "Ao inicializar");
                Field("onRegistered", "Ao registrar");
                Field("onConnected", "Ao conectar");
                Field("onDisplayReady", "Display pronto");
                Field("onDisconnected", "Ao desconectar");
                Field("onError", "Erro");
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Abrir Configurar Projeto (Setup)")) MetaDisplaySetupWindow.Open();
            serializedObject.ApplyModifiedProperties();
            if (Application.isPlaying) Repaint();
        }

        static bool Section(bool open, string title)
        {
            EditorGUILayout.Space(4);
            bool result = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
            EditorGUILayout.EndFoldoutHeaderGroup();
            return result;
        }

        static void DrawStatus(MetaDisplayManager manager)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(MetaWearables.IsNative ? "Óculos reais" : "Simulação no Editor", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Registro", MetaWearables.Registration.ToString());
                EditorGUILayout.LabelField("Sessão", MetaWearables.Session.ToString());
                EditorGUILayout.LabelField("Display", MetaGlassesDisplay.State.ToString());
#if METADISPLAY_UGUI
                EditorGUILayout.LabelField("Tela atual", GlassesScreen.Current != null ? GlassesScreen.Current.name : "-");
#endif
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!MetaWearables.IsRegistered && GUILayout.Button("Registrar")) manager.Register();
                    if (MetaWearables.Session != SessionState.Started && GUILayout.Button("Conectar")) manager.Connect();
                    if (MetaWearables.Session == SessionState.Started && GUILayout.Button("Desconectar")) manager.Disconnect();
                }
            }
        }

#if METADISPLAY_UGUI
        enum Destination
        {
            [InspectorName("Celular")] Phone,
            [InspectorName("Óculos")] Glasses,
            [InspectorName("Óculos + espelho no celular")] GlassesMirror,
            [InspectorName("Óculos + miniatura no celular")] GlassesThumbnail,
            [InspectorName("Misto (telas dentro)")] Mixed,
        }

        static void DrawUiMap()
        {
            EditorGUILayout.HelpBox(
                "Cada Canvas é UI do celular, a menos que tenha telas dos óculos (GlassesScreen).\n" +
                "Troque o destino abaixo, ou crie telas de 600x600 em GameObject > Meta Display > Tela dos Óculos.",
                MessageType.None);

            var canvases = MetaDisplayMenus.FindAll<Canvas>();
            int shown = 0;
            foreach (var canvas in canvases)
            {
                if (!canvas.isRootCanvas) continue;
                shown++;
                var screens = canvas.GetComponentsInChildren<GlassesScreen>(true);
                var own = canvas.GetComponent<GlassesScreen>();
                Destination current = own != null ? FromVisibility(own.phoneVisibility) : screens.Length > 0 ? Destination.Mixed : Destination.Phone;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(canvas.name, EditorStyles.linkLabel, GUILayout.MaxWidth(180))) EditorGUIUtility.PingObject(canvas.gameObject);
                        using (new EditorGUI.DisabledScope(current == Destination.Mixed))
                        {
                            var chosen = (Destination)EditorGUILayout.EnumPopup(current);
                            if (chosen != current && chosen != Destination.Mixed) SetCanvasDestination(canvas, chosen);
                        }
                    }
                    foreach (var s in screens)
                    {
                        if (s == own) continue;
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(16);
                            if (GUILayout.Button("▸ " + s.name, EditorStyles.linkLabel, GUILayout.MaxWidth(164))) Selection.activeObject = s.gameObject;
                            EditorGUI.BeginChangeCheck();
                            var vis = (GlassesScreen.PhoneVisibility)EditorGUILayout.EnumPopup(s.phoneVisibility);
                            if (EditorGUI.EndChangeCheck())
                            {
                                Undo.RecordObject(s, "Visibilidade no celular");
                                s.phoneVisibility = vis;
                                EditorUtility.SetDirty(s);
                            }
                            if (Application.isPlaying && GUILayout.Button("Mostrar", GUILayout.Width(64))) s.Show();
                        }
                    }
                }
            }
            if (shown == 0) EditorGUILayout.LabelField("Nenhum Canvas na cena.", EditorStyles.miniLabel);
            if (GUILayout.Button("+ Nova tela dos óculos")) MetaDisplayMenus.CreateScreenMenu();
        }

        static Destination FromVisibility(GlassesScreen.PhoneVisibility v) =>
            v == GlassesScreen.PhoneVisibility.Mirror ? Destination.GlassesMirror
            : v == GlassesScreen.PhoneVisibility.MirrorThumbnail ? Destination.GlassesThumbnail
            : Destination.Glasses;

        static void SetCanvasDestination(Canvas canvas, Destination destination)
        {
            var screen = canvas.GetComponent<GlassesScreen>();
            if (destination == Destination.Phone)
            {
                if (screen != null) Undo.DestroyObjectImmediate(screen);
                return;
            }
            if (screen == null)
            {
                screen = Undo.AddComponent<GlassesScreen>(canvas.gameObject);
                screen.screenId = canvas.name.ToLowerInvariant().Replace(' ', '-');
            }
            Undo.RecordObject(screen, "Destino do Canvas");
            screen.phoneVisibility = destination == Destination.GlassesMirror ? GlassesScreen.PhoneVisibility.Mirror
                : destination == Destination.GlassesThumbnail ? GlassesScreen.PhoneVisibility.MirrorThumbnail
                : GlassesScreen.PhoneVisibility.GlassesOnly;
            EditorUtility.SetDirty(screen);
        }
#endif
    }

#if METADISPLAY_UGUI
    [CustomEditor(typeof(GlassesScreen))]
    public class GlassesScreenEditor : UnityEditor.Editor
    {
        bool m_ShowJson;
        string m_Json;
        List<string> m_Warnings;
        double m_NextRefresh;

        public override void OnInspectorGUI()
        {
            var screen = (GlassesScreen)target;
            EditorGUILayout.HelpBox(
                "Tudo dentro deste objeto vira a UI dos óculos (600x600). Preto = transparente nos óculos.\n" +
                "Suportado: textos, botões, imagens, ícones e containers (FlexBox ou Layout Groups).",
                MessageType.None);

            DrawDefaultInspector();

            if (EditorApplication.timeSinceStartup > m_NextRefresh || m_Warnings == null)
            {
                m_NextRefresh = EditorApplication.timeSinceStartup + 1.0;
                m_Warnings = GlassesCanvasConverter.Validate(screen);
                if (m_ShowJson) m_Json = SafeJson(screen);
            }
            foreach (var w in m_Warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    if (GUILayout.Button(Application.isPlaying ? "Mostrar nos óculos" : "Mostrar nos óculos (Play)")) screen.Show();
                if (GUILayout.Button("Copiar JSON")) EditorGUIUtility.systemCopyBuffer = SafeJson(screen);
            }

            m_ShowJson = EditorGUILayout.Foldout(m_ShowJson, "JSON enviado aos óculos", true);
            if (m_ShowJson)
            {
                if (m_Json == null) m_Json = SafeJson(screen);
                EditorGUILayout.TextArea(m_Json, EditorStyles.wordWrappedMiniLabel);
            }
        }

        static string SafeJson(GlassesScreen screen)
        {
            try
            {
                return screen.BuildJson();
            }
            catch (System.Exception e)
            {
                return "Erro ao converter: " + e.Message;
            }
        }
    }

    [CustomEditor(typeof(GlassesIcon))]
    public class GlassesIconEditor : UnityEditor.Editor
    {
        string m_Filter = "";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var icon = (GlassesIcon)target;
            EditorGUILayout.Space();
            m_Filter = EditorGUILayout.TextField("Buscar ícone", m_Filter);
            if (string.IsNullOrEmpty(m_Filter)) return;
            int shown = 0;
            foreach (GlassesIconName name in System.Enum.GetValues(typeof(GlassesIconName)))
            {
                if (name.ToString().IndexOf(m_Filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(name.ToString(), EditorStyles.miniButton))
                {
                    Undo.RecordObject(icon, "Ícone");
                    icon.icon = name;
                    EditorUtility.SetDirty(icon);
                }
                if (++shown > 30) break;
            }
        }
    }
#endif
}
