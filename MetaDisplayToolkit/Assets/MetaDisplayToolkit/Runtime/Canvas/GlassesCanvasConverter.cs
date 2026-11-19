#if METADISPLAY_UGUI
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetaDisplay
{
    /// <summary>
    /// Converts the hierarchy of a <see cref="GlassesScreen"/> into <see cref="GlassesNode"/>.
    /// Rules (per object, in hierarchy order):
    ///  - GlassesIgnore: skipped (with its children).
    ///  - GlassesButtonGroup: group with the child buttons.
    ///  - GlassesIcon: native icon.
    ///  - GlassesImage, RawImage with a texture or Image with a sprite (no children): image.
    ///  - GlassesButton or UI.Button: button (label = first child text).
    ///  - GlassesText, TextMeshPro or Text: text (style inferred from the font size if there is no GlassesText).
    ///  - GlassesFlexBox, Horizontal/Vertical/GridLayoutGroup or object with children: container.
    /// </summary>
    public static class GlassesCanvasConverter
    {
        /// <summary>Objects without "Glasses*" components are converted too (Text, Button, Image, LayoutGroup).</summary>
        public static bool AutoMapStandardUI = true;

        static readonly Regex s_RichText = new Regex("<[^>]+>", RegexOptions.Compiled);

        public static FlexNode Convert(GlassesScreen screen)
        {
            var root = BuildFlex(screen.gameObject);
            return root;
        }

        static GlassesNode ConvertObject(GameObject go)
        {
            if (go.GetComponent<GlassesIgnore>() != null) return null;
            if (go.GetComponent<GlassesScreen>() != null) return null; // nested screens are independent

            var group = go.GetComponent<GlassesButtonGroup>();
            if (group != null) return BuildButtonGroup(group);

            // Icon and image before text: the icon uses a text only for the Canvas preview.
            var gIcon = go.GetComponent<GlassesIcon>();
            if (gIcon != null)
            {
                var icon = new IconNode { Icon = gIcon.icon, Style = gIcon.style };
                gIcon.ApplyFlexChild(icon);
                return icon;
            }

            var gImage = go.GetComponent<GlassesImage>();
            if (gImage != null) return BuildImage(go, gImage);

            var gButton = go.GetComponent<GlassesButton>();
            var uiButton = go.GetComponent<Button>();
            if (gButton != null || (AutoMapStandardUI && uiButton != null)) return BuildButton(go, gButton, uiButton);

            var gText = go.GetComponent<GlassesText>();
            var tmp = go.GetComponent<TMP_Text>();
            var legacy = go.GetComponent<Text>();
            if (gText != null || (AutoMapStandardUI && (tmp != null || legacy != null))) return BuildText(gText, tmp, legacy);

            bool hasChildren = HasActiveChildren(go.transform);
            if (AutoMapStandardUI && !hasChildren)
            {
                var raw = go.GetComponent<RawImage>();
                if (raw != null && raw.texture != null) return new ImageNode { Texture = raw.texture, CornerRadius = GlassesCornerRadius.None };
                var img = go.GetComponent<Image>();
                if (img != null && img.sprite != null && img.color.a > 0.01f) return new ImageNode { Sprite = img.sprite, CornerRadius = GlassesCornerRadius.None };
            }

            if (go.GetComponent<GlassesFlexBox>() != null || go.GetComponent<LayoutGroup>() != null || hasChildren)
            {
                var flex = BuildFlex(go);
                bool plain = go.GetComponent<GlassesFlexBox>() == null && go.GetComponent<LayoutGroup>() == null;
                if (flex.Children.Count == 0) return null;
                // "Invisible" container with a single child: skip the nesting.
                if (plain && flex.Children.Count == 1 && flex.FlexGrow == 0) return flex.Children[0];
                return flex;
            }
            return null;
        }

        static bool HasActiveChildren(Transform t)
        {
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) return true;
            return false;
        }

        static FlexNode BuildFlex(GameObject go)
        {
            var node = new FlexNode();
            var fb = go.GetComponent<GlassesFlexBox>();
            if (fb != null)
            {
                node.Direction = fb.direction;
                node.Gap = fb.gap;
                node.Alignment = fb.alignment;
                node.CrossAlignment = fb.crossAlignment;
                node.Wrap = fb.wrap;
                node.Background = fb.background;
                if (fb.perEdgePadding)
                {
                    node.PaddingTop = fb.paddingTop;
                    node.PaddingBottom = fb.paddingBottom;
                    node.PaddingStart = fb.paddingStart;
                    node.PaddingEnd = fb.paddingEnd;
                }
                else node.Padding = fb.padding;
                if (fb.clickable) node.OnClick = fb.InvokeClick;
                fb.ApplyFlexChild(node);
            }
            else if (AutoMapStandardUI)
            {
                var hv = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
                var grid = go.GetComponent<GridLayoutGroup>();
                if (hv != null)
                {
                    bool row = hv is HorizontalLayoutGroup;
                    node.Direction = row
                        ? (hv.reverseArrangement ? GlassesDirection.RowReverse : GlassesDirection.Row)
                        : (hv.reverseArrangement ? GlassesDirection.ColumnReverse : GlassesDirection.Column);
                    node.Gap = Mathf.RoundToInt(hv.spacing);
                    ApplyPadding(node, hv.padding);
                    MapAnchor(hv.childAlignment, row, out node.Alignment, out node.CrossAlignment);
                    bool stretchCross = row ? hv.childControlHeight && hv.childForceExpandHeight : hv.childControlWidth && hv.childForceExpandWidth;
                    if (stretchCross) node.CrossAlignment = GlassesAlignment.Stretch;
                }
                else if (grid != null)
                {
                    node.Direction = GlassesDirection.Row;
                    node.Wrap = true;
                    node.Gap = Mathf.RoundToInt(grid.spacing.x);
                    ApplyPadding(node, grid.padding);
                }
                var le = go.GetComponent<LayoutElement>();
                if (le != null) node.FlexGrow = Mathf.Max(le.flexibleWidth, le.flexibleHeight, 0f);
            }

            var t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i).gameObject;
                if (!child.activeSelf) continue;
                var n = ConvertObject(child);
                if (n != null) node.Children.Add(n);
            }
            return node;
        }

        static void ApplyPadding(FlexNode node, RectOffset p)
        {
            if (p.left == p.right && p.top == p.bottom && p.left == p.top) node.Padding = p.left;
            else
            {
                node.PaddingTop = p.top;
                node.PaddingBottom = p.bottom;
                node.PaddingStart = p.left;
                node.PaddingEnd = p.right;
            }
        }

        static void MapAnchor(TextAnchor anchor, bool row, out GlassesAlignment main, out GlassesAlignment cross)
        {
            int col = (int)anchor % 3;  // 0 left, 1 center, 2 right
            int line = (int)anchor / 3; // 0 top, 1 middle, 2 bottom
            var horizontal = (GlassesAlignment)col;
            var vertical = (GlassesAlignment)line;
            main = row ? horizontal : vertical;
            cross = row ? vertical : horizontal;
        }

        static string ReadText(TMP_Text tmp, Text legacy)
        {
            string raw = tmp != null ? tmp.text : legacy != null ? legacy.text : "";
            return string.IsNullOrEmpty(raw) ? "" : s_RichText.Replace(raw, "");
        }

        static string FirstChildText(GameObject go)
        {
            var tmp = go.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) return ReadText(tmp, null);
            var legacy = go.GetComponentInChildren<Text>(true);
            return legacy != null ? ReadText(null, legacy) : "";
        }

        static TextNode BuildText(GlassesText gText, TMP_Text tmp, Text legacy)
        {
            var node = new TextNode();
            node.Text = gText != null && !string.IsNullOrEmpty(gText.overrideText) ? gText.overrideText : ReadText(tmp, legacy);
            if (gText != null)
            {
                node.Style = gText.style;
                node.Color = gText.color;
                gText.ApplyFlexChild(node);
            }
            else
            {
                float size = tmp != null ? tmp.fontSize : legacy != null ? legacy.fontSize : 24;
                node.Style = size >= 30 ? GlassesTextStyle.Heading : size <= 19 ? GlassesTextStyle.Meta : GlassesTextStyle.Body;
                var c = tmp != null ? tmp.color : legacy != null ? legacy.color : Color.white;
                node.Color = c.grayscale < 0.75f || c.a < 0.8f ? GlassesTextColor.Secondary : GlassesTextColor.Primary;
            }
            return node;
        }

        static ButtonNode BuildButton(GameObject go, GlassesButton gButton, Button uiButton)
        {
            var node = new ButtonNode();
            node.Label = gButton != null && !string.IsNullOrEmpty(gButton.label) ? gButton.label : FirstChildText(go);
            if (gButton != null)
            {
                node.Style = gButton.style;
                if (gButton.showIcon) node.Icon = gButton.icon;
                node.Primary = gButton.primary;
                node.OnClick = gButton.Click;
                gButton.ApplyFlexChild(node);
            }
            else if (uiButton != null)
            {
                node.OnClick = () => uiButton.onClick.Invoke();
            }
            return node;
        }

        static ButtonGroupNode BuildButtonGroup(GlassesButtonGroup group)
        {
            var node = new ButtonGroupNode { Alignment = group.alignment };
            group.ApplyFlexChild(node);
            var t = group.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i).gameObject;
                if (!child.activeSelf || child.GetComponent<GlassesIgnore>() != null) continue;
                var gb = child.GetComponent<GlassesButton>();
                var ub = child.GetComponent<Button>();
                if (gb != null || ub != null) node.Buttons.Add(BuildButton(child, gb, ub));
            }
            return node;
        }

        static GlassesNode BuildImage(GameObject go, GlassesImage gImage)
        {
            var node = new ImageNode { Size = gImage.size, CornerRadius = gImage.cornerRadius, MaxSize = gImage.maxSize };
            switch (gImage.source)
            {
                case GlassesImage.ImageSource.Url:
                    if (string.IsNullOrEmpty(gImage.url)) return null;
                    node.Url = gImage.url;
                    break;
                case GlassesImage.ImageSource.Texture:
                    if (gImage.texture == null) return null;
                    node.Texture = gImage.texture;
                    break;
                default:
                    var raw = go.GetComponent<RawImage>();
                    var img = go.GetComponent<Image>();
                    if (raw != null && raw.texture != null) node.Texture = raw.texture;
                    else if (img != null && img.sprite != null) node.Sprite = img.sprite;
                    else if (gImage.texture != null) node.Texture = gImage.texture;
                    else if (!string.IsNullOrEmpty(gImage.url)) node.Url = gImage.url;
                    else return null;
                    break;
            }
            gImage.ApplyFlexChild(node);
            return node;
        }

        /// <summary>Warnings about the screen (what will not look as expected on the glasses).</summary>
        public static List<string> Validate(GlassesScreen screen)
        {
            var warnings = new List<string>();
            int images = 0, texts = 0, buttons = 0;
            foreach (var t in screen.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<GlassesImage>() != null || t.GetComponent<RawImage>() != null) images++;
                if (t.GetComponent<TMP_Text>() != null || t.GetComponent<Text>() != null) texts++;
                if (t.GetComponent<Button>() != null || t.GetComponent<GlassesButton>() != null) buttons++;
                if (t.GetComponent<Toggle>() != null || t.GetComponent<Slider>() != null || t.GetComponent<TMP_InputField>() != null || t.GetComponent<InputField>() != null || t.GetComponent<Dropdown>() != null || t.GetComponent<TMP_Dropdown>() != null || t.GetComponent<Scrollbar>() != null)
                    warnings.Add($"'{t.name}': control not supported on the glasses (only text, image, button, icon and containers). Add GlassesIgnore if it is phone-only.");
            }
            if (images > 4) warnings.Add($"{images} images: many images make sending slow over Bluetooth.");
            if (buttons == 0) warnings.Add("No buttons: the user has nothing to tap (a two-finger Back ends the session on the root screen).");
            if (texts == 0 && images == 0) warnings.Add("The screen has no text and no image.");
            var rt = (RectTransform)screen.transform;
            var size = rt.rect.size;
            if (size.x > 1 && (Mathf.Abs(size.x - 600) > 1 || Mathf.Abs(size.y - 600) > 1))
                warnings.Add($"The screen area is {size.x:0}x{size.y:0}. The display is 600x600; use that size so the preview matches.");
            return warnings;
        }
    }

#if UNITY_EDITOR
    /// <summary>Makes the Canvas look like the result on the glasses (Editor only).</summary>
    internal static class GlassesCanvasPreview
    {
        internal static readonly Color CardColor = new Color(0.16f, 0.17f, 0.19f, 0.95f);
        internal static readonly Color PrimaryText = Color.white;
        internal static readonly Color SecondaryText = new Color(0.68f, 0.70f, 0.74f, 1f);
        internal static readonly Color PrimaryButton = new Color(0.30f, 0.52f, 1f, 1f);
        internal static readonly Color SecondaryButton = new Color(0.28f, 0.29f, 0.32f, 1f);

        internal static float FontSize(GlassesTextStyle style) =>
            style == GlassesTextStyle.Heading ? 34 : style == GlassesTextStyle.Meta ? 20 : 26;

        internal static void SyncLayout(GlassesFlexBox fb)
        {
            var go = fb.gameObject;
            bool row = fb.direction == GlassesDirection.Row || fb.direction == GlassesDirection.RowReverse;
            var existing = go.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (existing != null && (existing is HorizontalLayoutGroup) != row)
            {
                UnityEditor.Undo.DestroyObjectImmediate(existing);
                existing = null;
            }
            if (go.GetComponent<GridLayoutGroup>() != null) return; // the user chose a grid: leave it alone
            HorizontalOrVerticalLayoutGroup lg = existing;
            if (lg == null)
                lg = row ? (HorizontalOrVerticalLayoutGroup)UnityEditor.Undo.AddComponent<HorizontalLayoutGroup>(go)
                         : UnityEditor.Undo.AddComponent<VerticalLayoutGroup>(go);

            UnityEditor.Undo.RecordObject(lg, "Sync glasses layout");
            lg.spacing = fb.gap;
            lg.reverseArrangement = fb.direction == GlassesDirection.RowReverse || fb.direction == GlassesDirection.ColumnReverse;
            lg.padding = fb.perEdgePadding
                ? new RectOffset(fb.paddingStart, fb.paddingEnd, fb.paddingTop, fb.paddingBottom)
                : new RectOffset(fb.padding, fb.padding, fb.padding, fb.padding);
            lg.childControlWidth = true;
            lg.childControlHeight = true;
            lg.childForceExpandWidth = !row && fb.crossAlignment == GlassesAlignment.Stretch;
            lg.childForceExpandHeight = row && fb.crossAlignment == GlassesAlignment.Stretch;
            var main = fb.alignment == GlassesAlignment.Stretch ? GlassesAlignment.Start : fb.alignment;
            var cross = fb.crossAlignment == GlassesAlignment.Stretch ? GlassesAlignment.Start : fb.crossAlignment;
            int h = (int)(row ? main : cross);
            int v = (int)(row ? cross : main);
            lg.childAlignment = (TextAnchor)(v * 3 + h);
            UnityEditor.EditorUtility.SetDirty(lg);

            ApplyFlexGrow(fb);

            var image = go.GetComponent<Image>();
            if (fb.background == GlassesBackground.Card)
            {
                if (image == null) image = UnityEditor.Undo.AddComponent<Image>(go);
                UnityEditor.Undo.RecordObject(image, "Card background");
                if (image.sprite == null) image.sprite = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                image.type = Image.Type.Sliced;
                image.color = CardColor;
                image.enabled = true;
                UnityEditor.EditorUtility.SetDirty(image);
            }
            else if (image != null && image.color == CardColor)
            {
                UnityEditor.Undo.RecordObject(image, "No background");
                image.enabled = false;
            }
        }

        internal static void ApplyFlexGrow(GlassesElement element)
        {
            var parent = element.transform.parent != null ? element.transform.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
            if (element.flexGrow <= 0 && element.GetComponent<LayoutElement>() == null) return;
            var le = element.GetComponent<LayoutElement>();
            if (le == null) le = UnityEditor.Undo.AddComponent<LayoutElement>(element.gameObject);
            UnityEditor.Undo.RecordObject(le, "Flex grow");
            bool parentRow = parent is HorizontalLayoutGroup;
            le.flexibleWidth = parentRow ? element.flexGrow : -1;
            le.flexibleHeight = parentRow ? -1 : element.flexGrow;
            UnityEditor.EditorUtility.SetDirty(le);
        }

        internal static void StyleText(GlassesText gt)
        {
            var color = gt.color == GlassesTextColor.Primary ? PrimaryText : SecondaryText;
            var tmp = gt.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                UnityEditor.Undo.RecordObject(tmp, "Preview style");
                tmp.fontSize = FontSize(gt.style);
                tmp.fontStyle = gt.style == GlassesTextStyle.Heading ? FontStyles.Bold : FontStyles.Normal;
                tmp.color = color;
                UnityEditor.EditorUtility.SetDirty(tmp);
            }
            var legacy = gt.GetComponent<Text>();
            if (legacy != null)
            {
                UnityEditor.Undo.RecordObject(legacy, "Preview style");
                legacy.fontSize = Mathf.RoundToInt(FontSize(gt.style));
                legacy.fontStyle = gt.style == GlassesTextStyle.Heading ? FontStyle.Bold : FontStyle.Normal;
                legacy.color = color;
                UnityEditor.EditorUtility.SetDirty(legacy);
            }
            if (gt.flexGrow > 0) ApplyFlexGrow(gt);
        }

        internal static void StyleButton(GlassesButton gb)
        {
            var image = gb.GetComponent<Image>();
            if (image != null)
            {
                UnityEditor.Undo.RecordObject(image, "Preview style");
                image.color = gb.style == GlassesButtonStyle.Primary ? PrimaryButton
                    : gb.style == GlassesButtonStyle.Secondary ? SecondaryButton
                    : new Color(1, 1, 1, 0.08f);
                UnityEditor.EditorUtility.SetDirty(image);
            }
            var tmp = gb.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                UnityEditor.Undo.RecordObject(tmp, "Preview style");
                tmp.color = Color.white;
                tmp.fontSize = 24;
                if (!string.IsNullOrEmpty(gb.label)) tmp.text = gb.label;
                UnityEditor.EditorUtility.SetDirty(tmp);
            }
            var legacy = gb.GetComponentInChildren<Text>(true);
            if (legacy != null)
            {
                UnityEditor.Undo.RecordObject(legacy, "Preview style");
                legacy.color = Color.white;
                legacy.fontSize = 24;
                if (!string.IsNullOrEmpty(gb.label)) legacy.text = gb.label;
                UnityEditor.EditorUtility.SetDirty(legacy);
            }
            if (gb.flexGrow > 0) ApplyFlexGrow(gb);
        }
    }
#endif
}
#endif
