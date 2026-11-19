using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MetaDisplay.Editor
{
    /// <summary>Meta Display > 3D menus: avatar sample scene, Web App export and MP4 clip recording.</summary>
    public static class Glasses3DMenus
    {
        public const string AvatarScenePath = "Assets/MetaDisplayAvatarDemo.unity";

        [MenuItem("Meta Display/3D/Create Avatar Sample Scene", false, 30)]
        static void CreateAvatarSceneMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateAvatarScene();
        }

        [MenuItem("Meta Display/3D/Export Web App (Three.js)...", false, 31)]
        static void ExportWebAppMenu() => ExportWebAppInteractive(FindView());

        [MenuItem("Meta Display/3D/Record MP4 Clips...", false, 32)]
        static void RecordClipsMenu() => RecordClipsInteractive(FindView(), Glasses3DVideoRecorder.DefaultSize, Glasses3DVideoRecorder.DefaultSize, 24);

        [MenuItem("GameObject/Meta Display/Glasses 3D View (show a 3D object)", false, 30)]
        static void CreateViewMenu()
        {
            var selected = Selection.activeTransform;
            var go = new GameObject("Glasses 3D View");
            Undo.RegisterCreatedObjectUndo(go, "Create Glasses 3D View");
            var view = go.AddComponent<Glasses3DView>();
            view.target = selected;
            go.AddComponent<Glasses3DInteraction>().view = view;
            Selection.activeObject = go;
        }

        static Glasses3DView FindView()
        {
            var selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<Glasses3DView>() : null;
            if (selected != null) return selected;
            var all = MetaDisplayMenus.FindAll<Glasses3DView>();
            return all.Length > 0 ? all[0] : null;
        }

        // ───────────── avatar sample scene ─────────────

        /// <summary>Creates and saves the avatar sample scene. Returns the scene path.</summary>
        public static string CreateAvatarScene(string path = AvatarScenePath, bool addToBuild = true)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var phoneCamera = Camera.main;
            if (phoneCamera != null)
            {
                phoneCamera.transform.position = new Vector3(0f, 1.15f, 4.4f);
                phoneCamera.transform.LookAt(new Vector3(0f, 1f, 0f));
                phoneCamera.clearFlags = CameraClearFlags.SolidColor;
                phoneCamera.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
            }

            // Light the avatar from the front and lift the shadows: on the glasses, dark means invisible.
            foreach (var light in MetaDisplayMenus.FindAll<Light>())
            {
                if (light.type != LightType.Directional) continue;
                light.transform.rotation = Quaternion.Euler(35f, 155f, 0f);
                light.intensity = 1.25f;
                light.color = Color.white;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.58f);

            new GameObject("Meta Display Manager").AddComponent<MetaDisplayManager>();

            var avatarGo = new GameObject("Avatar");
            var avatar = avatarGo.AddComponent<SampleAvatar>();
            avatar.EnsureBuilt();

            var viewGo = new GameObject("Glasses 3D View");
            var view = viewGo.AddComponent<Glasses3DView>();
            view.target = avatarGo.transform;
            view.caption = "Swipe to rotate me";
            view.poses = new List<Glasses3DPose>();
            foreach (var action in avatar.Actions)
            {
                float seconds = SampleAvatar.DurationOf(action);
                view.poses.Add(new Glasses3DPose
                {
                    name = action,
                    fps = 6f,
                    frames = Mathf.Max(2, Mathf.RoundToInt(seconds * 6f)),
                    loop = SampleAvatar.IsLoop(action),
                });
            }
            view.buttons = new List<Glasses3DButton>
            {
                new Glasses3DButton { label = "Wave", action = "wave", style = GlassesButtonStyle.Primary },
                new Glasses3DButton { label = "Dance", action = "happy" },
                new Glasses3DButton { label = "Spin", action = "spin" },
            };

            var interaction = viewGo.AddComponent<Glasses3DInteraction>();
            interaction.view = view;
            interaction.startSpeech = true;
            interaction.echoSpeech = false;

            var chat = viewGo.AddComponent<SampleAvatarChat>();
            chat.view = view;
            UnityEventTools.AddPersistentListener<string>(interaction.onHeard, chat.OnHeard);

            view.webAppLink = viewGo.AddComponent<GlassesWebAppLink>();

            EditorSceneManager.MarkSceneDirty(scene);
            if (EditorSceneManager.SaveScene(scene, path))
            {
                if (addToBuild)
                {
                    var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                    if (!scenes.Exists(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                Debug.Log("[MetaDisplay] Avatar sample scene created at " + path + ". Press Play and use the debug panel's 3D tab to compare the strategies.");
            }
            Selection.activeObject = viewGo;
            return path;
        }

        // ───────────── Web App ─────────────

        const string GlbPrefKey = "MetaDisplay.WebApp.GlbPath";
        const string FolderPrefKey = "MetaDisplay.WebApp.Folder";

        public static string LastGlbPath
        {
            get => EditorPrefs.GetString(GlbPrefKey, "");
            set => EditorPrefs.SetString(GlbPrefKey, value ?? "");
        }

        public static void ExportWebAppInteractive(Glasses3DView view)
        {
            var folder = EditorUtility.SaveFolderPanel("Export the Web App to…", EditorPrefs.GetString(FolderPrefKey, ""), "GlassesAvatarWebApp");
            if (string.IsNullOrEmpty(folder)) return;
            EditorPrefs.SetString(FolderPrefKey, folder);
            var options = Glasses3DWebAppExporter.OptionsFor(view);
            options.GlbPath = LastGlbPath;
            try
            {
                var report = Glasses3DWebAppExporter.Export(folder, options);
                Debug.Log("[MetaDisplay] " + report);
                if (EditorUtility.DisplayDialog("Web App exported", report + "\nNext: host the folder on HTTPS and add the URL in the Meta AI app (Apps > Web Apps). See README.md in the folder.", "Open folder", "Close"))
                    EditorUtility.RevealInFinder(System.IO.Path.Combine(folder, "index.html"));
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Web App export failed", e.Message, "OK");
            }
        }

        // ───────────── MP4 clips ─────────────

        public static void RecordClipsInteractive(Glasses3DView view, int width, int height, int fps)
        {
            if (view == null)
            {
                EditorUtility.DisplayDialog("Record MP4 clips", "Add a Glasses 3D View to the scene first.", "OK");
                return;
            }
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Record MP4 clips", "Enter Play Mode first: the clips are rendered from the running scene (poses, animations, lighting).", "OK");
                return;
            }
            var error = Glasses3DVideoRecorder.Validate(width, height);
            if (error != null)
            {
                EditorUtility.DisplayDialog("Record MP4 clips", error, "OK");
                return;
            }
            var folder = EditorUtility.SaveFolderPanel("Save the MP4 clips to…", "", "GlassesClips");
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                var files = Glasses3DVideoRecorder.RecordAll(view, folder, width, height, fps);
                Debug.Log($"[MetaDisplay] {files.Count} clips recorded in {folder}. Upload them to an HTTPS host and set Video base URL.");
                EditorUtility.RevealInFinder(folder);
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Recording failed", e.Message + "\n\nMP4 encoding needs the Windows or macOS Editor.", "OK");
            }
        }
    }
}
