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
    /// <summary>Menus GameObject > Meta Display e a cena de exemplo.</summary>
    public static class MetaDisplayMenus
    {
        const string Root = "GameObject/Meta Display/";

        /// <summary>Todos os objetos do tipo na cena (inclui inativos). Compatível com Unity 6.0 a 6.6+.</summary>
        public static T[] FindAll<T>() where T : Object
        {
#pragma warning disable CS0618 // a sobrecarga sem FindObjectsSortMode não existe no Unity 6.0
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
            Undo.RegisterCreatedObjectUndo(go, "Criar Meta Display Manager");
            go.AddComponent<MetaDisplayManager>();
            Selection.activeObject = go;
        }

#if !METADISPLAY_UGUI
        [MenuItem(Root + "Tela dos Óculos (requer uGUI)", false, 20)]
        public static void CreateScreenMenu() => AskForUgui();

        public static void CreateDemoScene() => AskForUgui();

        static void AskForUgui()
        {
            if (EditorUtility.DisplayDialog("Meta Display", "Desenhar a UI dos óculos no Canvas precisa do pacote com.unity.ugui (Canvas + TextMeshPro). Instalar agora?", "Instalar", "Cancelar"))
                UnityEditor.PackageManager.Client.Add("com.unity.ugui");
        }
#else
        static readonly Color Black = Color.black;

        static bool UseTmp => AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;

        // ───────────── menus ─────────────

        [MenuItem(Root + "Tela dos Óculos (600x600)", false, 20)]
        public static void CreateScreenMenu()
        {
            var screen = CreateScreen("Tela " + (FindAll<GlassesScreen>().Length + 1), true);
            Selection.activeObject = screen.gameObject;
        }

        [MenuItem(Root + "Container - Coluna", false, 40)]
        static void MenuColumn() => Select(CreateFlex("Coluna", ResolveParent(), GlassesDirection.Column, GlassesBackground.None));

        [MenuItem(Root + "Container - Linha", false, 41)]
        static void MenuRow() => Select(CreateFlex("Linha", ResolveParent(), GlassesDirection.Row, GlassesBackground.None));

        [MenuItem(Root + "Container - Cartão", false, 42)]
        static void MenuCard() => Select(CreateFlex("Cartão", ResolveParent(), GlassesDirection.Column, GlassesBackground.Card));

        [MenuItem(Root + "Texto - Título", false, 60)]
        static void MenuHeading() => Select(CreateText("Título", ResolveParent(), "Título", GlassesTextStyle.Heading));

        [MenuItem(Root + "Texto - Corpo", false, 61)]
        static void MenuBody() => Select(CreateText("Texto", ResolveParent(), "Texto do corpo", GlassesTextStyle.Body));

        [MenuItem(Root + "Texto - Legenda (Meta)", false, 62)]
        static void MenuMeta() => Select(CreateText("Legenda", ResolveParent(), "Legenda", GlassesTextStyle.Meta, GlassesTextColor.Secondary));

        [MenuItem(Root + "Botão", false, 80)]
        static void MenuButton() => Select(CreateButton("Botão", ResolveParent(), "Botão", GlassesButtonStyle.Primary));

        [MenuItem(Root + "Grupo de Botões", false, 81)]
        static void MenuButtonGroup()
        {
            var parent = ResolveParent();
            var go = NewUI("Grupo de Botões", parent);
            var group = go.AddComponent<GlassesButtonGroup>();
            var lg = go.AddComponent<HorizontalLayoutGroup>();
            lg.spacing = 12;
            lg.childControlWidth = lg.childControlHeight = true;
            lg.childForceExpandWidth = lg.childForceExpandHeight = false;
            lg.childAlignment = TextAnchor.MiddleCenter;
            CreateButton("Botão A", go.transform, "Sim", GlassesButtonStyle.Primary);
            CreateButton("Botão B", go.transform, "Não", GlassesButtonStyle.Secondary);
            Select(go);
        }

        [MenuItem(Root + "Imagem", false, 100)]
        static void MenuImage() => Select(CreateImage("Imagem", ResolveParent()));

        [MenuItem(Root + "Ícone", false, 101)]
        static void MenuIcon() => Select(CreateIcon("Ícone", ResolveParent(), GlassesIconName.Star));

        [MenuItem(Root + "Somente no celular (GlassesIgnore)", false, 120)]
        static void MenuIgnore()
        {
            foreach (var go in Selection.gameObjects)
                if (go.GetComponent<GlassesIgnore>() == null) Undo.AddComponent<GlassesIgnore>(go);
        }

        [MenuItem("Meta Display/Criar cena de exemplo", false, 20)]
        public static void CreateDemoSceneMenu() => CreateDemoScene();

        // ───────────── construção ─────────────

        static void Select(GameObject go)
        {
            if (go != null) Selection.activeObject = go;
        }

        static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Criar " + name);
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
            return screen != null ? screen.transform : CreateScreen("Tela 1", false).transform;
        }

        static Canvas FindOrCreateGlassesCanvas()
        {
            foreach (var c in FindAll<Canvas>())
                if (c.isRootCanvas && c.name.StartsWith("Óculos")) return c;

            var go = new GameObject("Óculos (Canvas)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Criar Canvas dos óculos");
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
            Undo.RegisterCreatedObjectUndo(es, "Criar EventSystem");
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
            bg.color = Black; // nos óculos, preto = transparente
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
                CreateText("Título", go.transform, name, GlassesTextStyle.Heading);
                CreateText("Texto", go.transform, "Desenhe esta tela no Canvas: ela vira a UI dos óculos.", GlassesTextStyle.Body);
                CreateButton("Botão", go.transform, "OK", GlassesButtonStyle.Primary, GlassesIconName.Checkmark, true);
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

            var labelGo = NewUI("Texto", go.transform);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16, 0);
            lrt.offsetMax = new Vector2(-16, 0);
            AddText(labelGo, (icon.HasValue ? "◆ " : "") + label, 24, Color.white, true);
            gb.label = label;
            GlassesCanvasPreview.StyleButton(gb);
            // StyleButton escreve o label sem o marcador do ícone; recoloca para o preview
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

        // ───────────── cena de exemplo ─────────────

        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CreateManager();
            var manager = Object.FindAnyObjectByType<MetaDisplayManager>();

            var home = CreateScreen("Início", false, 0);
            var counter = CreateScreen("Contador", false, 650);
            var about = CreateScreen("Sobre", false, 1300);

            // Início
            var header = CreateFlex("Cabeçalho", home.transform, GlassesDirection.Row, GlassesBackground.None, 12);
            CreateIcon("Ícone", header.transform, GlassesIconName.SmartGlasses);
            CreateText("Título", header.transform, "Olá, óculos!", GlassesTextStyle.Heading);
            CreateText("Texto", home.transform, "Esta tela foi desenhada no Canvas do Unity e convertida para o Meta Ray-Ban Display.", GlassesTextStyle.Body);
            CreateText("Dica", home.transform, "Toque nos botões pelo touchpad ou pela Neural Band.", GlassesTextStyle.Meta, GlassesTextColor.Secondary);
            var homeButtons = CreateFlex("Botões", home.transform, GlassesDirection.Row, GlassesBackground.None, 12);
            var toCounter = CreateButton("Botão Contador", homeButtons.transform, "Contador", GlassesButtonStyle.Primary, GlassesIconName.Plus, true);
            var toAbout = CreateButton("Botão Sobre", homeButtons.transform, "Sobre", GlassesButtonStyle.Secondary, GlassesIconName.ICircle);
            toCounter.GetComponent<GlassesButton>().navigateTo = counter;
            toAbout.GetComponent<GlassesButton>().navigateTo = about;

            // Contador
            var card = CreateFlex("Cartão", counter.transform, GlassesDirection.Column, GlassesBackground.Card, 8);
            CreateText("Título", card.transform, "Contador", GlassesTextStyle.Heading);
            var value = CreateText("Valor", card.transform, "Toques: 0", GlassesTextStyle.Body);
            var counterComp = counter.gameObject.AddComponent<GlassesCounter>();
            counterComp.target = value;
            var counterButtons = CreateFlex("Botões", counter.transform, GlassesDirection.Row, GlassesBackground.None, 12);
            var plus = CreateButton("Botão +1", counterButtons.transform, "+1", GlassesButtonStyle.Primary, GlassesIconName.Plus, true);
            UnityEventTools.AddPersistentListener(plus.GetComponent<GlassesButton>().onClick, counterComp.Increment);
            var back1 = CreateButton("Botão Voltar", counterButtons.transform, "Voltar", GlassesButtonStyle.Secondary, GlassesIconName.ArrowLeft);
            back1.GetComponent<GlassesButton>().navigateTo = home;

            // Sobre
            var aboutCard = CreateFlex("Cartão", about.transform, GlassesDirection.Column, GlassesBackground.Card, 8);
            CreateText("Título", aboutCard.transform, "Meta Display Toolkit", GlassesTextStyle.Heading);
            CreateText("Texto", aboutCard.transform, "Unity + Meta Wearables Device Access Toolkit 1.0.1", GlassesTextStyle.Body);
            CreateText("Legenda", aboutCard.transform, "Textos, botões, ícones e imagens do Canvas viram UI nativa dos óculos.", GlassesTextStyle.Meta, GlassesTextColor.Secondary);
            var back2 = CreateButton("Botão Voltar", about.transform, "Voltar", GlassesButtonStyle.Outline, GlassesIconName.ArrowLeft, true);
            back2.GetComponent<GlassesButton>().navigateTo = home;

            manager.initialScreen = home;

            // UI só do celular
            var phone = new GameObject("Celular (Canvas)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(phone, "Canvas do celular");
            phone.layer = LayerMask.NameToLayer("UI");
            phone.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = phone.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            var note = NewUI("Aviso", phone.transform);
            var nrt = (RectTransform)note.transform;
            nrt.anchorMin = new Vector2(0, 0);
            nrt.anchorMax = new Vector2(1, 0);
            nrt.pivot = new Vector2(0.5f, 0);
            nrt.sizeDelta = new Vector2(-80, 220);
            nrt.anchoredPosition = new Vector2(0, 60);
            AddText(note, "UI do CELULAR (este Canvas não vai para os óculos).\nO espelho no centro mostra a tela atual dos óculos.", 34, new Color(1, 1, 1, 0.8f), true);

            EditorSceneManager.MarkSceneDirty(scene);
            var path = "Assets/MetaDisplayDemo.unity";
            if (EditorSceneManager.SaveScene(scene, path))
            {
                var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                if (!scenes.Exists(s => s.path == path)) scenes.Insert(0, new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("[MetaDisplay] Cena de exemplo criada em " + path + " (adicionada ao Build). Dê Play para testar no Editor.");
            }
            Selection.activeObject = manager;
        }
#endif
    }
}
