using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if METADISPLAY_UGUI
using TMPro;
using UnityEditor.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

namespace MetaDisplay.Editor
{
    /// <summary>GameObject > Meta Display menus and the sample scene.</summary>
    public static class MetaDisplayMenus
    {
        const string Root = "GameObject/Meta Display/";

        /// <summary>All objects of the type in the scene (including inactive ones). Works on Unity 6.0 to 6.6+.</summary>
        public static T[] FindAll<T>() where T : Object
        {
#pragma warning disable CS0618 // the overload without FindObjectsSortMode does not exist on Unity 6.0
            return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#pragma warning restore CS0618
        }

        [MenuItem(Root + "Meta Display Manager", false, 10)]
        public static void CreateManager()
        {
            var existing = Object.FindAnyObjectByType<MetaDisplayManager>();
            if (existing != null)
            {
                Selection.activeObject = existing;
                return;
            }
            var go = new GameObject("Meta Display Manager");
            Undo.RegisterCreatedObjectUndo(go, "Create Meta Display Manager");
            go.AddComponent<MetaDisplayManager>();
            Selection.activeObject = go;
        }

#if !METADISPLAY_UGUI
        [MenuItem(Root + "Glasses Screen (requires uGUI)", false, 20)]
        public static void CreateScreenMenu() => AskForUgui();

        public static void CreateDemoScene() => AskForUgui();

        static void AskForUgui()
        {
            if (EditorUtility.DisplayDialog("Meta Display", "Drawing the glasses UI on the Canvas needs the com.unity.ugui package (Canvas + TextMeshPro). Install it now?", "Install", "Cancel"))
                UnityEditor.PackageManager.Client.Add("com.unity.ugui");
        }
#else
        static readonly Color Black = Color.black;

        static bool UseTmp => AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;

        // ───────────── menus ─────────────

        [MenuItem(Root + "Glasses Screen (600x600)", false, 20)]
        public static void CreateScreenMenu()
        {
            var screen = CreateScreen("Screen " + (FindAll<GlassesScreen>().Length + 1), true);
            Selection.activeObject = screen.gameObject;
        }

        [MenuItem(Root + "Container - Column", false, 40)]
        static void MenuColumn() => Select(CreateFlex("Column", ResolveParent(), GlassesDirection.Column, GlassesBackground.None));

        [MenuItem(Root + "Container - Row", false, 41)]
        static void MenuRow() => Select(CreateFlex("Row", ResolveParent(), GlassesDirection.Row, GlassesBackground.None));

        [MenuItem(Root + "Container - Card", false, 42)]
        static void MenuCard() => Select(CreateFlex("Card", ResolveParent(), GlassesDirection.Column, GlassesBackground.Card));

        [MenuItem(Root + "Text - Heading", false, 60)]
        static void MenuHeading() => Select(CreateText("Heading", ResolveParent(), "Heading", GlassesTextStyle.Heading));

        [MenuItem(Root + "Text - Body", false, 61)]
        static void MenuBody() => Select(CreateText("Text", ResolveParent(), "Body text", GlassesTextStyle.Body));

        [MenuItem(Root + "Text - Caption (Meta)", false, 62)]
        static void MenuMeta() => Select(CreateText("Caption", ResolveParent(), "Caption", GlassesTextStyle.Meta, GlassesTextColor.Secondary));

        [MenuItem(Root + "Button", false, 80)]
        static void MenuButton() => Select(CreateButton("Button", ResolveParent(), "Button", GlassesButtonStyle.Primary));

        [MenuItem(Root + "Button Group", false, 81)]
        static void MenuButtonGroup()
        {
            var parent = ResolveParent();
            var go = NewUI("Button Group", parent);
            go.AddComponent<GlassesButtonGroup>();
            var lg = go.AddComponent<HorizontalLayoutGroup>();
            lg.spacing = 12;
            lg.childControlWidth = lg.childControlHeight = true;
            lg.childForceExpandWidth = lg.childForceExpandHeight = false;
            lg.childAlignment = TextAnchor.MiddleCenter;
            CreateButton("Button A", go.transform, "Yes", GlassesButtonStyle.Primary);
            CreateButton("Button B", go.transform, "No", GlassesButtonStyle.Secondary);
            Select(go);
        }

        [MenuItem(Root + "Image", false, 100)]
        static void MenuImage() => Select(CreateImage("Image", ResolveParent()));

        [MenuItem(Root + "Icon", false, 101)]
        static void MenuIcon() => Select(CreateIcon("Icon", ResolveParent(), GlassesIconName.Star));

        [MenuItem(Root + "Phone Only (GlassesIgnore)", false, 120)]
        static void MenuIgnore()
        {
            foreach (var go in Selection.gameObjects)
                if (go.GetComponent<GlassesIgnore>() == null) Undo.AddComponent<GlassesIgnore>(go);
        }

        [MenuItem("Meta Display/Create Sample Scene", false, 20)]
        public static void CreateDemoSceneMenu() => CreateDemoScene();

        // ───────────── building ─────────────

        static void Select(GameObject go)
        {
            if (go != null) Selection.activeObject = go;
        }

        static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            if (parent != null) go.transform.SetParent(parent, false);
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) go.layer = ui;
            return go;
        }

        static Transform ResolveParent()
        {
            var sel = Selection.activeGameObject;
            if (sel != null && sel.GetComponentInParent<GlassesScreen>(true) != null)
            {
                bool container = sel.GetComponent<GlassesScreen>() != null || sel.GetComponent<GlassesFlexBox>() != null || sel.GetComponent<LayoutGroup>() != null;
                return container ? sel.transform : sel.transform.parent;
            }
            var screen = Object.FindAnyObjectByType<GlassesScreen>(FindObjectsInactive.Include);
            return screen != null ? screen.transform : CreateScreen("Screen 1", false).transform;
        }

        static Canvas FindOrCreateGlassesCanvas()
        {
            foreach (var c in FindAll<Canvas>())
                if (c.isRootCanvas && c.name.StartsWith("Glasses")) return c;

            var go = new GameObject("Glasses (Canvas)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Create glasses Canvas");
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) go.layer = ui;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            go.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            EnsureEventSystem();
            return canvas;
        }

        static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            if (EditorApplication.ExecuteMenuItem("GameObject/UI/Event System")) return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        }

        public static GlassesScreen CreateScreen(string name, bool withSample, float x = 0)
        {
            var canvas = FindOrCreateGlassesCanvas();
            var go = NewUI(name, canvas.transform);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600, 600);
            rt.anchoredPosition = new Vector2(x, 0);
            var bg = go.AddComponent<Image>();
            bg.color = Black; // on the glasses, black = transparent
            var screen = go.AddComponent<GlassesScreen>();
            screen.screenId = name.ToLowerInvariant().Replace(' ', '-');
            screen.phoneVisibility = GlassesScreen.PhoneVisibility.Mirror;
            var fb = go.AddComponent<GlassesFlexBox>();
            fb.direction = GlassesDirection.Column;
            fb.gap = 16;
            fb.padding = 32;
            fb.crossAlignment = GlassesAlignment.Stretch;
            GlassesCanvasPreview.SyncLayout(fb);
            if (withSample)
            {
                CreateText("Heading", go.transform, name, GlassesTextStyle.Heading);
                CreateText("Text", go.transform, "Draw this screen on the Canvas: it becomes the glasses UI.", GlassesTextStyle.Body);
                CreateButton("Button", go.transform, "OK", GlassesButtonStyle.Primary, GlassesIconName.Checkmark, true);
            }
            return screen;
        }

        public static GameObject CreateFlex(string name, Transform parent, GlassesDirection direction, GlassesBackground background, int gap = 12, int padding = 0)
        {
            var go = NewUI(name, parent);
            var fb = go.AddComponent<GlassesFlexBox>();
            fb.direction = direction;
            fb.gap = gap;
            fb.background = background;
            fb.padding = background == GlassesBackground.Card ? 24 : padding;
            fb.crossAlignment = direction == GlassesDirection.Row ? GlassesAlignment.Center : GlassesAlignment.Stretch;
            GlassesCanvasPreview.SyncLayout(fb);
            return go;
        }

        static Graphic AddText(GameObject go, string text, float size, Color color, bool center)
        {
            if (UseTmp)
            {
                var t = go.AddComponent<TextMeshProUGUI>();
                t.text = text;
                t.fontSize = size;
                t.color = color;
                t.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
                return t;
            }
            var legacy = go.AddComponent<Text>();
            legacy.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            legacy.text = text;
            legacy.fontSize = Mathf.RoundToInt(size);
            legacy.color = color;
            legacy.alignment = center ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            legacy.horizontalOverflow = HorizontalWrapMode.Wrap;
            legacy.verticalOverflow = VerticalWrapMode.Overflow;
            return legacy;
        }

        public static GameObject CreateText(string name, Transform parent, string text, GlassesTextStyle style, GlassesTextColor color = GlassesTextColor.Primary)
        {
            var go = NewUI(name, parent);
            AddText(go, text, GlassesCanvasPreview.FontSize(style), color == GlassesTextColor.Primary ? GlassesCanvasPreview.PrimaryText : GlassesCanvasPreview.SecondaryText, false);
            var gt = go.AddComponent<GlassesText>();
            gt.style = style;
            gt.color = color;
            GlassesCanvasPreview.StyleText(gt);
            return go;
        }

        public static GameObject CreateButton(string name, Transform parent, string label, GlassesButtonStyle style, GlassesIconName? icon = null, bool primary = false)
        {
            var go = NewUI(name, parent);
            var img = go.AddComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            go.AddComponent<Button>();
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 64;
            le.preferredHeight = 64;
            le.minWidth = 160;
            var gb = go.AddComponent<GlassesButton>();
            gb.style = style;
            gb.primary = primary;
            if (icon.HasValue)
            {
                gb.showIcon = true;
                gb.icon = icon.Value;
            }

            var labelGo = NewUI("Text", go.transform);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16, 0);
            lrt.offsetMax = new Vector2(-16, 0);
            AddText(labelGo, (icon.HasValue ? "◆ " : "") + label, 24, Color.white, true);
            gb.label = label;
            GlassesCanvasPreview.StyleButton(gb);
            // StyleButton writes the label without the icon marker; put it back for the preview
            var tmp = labelGo.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = (icon.HasValue ? "◆ " : "") + label;
            var legacy = labelGo.GetComponent<Text>();
            if (legacy != null) legacy.text = (icon.HasValue ? "◆ " : "") + label;
            return go;
        }

        public static GameObject CreateImage(string name, Transform parent)
        {
            var go = NewUI(name, parent);
            var raw = go.AddComponent<RawImage>();
            raw.color = new Color(1, 1, 1, 0.9f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 220;
            le.preferredWidth = 220;
            var gi = go.AddComponent<GlassesImage>();
            gi.source = GlassesImage.ImageSource.Auto;
            return go;
        }

        public static GameObject CreateIcon(string name, Transform parent, GlassesIconName icon)
        {
            var go = NewUI(name, parent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 48;
            le.preferredWidth = 48;
            AddText(go, "★", 36, Color.white, true);
            var gi = go.AddComponent<GlassesIcon>();
            gi.icon = icon;
            return go;
        }

        // ───────────── sample scene ─────────────

        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateManager();
            var manager = Object.FindAnyObjectByType<MetaDisplayManager>();

            var home = CreateScreen("Home", false, 0);
            var counter = CreateScreen("Counter", false, 650);
            var about = CreateScreen("About", false, 1300);

            // Home
            var header = CreateFlex("Header", home.transform, GlassesDirection.Row, GlassesBackground.None, 12);
            CreateIcon("Icon", header.transform, GlassesIconName.SmartGlasses);
            CreateText("Heading", header.transform, "Hello, glasses!", GlassesTextStyle.Heading);
            CreateText("Text", home.transform, "This screen was drawn on the Unity Canvas and converted for the Meta Ray-Ban Display.", GlassesTextStyle.Body);
            CreateText("Hint", home.transform, "Tap the buttons with the touchpad or the Neural Band.", GlassesTextStyle.Meta, GlassesTextColor.Secondary);
            var homeButtons = CreateFlex("Buttons", home.transform, GlassesDirection.Row, GlassesBackground.None, 12);
            var toCounter = CreateButton("Counter Button", homeButtons.transform, "Counter", GlassesButtonStyle.Primary, GlassesIconName.Plus, true);
            var toAbout = CreateButton("About Button", homeButtons.transform, "About", GlassesButtonStyle.Secondary, GlassesIconName.ICircle);
            toCounter.GetComponent<GlassesButton>().navigateTo = counter;
            toAbout.GetComponent<GlassesButton>().navigateTo = about;

            // Counter
            var card = CreateFlex("Card", counter.transform, GlassesDirection.Column, GlassesBackground.Card, 8);
            CreateText("Heading", card.transform, "Counter", GlassesTextStyle.Heading);
            var value = CreateText("Value", card.transform, "Taps: 0", GlassesTextStyle.Body);
            var counterComp = counter.gameObject.AddComponent<GlassesCounter>();
            counterComp.target = value;
            var counterButtons = CreateFlex("Buttons", counter.transform, GlassesDirection.Row, GlassesBackground.None, 12);
            var plus = CreateButton("+1 Button", counterButtons.transform, "+1", GlassesButtonStyle.Primary, GlassesIconName.Plus, true);
            UnityEventTools.AddPersistentListener(plus.GetComponent<GlassesButton>().onClick, counterComp.Increment);
            var back1 = CreateButton("Back Button", counterButtons.transform, "Back", GlassesButtonStyle.Secondary, GlassesIconName.ArrowLeft);
            back1.GetComponent<GlassesButton>().navigateTo = home;

            // About
            var aboutCard = CreateFlex("Card", about.transform, GlassesDirection.Column, GlassesBackground.Card, 8);
            CreateText("Heading", aboutCard.transform, "Meta Display Toolkit", GlassesTextStyle.Heading);
            CreateText("Text", aboutCard.transform, "Unity + Meta Wearables Device Access Toolkit 1.0.1", GlassesTextStyle.Body);
            CreateText("Caption", aboutCard.transform, "Canvas texts, buttons, icons and images become native glasses UI.", GlassesTextStyle.Meta, GlassesTextColor.Secondary);
            var back2 = CreateButton("Back Button", about.transform, "Back", GlassesButtonStyle.Outline, GlassesIconName.ArrowLeft, true);
            back2.GetComponent<GlassesButton>().navigateTo = home;

            manager.initialScreen = home;

            // Phone-only UI
            var phone = new GameObject("Phone (Canvas)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(phone, "Phone Canvas");
            phone.layer = LayerMask.NameToLayer("UI");
            phone.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = phone.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            var note = NewUI("Note", phone.transform);
            var nrt = (RectTransform)note.transform;
            nrt.anchorMin = new Vector2(0, 0);
            nrt.anchorMax = new Vector2(1, 0);
            nrt.pivot = new Vector2(0.5f, 0);
            nrt.sizeDelta = new Vector2(-80, 220);
            nrt.anchoredPosition = new Vector2(0, 60);
            AddText(note, "PHONE UI (this Canvas does not go to the glasses).\nThe mirror in the center shows the current glasses screen.", 34, new Color(1, 1, 1, 0.8f), true);

            EditorSceneManager.MarkSceneDirty(scene);
            var path = "Assets/MetaDisplayDemo.unity";
            if (EditorSceneManager.SaveScene(scene, path))
            {
                var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                if (!scenes.Exists(s => s.path == path)) scenes.Insert(0, new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("[MetaDisplay] Sample scene created at " + path + " (added to the Build). Press Play to test in the Editor.");
            }
            Selection.activeObject = manager;
        }
#endif
    }
}
