using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Nó da UI dos óculos. Espelha o DSL do mwdat-display: flexBox, text, image, button, icon e buttonGroup.
    /// Monte com <see cref="Glasses"/> (código) ou desenhe num Canvas com <c>GlassesScreen</c>.
    /// </summary>
    public abstract class GlassesNode
    {
        /// <summary>Quanto o nó cresce para ocupar espaço livre (como CSS flex-grow).</summary>
        public float FlexGrow;
        public float FlexShrink;
        /// <summary>Sobrescreve o alinhamento no eixo cruzado do pai.</summary>
        public GlassesAlignment? AlignSelf;

        internal abstract string Type { get; }

        internal virtual void Write(GlassesJson w, GlassesSendContext ctx)
        {
            w.Prop("type", Type);
            if (FlexGrow != 0) w.Prop("flexGrow", FlexGrow);
            if (FlexShrink != 0) w.Prop("flexShrink", FlexShrink);
            if (AlignSelf.HasValue) w.Prop("alignSelf", MetaEnum.ToSdk(AlignSelf.Value));
        }

        /// <summary>Atalho fluente para FlexGrow.</summary>
        public GlassesNode Grow(float grow = 1f)
        {
            FlexGrow = grow;
            return this;
        }
    }

    /// <summary>Container flexbox (único tipo permitido na raiz).</summary>
    public sealed class FlexNode : GlassesNode
    {
        public GlassesDirection Direction = GlassesDirection.Column;
        public int Gap;
        public GlassesAlignment Alignment = GlassesAlignment.Start;
        public GlassesAlignment CrossAlignment = GlassesAlignment.Start;
        public bool Wrap;
        public int Padding;
        public int? PaddingTop, PaddingBottom, PaddingStart, PaddingEnd;
        public GlassesBackground Background = GlassesBackground.None;
        /// <summary>Torna o container inteiro clicável.</summary>
        public Action OnClick;
        public readonly List<GlassesNode> Children = new List<GlassesNode>();

        internal override string Type => "flex";

        public FlexNode Add(params GlassesNode[] nodes)
        {
            foreach (var n in nodes)
                if (n != null) Children.Add(n);
            return this;
        }

        public FlexNode Add(IEnumerable<GlassesNode> nodes)
        {
            foreach (var n in nodes)
                if (n != null) Children.Add(n);
            return this;
        }

        internal override void Write(GlassesJson w, GlassesSendContext ctx)
        {
            base.Write(w, ctx);
            w.Prop("direction", MetaEnum.ToSdk(Direction));
            if (Gap != 0) w.Prop("gap", Gap);
            w.Prop("alignment", MetaEnum.ToSdk(Alignment));
            w.Prop("crossAlignment", MetaEnum.ToSdk(CrossAlignment));
            if (Wrap) w.Prop("wrap", true);
            if (Padding != 0) w.Prop("padding", Padding);
            if (PaddingTop.HasValue) w.Prop("paddingTop", PaddingTop.Value);
            if (PaddingBottom.HasValue) w.Prop("paddingBottom", PaddingBottom.Value);
            if (PaddingStart.HasValue) w.Prop("paddingStart", PaddingStart.Value);
            if (PaddingEnd.HasValue) w.Prop("paddingEnd", PaddingEnd.Value);
            if (Background != GlassesBackground.None) w.Prop("background", MetaEnum.ToSdk(Background));
            var click = ctx.RegisterClick(OnClick);
            if (click != null) w.Prop("clickId", click);
            w.BeginArray("children");
            foreach (var child in Children)
            {
                w.BeginObject();
                child.Write(w, ctx);
                w.EndObject();
            }
            w.EndArray();
        }
    }

    public sealed class TextNode : GlassesNode
    {
        public string Text;
        public GlassesTextStyle Style = GlassesTextStyle.Body;
        public GlassesTextColor Color = GlassesTextColor.Primary;

        internal override string Type => "text";

        internal override void Write(GlassesJson w, GlassesSendContext ctx)
        {
            base.Write(w, ctx);
            w.Prop("text", Text ?? "");
            w.Prop("style", MetaEnum.ToSdk(Style));
            w.Prop("color", MetaEnum.ToSdk(Color));
        }
    }

    /// <summary>Imagem por URL HTTPS ou por textura/sprite do Unity (enviada como PNG uma única vez).</summary>
    public sealed class ImageNode : GlassesNode
    {
        public string Url;
        public Texture Texture;
        public Sprite Sprite;
        /// <summary>Chave de uma imagem já registrada com <see cref="MetaGlassesDisplay.RegisterImage(string, byte[], int)"/>.</summary>
        public string ImageKey;
        public GlassesImageSize Size = GlassesImageSize.Fill;
        public GlassesCornerRadius CornerRadius = GlassesCornerRadius.None;
        /// <summary>Maior lado, em pixels, ao enviar a textura (o display tem 600x600).</summary>
        public int MaxSize = 0;

        internal override string Type => "image";

        internal override void Write(GlassesJson w, GlassesSendContext ctx)
        {
            base.Write(w, ctx);
            int max = MaxSize > 0 ? MaxSize : (Size == GlassesImageSize.Icon ? 128 : 600);
            string key = ImageKey;
            if (string.IsNullOrEmpty(key) && Sprite != null)
                key = ctx.DryRun ? MetaGlassesDisplay.ImageKeyFor(Sprite, max) : MetaGlassesDisplay.RegisterImage(Sprite, max);
            if (string.IsNullOrEmpty(key) && Texture != null)
                key = ctx.DryRun ? MetaGlassesDisplay.ImageKeyFor(Texture, max) : MetaGlassesDisplay.RegisterImage(Texture, max);
            if (!string.IsNullOrEmpty(key)) w.Prop("imageKey", key);
            if (!string.IsNullOrEmpty(Url)) w.Prop("uri", Url);
            w.Prop("size", MetaEnum.ToSdk(Size));
            w.Prop("cornerRadius", MetaEnum.ToSdk(CornerRadius));
        }
    }

    public sealed class ButtonNode : GlassesNode
    {
        public string Label;
        public GlassesButtonStyle Style = GlassesButtonStyle.Primary;
        public GlassesIconName? Icon;
        public Action OnClick;
        /// <summary>A primeira ação "primária" recebe o foco quando a tela aparece.</summary>
        public bool Primary;

        internal override string Type => "button";

        internal override void Write(GlassesJson w, GlassesSendContext ctx)
        {
            base.Write(w, ctx);
            WriteButtonFields(w, ctx);
        }

        internal void WriteButtonFields(GlassesJson w, GlassesSendContext ctx)
        {
            w.Prop("label", Label ?? "");
            w.Prop("style", MetaEnum.ToSdk(Style));
            if (Icon.HasValue) w.Prop("icon", GlassesIconNames.ToSdk(Icon.Value));
            if (Primary) w.Prop("primary", true);
            var click = ctx.RegisterClick(OnClick);
            if (click != null) w.Prop("clickId", click);
        }
    }

    public sealed class IconNode : GlassesNode
    {
        public GlassesIconName Icon;
        public GlassesIconStyle Style = GlassesIconStyle.Filled;

        internal override string Type => "icon";

        internal override void Write(GlassesJson w, GlassesSendContext ctx)
        {
            base.Write(w, ctx);
            w.Prop("icon", GlassesIconNames.ToSdk(Icon));
            w.Prop("iconStyle", MetaEnum.ToSdk(Style));
        }
    }

    /// <summary>Grupo de botões com o espaçamento padrão do sistema dos óculos.</summary>
    public sealed class ButtonGroupNode : GlassesNode
    {
        public GlassesButtonGroupAlignment Alignment = GlassesButtonGroupAlignment.Center;
        public readonly List<ButtonNode> Buttons = new List<ButtonNode>();

        internal override string Type => "buttonGroup";

        public ButtonGroupNode Add(params ButtonNode[] buttons)
        {
            foreach (var b in buttons)
                if (b != null) Buttons.Add(b);
            return this;
        }

        internal override void Write(GlassesJson w, GlassesSendContext ctx)
        {
            base.Write(w, ctx);
            w.Prop("alignment", MetaEnum.ToSdk(Alignment));
            w.BeginArray("children");
            foreach (var b in Buttons)
            {
                w.BeginObject();
                b.WriteButtonFields(w, ctx);
                w.EndObject();
            }
            w.EndArray();
        }
    }

    /// <summary>
    /// Atalhos para montar telas por código:
    /// <code>
    /// MetaGlassesDisplay.Show(
    ///     Glasses.Column(gap: 12, padding: 24).Add(
    ///         Glasses.Text("Olá!", GlassesTextStyle.Heading),
    ///         Glasses.Button("OK", () => Debug.Log("toque"), icon: GlassesIconName.Checkmark, primary: true)));
    /// </code>
    /// </summary>
    public static class Glasses
    {
        public static FlexNode Column(int gap = 0, int padding = 0, GlassesBackground background = GlassesBackground.None,
            GlassesAlignment alignment = GlassesAlignment.Start, GlassesAlignment crossAlignment = GlassesAlignment.Start, Action onClick = null) =>
            new FlexNode { Direction = GlassesDirection.Column, Gap = gap, Padding = padding, Background = background, Alignment = alignment, CrossAlignment = crossAlignment, OnClick = onClick };

        public static FlexNode Row(int gap = 0, int padding = 0, GlassesBackground background = GlassesBackground.None,
            GlassesAlignment alignment = GlassesAlignment.Start, GlassesAlignment crossAlignment = GlassesAlignment.Center, Action onClick = null) =>
            new FlexNode { Direction = GlassesDirection.Row, Gap = gap, Padding = padding, Background = background, Alignment = alignment, CrossAlignment = crossAlignment, OnClick = onClick };

        /// <summary>Container com fundo de cartão.</summary>
        public static FlexNode Card(int gap = 8, int padding = 24, Action onClick = null) =>
            Column(gap, padding, GlassesBackground.Card, onClick: onClick);

        public static TextNode Text(string text, GlassesTextStyle style = GlassesTextStyle.Body, GlassesTextColor color = GlassesTextColor.Primary) =>
            new TextNode { Text = text, Style = style, Color = color };

        public static TextNode Heading(string text) => Text(text, GlassesTextStyle.Heading);

        public static TextNode Meta(string text) => Text(text, GlassesTextStyle.Meta, GlassesTextColor.Secondary);

        public static ButtonNode Button(string label, Action onClick = null, GlassesButtonStyle style = GlassesButtonStyle.Primary,
            GlassesIconName? icon = null, bool primary = false) =>
            new ButtonNode { Label = label, OnClick = onClick, Style = style, Icon = icon, Primary = primary };

        public static IconNode Icon(GlassesIconName icon, GlassesIconStyle style = GlassesIconStyle.Filled) =>
            new IconNode { Icon = icon, Style = style };

        public static ImageNode Image(Texture texture, GlassesImageSize size = GlassesImageSize.Fill, GlassesCornerRadius corner = GlassesCornerRadius.Medium) =>
            new ImageNode { Texture = texture, Size = size, CornerRadius = corner };

        public static ImageNode Image(Sprite sprite, GlassesImageSize size = GlassesImageSize.Fill, GlassesCornerRadius corner = GlassesCornerRadius.Medium) =>
            new ImageNode { Sprite = sprite, Size = size, CornerRadius = corner };

        /// <summary>Imagem por URL HTTPS (os óculos/SDK baixam a imagem).</summary>
        public static ImageNode ImageUrl(string url, GlassesImageSize size = GlassesImageSize.Fill, GlassesCornerRadius corner = GlassesCornerRadius.Medium) =>
            new ImageNode { Url = url, Size = size, CornerRadius = corner };

        public static ButtonGroupNode ButtonGroup(params ButtonNode[] buttons) => new ButtonGroupNode().Add(buttons);

        /// <summary>Espaço flexível que empurra os próximos itens para o fim.</summary>
        public static FlexNode Spacer() => new FlexNode { FlexGrow = 1 };
    }

    /// <summary>Estado de um envio: ids de clique e imagens usadas.</summary>
    internal sealed class GlassesSendContext
    {
        internal readonly Dictionary<string, Action> Clicks = new Dictionary<string, Action>();
        /// <summary>Só gera o JSON (para comparar/depurar), sem enviar imagens.</summary>
        internal bool DryRun;
        int m_Next;

        internal string RegisterClick(Action action)
        {
            if (action == null) return null;
            var id = "c" + (m_Next++).ToString(CultureInfo.InvariantCulture);
            Clicks[id] = action;
            return id;
        }
    }

    /// <summary>Gerador de JSON mínimo (sem dependências).</summary>
    internal sealed class GlassesJson
    {
        readonly StringBuilder m_Sb = new StringBuilder(1024);
        readonly Stack<bool> m_First = new Stack<bool>();

        internal void BeginObject()
        {
            Separator();
            m_Sb.Append('{');
            m_First.Push(true);
        }

        internal void EndObject()
        {
            m_Sb.Append('}');
            m_First.Pop();
        }

        internal void BeginArray(string name)
        {
            Key(name);
            m_Sb.Append('[');
            m_First.Push(true);
        }

        internal void EndArray()
        {
            m_Sb.Append(']');
            m_First.Pop();
        }

        internal void Prop(string name, string value)
        {
            Key(name);
            Escape(value);
        }

        internal void Prop(string name, int value)
        {
            Key(name);
            m_Sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        internal void Prop(string name, float value)
        {
            Key(name);
            m_Sb.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        }

        internal void Prop(string name, bool value)
        {
            Key(name);
            m_Sb.Append(value ? "true" : "false");
        }

        void Key(string name)
        {
            Separator();
            Escape(name);
            m_Sb.Append(':');
        }

        void Separator()
        {
            if (m_First.Count == 0) return;
            if (m_First.Peek()) { m_First.Pop(); m_First.Push(false); }
            else m_Sb.Append(',');
        }

        void Escape(string s)
        {
            m_Sb.Append('"');
            if (s != null)
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': m_Sb.Append("\\\""); break;
                        case '\\': m_Sb.Append("\\\\"); break;
                        case '\n': m_Sb.Append("\\n"); break;
                        case '\r': m_Sb.Append("\\r"); break;
                        case '\t': m_Sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) m_Sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else m_Sb.Append(c);
                            break;
                    }
                }
            m_Sb.Append('"');
        }

        public override string ToString() => m_Sb.ToString();

        /// <summary>Serializa a árvore. A raiz precisa ser um <see cref="FlexNode"/>.</summary>
        internal static string Serialize(GlassesNode root, GlassesSendContext ctx)
        {
            var flex = root as FlexNode ?? new FlexNode().Add(root);
            var w = new GlassesJson();
            w.BeginObject();
            flex.Write(w, ctx);
            w.EndObject();
            return w.ToString();
        }
    }
}
