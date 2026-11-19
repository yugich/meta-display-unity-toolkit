using UnityEditor;
using UnityEngine;

namespace MetaDisplay.Editor
{
    [CustomEditor(typeof(Glasses3DView))]
    public class Glasses3DViewEditor : UnityEditor.Editor
    {
        static readonly string[] s_StrategyHelp =
        {
            "LIVE FRAMES — a Unity camera renders the object and each frame goes to the glasses as a JPEG over Bluetooth.\n" +
            "+ Fully interactive (any animation, rotation, lighting) · − Few frames per second: check the metrics in Play Mode.\n" +
            "Smaller resolution and lower JPEG quality = more frames per second.",

            "BAKED FRAMES — each action is rendered once into a few JPEG frames, then played as a flipbook.\n" +
            "+ No render/encode cost on the phone, consistent timing (frames are skipped, never slowed) · − Same Bluetooth cost per frame; rotation is not baked.",

            "VIDEO CLIPS — each action is an MP4 played by the glasses' video player (≤ 400 px per side, ≤ 70,000 px, HTTPS).\n" +
            "+ Smooth motion · − Not real-time, takes the whole screen (no buttons/caption), a short gap when switching clips.\n" +
            "Record the clips below (Play Mode), upload them and set Video base URL.",

            "WEB APP — the object is rendered on the glasses by a Three.js Web App; Unity only sends commands through a relay.\n" +
            "+ Smooth, real-time, no Bluetooth video · − Separate app on the glasses (opened from the app grid), model must be glTF/GLB, needs HTTPS hosting.\n" +
            "Export the Web App below; the built-in robot works without a model.",
        };

        static bool s_ShowAdvanced;
        static int s_ClipSize = 264, s_ClipFps = 24;

        SerializedProperty P(string name) => serializedObject.FindProperty(name);

        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var view = (Glasses3DView)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(P("strategy"));
            var strategy = (Glasses3DStrategy)P("strategy").enumValueIndex;
            EditorGUILayout.HelpBox(s_StrategyHelp[(int)strategy], MessageType.None);

            Section("Object");
            EditorGUILayout.PropertyField(P("target"));
            if (view.target == null) EditorGUILayout.HelpBox("Choose the object (e.g. the avatar root) to show on the glasses.", MessageType.Warning);
            EditorGUILayout.PropertyField(P("idleAction"));
            EditorGUILayout.PropertyField(P("poses"), new GUIContent("Actions (poses)"), true);
            EditorGUILayout.PropertyField(P("showWhenDisplayReady"));

            switch (strategy)
            {
                case Glasses3DStrategy.LiveFrames:
                    ImageSection();
                    Section("Live frames");
                    EditorGUILayout.PropertyField(P("updateMode"));
                    EditorGUILayout.PropertyField(P("maxFps"));
                    EditorGUILayout.PropertyField(P("maxInFlight"));
                    if (view.updateMode == Glasses3DUpdateMode.OnChange) EditorGUILayout.PropertyField(P("changeThreshold"));
                    LayoutSection();
                    break;

                case Glasses3DStrategy.BakedFrames:
                    ImageSection();
                    Section("Baked frames");
                    EditorGUILayout.PropertyField(P("bakeOnStart"));
                    EditorGUILayout.PropertyField(P("maxFps"));
                    using (new EditorGUI.DisabledScope(!Application.isPlaying))
                        if (GUILayout.Button(view.IsBaking ? $"Baking… {view.BakeProgress:P0}" : "Bake now (Play Mode)")) view.Bake();
                    LayoutSection();
                    break;

                case Glasses3DStrategy.VideoClips:
                    Section("Video clips");
                    EditorGUILayout.PropertyField(P("videoBaseUrl"));
                    EditorGUILayout.HelpBox("Each action plays <base>/<action>.mp4, or the pose's own Video URL. Looping poses restart when the clip ends.", MessageType.None);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        s_ClipSize = EditorGUILayout.IntField("Clip size (square)", s_ClipSize);
                        s_ClipFps = EditorGUILayout.IntField("FPS", s_ClipFps, GUILayout.MinWidth(120));
                    }
                    var error = Glasses3DVideoRecorder.Validate(s_ClipSize, s_ClipSize);
                    if (error != null) EditorGUILayout.HelpBox(error, MessageType.Warning);
                    using (new EditorGUI.DisabledScope(!Application.isPlaying || error != null))
                        if (GUILayout.Button("Record MP4 clips of every action… (Play Mode)"))
                            Glasses3DMenus.RecordClipsInteractive(view, s_ClipSize, s_ClipSize, Mathf.Clamp(s_ClipFps, 5, 30));
                    break;

                case Glasses3DStrategy.WebApp:
                    Section("Web App");
                    EditorGUILayout.PropertyField(P("webAppLink"));
                    if (view.webAppLink == null && GUILayout.Button("Add a Glasses Web App Link"))
                    {
                        var link = Undo.AddComponent<GlassesWebAppLink>(view.gameObject);
                        P("webAppLink").objectReferenceValue = link;
                    }
                    else if (view.webAppLink != null)
                    {
                        EditorGUILayout.LabelField("Relay", string.IsNullOrEmpty(view.webAppLink.relayUrl) ? "(none: the Web App works alone)" : view.webAppLink.relayUrl);
                        if (Application.isPlaying) EditorGUILayout.LabelField("Status", view.webAppLink.Status);
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var glb = EditorGUILayout.TextField(new GUIContent("Model (.glb)", "Your avatar as GLB with animations. Empty = UnityGLTF export of the target if installed, else the built-in robot."), Glasses3DMenus.LastGlbPath);
                        if (GUILayout.Button("…", GUILayout.Width(28)))
                        {
                            var picked = EditorUtility.OpenFilePanel("Choose the avatar (.glb)", "", "glb");
                            if (!string.IsNullOrEmpty(picked)) glb = picked;
                        }
                        Glasses3DMenus.LastGlbPath = glb;
                    }
                    if (GUILayout.Button("Export Web App (Three.js)…")) Glasses3DMenus.ExportWebAppInteractive(view);
                    break;
            }

            s_ShowAdvanced = EditorGUILayout.Foldout(s_ShowAdvanced, "Camera, Editor test and events", true);
            if (s_ShowAdvanced)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(P("renderCamera"));
                EditorGUILayout.PropertyField(P("cullingMask"));
                EditorGUILayout.PropertyField(P("background"));
                EditorGUILayout.PropertyField(P("editorLinkKBps"), new GUIContent("Simulated link (KB/s)"));
                EditorGUILayout.PropertyField(P("editorLinkLatencyMs"), new GUIContent("Simulated latency (ms)"));
                EditorGUILayout.PropertyField(P("onAction"));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying) RuntimeSection(view);
        }

        void ImageSection()
        {
            Section("Image sent to the glasses");
            EditorGUILayout.PropertyField(P("resolution"));
            EditorGUILayout.PropertyField(P("supersample"));
            EditorGUILayout.PropertyField(P("encoding"));
            if ((Glasses3DEncoding)P("encoding").enumValueIndex == Glasses3DEncoding.DirectJpeg) EditorGUILayout.PropertyField(P("jpegQuality"));
        }

        void LayoutSection()
        {
            Section("Around the object on the glasses");
            EditorGUILayout.PropertyField(P("caption"));
            EditorGUILayout.PropertyField(P("captionStyle"));
            EditorGUILayout.PropertyField(P("buttons"), true);
        }

        static void Section(string title)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        static void RuntimeSection(Glasses3DView view)
        {
            Section("Play Mode");
            var m = view.Metrics;
            EditorGUILayout.HelpBox(
                $"{(view.IsShowing ? "Showing" : "Not showing")} · display {MetaGlassesDisplay.State} · action '{view.CurrentAction}'\n" +
                $"{m.Fps:0.0} fps · latency {m.LastLatencyMs:0} ms (avg {m.AvgLatencyMs:0}) · {m.LastBytes / 1024f:0.0} KB/frame (avg {m.AvgBytes / 1024f:0.0})\n" +
                $"capture {m.CaptureMs:0.0} ms · sent {m.FramesSent} · skipped {m.FramesSkipped} · failed {m.FramesFailed} · in flight {m.InFlight}",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (Glasses3DStrategy s in System.Enum.GetValues(typeof(Glasses3DStrategy)))
                {
                    using (new EditorGUI.DisabledScope(view.strategy == s))
                        if (GUILayout.Button(s.ToString(), EditorStyles.miniButton)) view.SetStrategy(s);
                }
            }

            var actions = view.Actions;
            if (actions.Count > 0)
            {
                EditorGUILayout.LabelField("Play an action", EditorStyles.miniBoldLabel);
                int perRow = 4;
                for (int i = 0; i < actions.Count; i += perRow)
                {
                    using (new EditorGUILayout.HorizontalScope())
                        for (int j = i; j < Mathf.Min(i + perRow, actions.Count); j++)
                            if (GUILayout.Button(actions[j], EditorStyles.miniButton)) view.Play(actions[j]);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("⟲ -30°", EditorStyles.miniButton)) view.Rotate(-30);
                if (GUILayout.Button("⟳ +30°", EditorStyles.miniButton)) view.Rotate(30);
                if (GUILayout.Button("Send frame", EditorStyles.miniButton)) view.SendFrameNow();
                if (GUILayout.Button("Reset metrics", EditorStyles.miniButton)) view.ResetMetrics();
            }

            if (view.Preview != null)
            {
                var rect = GUILayoutUtility.GetRect(160, 160, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(rect, view.Preview, null, ScaleMode.ScaleToFit);
            }
        }
    }
}
