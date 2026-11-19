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
            else EditorGUILayout.HelpBox("Press Play to test in the Editor (simulated glasses) or build the APK to use the real glasses.", MessageType.None);

            s_Connection = Section(s_Connection, "Connection to the glasses");
            if (s_Connection)
            {
                Field("initializeOnStart", "Initialize on start");
                Field("registerAutomatically", "Register automatically");
                Field("connectWhenRegistered", "Connect when registered");
                Field("deviceSelection", "Which glasses");
                if (manager.deviceSelection == DeviceSelection.Specific) Field("specificDeviceId", "Device id");
            }

            s_Glasses = Section(s_Glasses, "Glasses display");
            if (s_Glasses)
            {
                Field("enableDisplay", "Use the display");
#if METADISPLAY_UGUI
                Field("initialScreen", "Initial screen");
                Field("restoreCurrentScreen", "Restore screen on reconnect");
                if (manager.initialScreen == null)
                    EditorGUILayout.HelpBox("Pick the initial screen (GlassesScreen) or create one in GameObject > Meta Display > Glasses Screen.", MessageType.Warning);
#else
                EditorGUILayout.HelpBox("Install the com.unity.ugui package to draw screens on the Canvas (Meta Display > Project Setup).", MessageType.Info);
#endif
            }

#if METADISPLAY_UGUI
            s_UiMap = Section(s_UiMap, "UI map: what goes to the phone and what goes to the glasses");
            if (s_UiMap) DrawUiMap();
#endif

            s_Capabilities = Section(s_Capabilities, "Capabilities started on connect");
            if (s_Capabilities)
            {
                Field("startCamera", "Camera");
                if (manager.startCamera)
                {
                    EditorGUI.indentLevel++;
                    Field("cameraQuality", "Quality");
                    Field("cameraFps", "FPS");
                    Field("cameraAudio", "Audio (experimental)");
                    EditorGUI.indentLevel--;
                }
                Field("startMotion", "IMU / Motion (experimental)");
                if (manager.startMotion)
                {
                    EditorGUI.indentLevel++;
                    Field("motionRate", "Rate");
                    EditorGUI.indentLevel--;
                }
                Field("startInputs", "Inputs (experimental)");
                if (manager.startInputs)
                {
                    EditorGUI.indentLevel++;
                    Field("inputsConsumeBack", "Capture Back");
                    EditorGUI.indentLevel--;
                }
                Field("startSpeech", "Speech (experimental)");
                Field("startVoiceInvocations", "\"Hey Meta\" (experimental)");
                EditorGUILayout.HelpBox("Experimental features work in Developer Mode and beta channels; they cannot ship to production yet.", MessageType.None);
            }

            s_Testing = Section(s_Testing, "Phone and testing");
            if (s_Testing)
            {
                Field("showPhoneDebugPanel", "Debug panel on the phone");
                Field("simulateInEditor", "Simulate glasses in the Editor");
                Field("useMockDevice", "Simulated glasses on the phone");
                if (manager.useMockDevice)
                {
                    EditorGUI.indentLevel++;
                    Field("mockShowDisplayPreview", "Display preview");
                    Field("mockPreviewSize", "Size (px)");
                    Field("mockPreviewCorner", "Corner");
                    EditorGUI.indentLevel--;
                    EditorGUILayout.HelpBox("With simulated glasses, the app does not use the real glasses. Turn it off before testing with the hardware.", MessageType.Warning);
                }
            }

            s_Events = Section(s_Events, "Events (no code)");
            if (s_Events)
            {
                Field("onInitialized", "On initialized");
                Field("onRegistered", "On registered");
                Field("onConnected", "On connected");
                Field("onDisplayReady", "Display ready");
                Field("onDisconnected", "On disconnected");
                Field("onError", "Error");
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Open Project Setup")) MetaDisplaySetupWindow.Open();
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
                EditorGUILayout.LabelField(MetaWearables.IsNative ? "Real glasses" : "Editor simulation", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Registration", MetaWearables.Registration.ToString());
                EditorGUILayout.LabelField("Session", MetaWearables.Session.ToString());
                EditorGUILayout.LabelField("Display", MetaGlassesDisplay.State.ToString());
#if METADISPLAY_UGUI
                EditorGUILayout.LabelField("Current screen", GlassesScreen.Current != null ? GlassesScreen.Current.name : "-");
#endif
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!MetaWearables.IsRegistered && GUILayout.Button("Register")) manager.Register();
                    if (MetaWearables.Session != SessionState.Started && GUILayout.Button("Connect")) manager.Connect();
                    if (MetaWearables.Session == SessionState.Started && GUILayout.Button("Disconnect")) manager.Disconnect();
                }
            }
        }

#if METADISPLAY_UGUI
        enum Destination
        {
            [InspectorName("Phone")] Phone,
            [InspectorName("Glasses")] Glasses,
            [InspectorName("Glasses + mirror on the phone")] GlassesMirror,
            [InspectorName("Glasses + thumbnail on the phone")] GlassesThumbnail,
            [InspectorName("Mixed (screens inside)")] Mixed,
        }

        static void DrawUiMap()
        {
            EditorGUILayout.HelpBox(
                "Each Canvas is phone UI unless it has glasses screens (GlassesScreen).\n" +
                "Change the destination below, or create 600x600 screens in GameObject > Meta Display > Glasses Screen.",
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
                                Undo.RecordObject(s, "Phone visibility");
                                s.phoneVisibility = vis;
                                EditorUtility.SetDirty(s);
                            }
                            if (Application.isPlaying && GUILayout.Button("Show", GUILayout.Width(64))) s.Show();
                        }
                    }
                }
            }
            if (shown == 0) EditorGUILayout.LabelField("No Canvas in the scene.", EditorStyles.miniLabel);
            if (GUILayout.Button("+ New glasses screen")) MetaDisplayMenus.CreateScreenMenu();
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
            Undo.RecordObject(screen, "Canvas destination");
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
                "Everything inside this object becomes the glasses UI (600x600). Black = transparent on the glasses.\n" +
                "Supported: texts, buttons, images, icons and containers (FlexBox or Layout Groups).",
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
                    if (GUILayout.Button(Application.isPlaying ? "Show on the glasses" : "Show on the glasses (Play)")) screen.Show();
                if (GUILayout.Button("Copy JSON")) EditorGUIUtility.systemCopyBuffer = SafeJson(screen);
            }

            m_ShowJson = EditorGUILayout.Foldout(m_ShowJson, "JSON sent to the glasses", true);
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
                return "Conversion error: " + e.Message;
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
            m_Filter = EditorGUILayout.TextField("Search icon", m_Filter);
            if (string.IsNullOrEmpty(m_Filter)) return;
            int shown = 0;
            foreach (GlassesIconName name in System.Enum.GetValues(typeof(GlassesIconName)))
            {
                if (name.ToString().IndexOf(m_Filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(name.ToString(), EditorStyles.miniButton))
                {
                    Undo.RecordObject(icon, "Icon");
                    icon.icon = name;
                    EditorUtility.SetDirty(icon);
                }
                if (++shown > 30) break;
            }
        }
    }
#endif
}
