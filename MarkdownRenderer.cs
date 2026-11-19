using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MdViewer;

public class MarkdownRenderer
{
    sealed record LinkSpan(int Start, int End, string Url);

    sealed class InlineCtx
    {
        public readonly StringBuilder Text = new();
        public readonly List<LinkSpan> Links = new();
    }

    const string MonoFont = "Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace";

    readonly string _baseDir;
    readonly bool _dark;
    readonly Action<string> _onLink;
    readonly Dictionary<string, int> _slugCount = new();

    public List<(int Line, Control Control)> Anchors { get; } = new();
    public List<(SelectableTextBlock Block, string Text)> Texts { get; } = new();
    public Dictionary<string, Control> Headings { get; } = new();

    IBrush LinkBrush => new SolidColorBrush(Color.Parse(_dark ? "#3794FF" : "#006AB1"));
    IBrush CodeBg => new SolidColorBrush(_dark ? Color.FromArgb(102, 10, 10, 10) : Color.FromArgb(102, 220, 220, 220));
    IBrush Muted => new SolidColorBrush(_dark ? Color.FromArgb(160, 0, 122, 204) : Color.FromArgb(160, 0, 122, 204));

    public MarkdownRenderer(string baseDir, bool dark, Action<string> onLink)
    {
        _baseDir = baseDir;
        _dark = dark;
        _onLink = onLink;
    }

    public Control Render(MarkdownDocument doc)
    {
        var panel = new StackPanel { Spacing = 12 };
        RenderBlocks(doc, panel);
        return panel;
    }

    void RenderBlocks(ContainerBlock container, Panel parent)
    {
        foreach (var block in container)
        {
            var c = RenderBlock(block);
            if (c == null) continue;
            Anchors.Add((block.Line, c));
            parent.Children.Add(c);
        }
    }

    Control? RenderBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var size = h.Level switch { 1 => 28, 2 => 22, 3 => 18, 4 => 16, _ => 14 };
                var tb = CreateText(h.Inline, size, FontWeight.Bold);
                if (h.Level <= 2) tb.Margin = new Thickness(0, 8, 0, 0);
                var slug = Slugify(PlainText(h.Inline));
                if (slug.Length > 0)
                {
                    var n = _slugCount.GetValueOrDefault(slug);
                    _slugCount[slug] = n + 1;
                    Headings.TryAdd(n == 0 ? slug : $"{slug}-{n}", tb);
                }
                return tb;
            }
            case ParagraphBlock p:
                return CreateText(p.Inline, 14);
            case QuoteBlock q:
            {
                var inner = new StackPanel { Spacing = 8 };
                RenderBlocks(q, inner);
                return new Border
                {
                    BorderBrush = Muted,
                    BorderThickness = new Thickness(4, 0, 0, 0),
                    Padding = new Thickness(12, 2, 0, 2),
                    Child = inner
                };
            }
            case ListBlock l:
                return RenderList(l);
            case Table t:
                return RenderTable(t);
            case FencedCodeBlock fc:
                return RenderCode(CodeText(fc), fc.Info);
            case CodeBlock cb:
                return RenderCode(CodeText(cb), null);
            case ThematicBreakBlock:
                return new Border { Height = 1, Background = Muted, Margin = new Thickness(0, 6) };
            case ContainerBlock cont:
            {
                var inner = new StackPanel { Spacing = 12 };
                RenderBlocks(cont, inner);
                return inner;
            }
            default:
                return null;
        }
    }

    static string CodeText(LeafBlock b) =>
        string.Join("\n", b.Lines.Lines.Take(b.Lines.Count).Select(l => l.ToString()));

    Control RenderList(ListBlock list)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
        var number = int.TryParse(list.OrderedStart, out var s) ? s : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var marker = new TextBlock
            {
                Text = list.IsOrdered ? $"{number++}." : "•",
                MinWidth = 22,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Top
            };
            var content = new StackPanel { Spacing = 6 };
            RenderBlocks(item, content);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(content, 1);
            row.Children.Add(marker);
            row.Children.Add(content);
            Anchors.Add((item.Line, row));
            panel.Children.Add(row);
        }
        return panel;
    }

    Control RenderTable(Table table)
    {
        var grid = new Grid();
        var cols = table.ColumnDefinitions.Count;
        for (var i = 0; i < cols; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        var r = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var c = 0;
            foreach (var cell in row.OfType<TableCell>())
            {
                var inner = new StackPanel();
                foreach (var b in cell)
                {
                    if (b is ParagraphBlock p)
                        inner.Children.Add(CreateText(p.Inline, 14, row.IsHeader ? FontWeight.Bold : FontWeight.Normal));
                }
                var border = new Border
                {
                    BorderBrush = Muted,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 4),
                    Background = row.IsHeader ? CodeBg : null,
                    Child = inner
                };
                Grid.SetRow(border, r);
                Grid.SetColumn(border, c++);
                grid.Children.Add(border);
            }
            r++;
        }
        return grid;
    }

    Control RenderCode(string code, string? lang)
    {
        var tb = new SelectableTextBlock { FontFamily = new FontFamily(MonoFont), FontSize = 13 };
        foreach (var (text, brush) in CodeHighlighter.Highlight(code, lang?.Split(' ')[0], _dark))
        {
            var run = new Run(text);
            if (brush != null) run.Foreground = brush;
            tb.Inlines!.Add(run);
        }
        Texts.Add((tb, code));
        return new Border
        {
            Background = CodeBg,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = tb
            }
        };
    }

    SelectableTextBlock CreateText(ContainerInline? inline, double size, FontWeight weight = FontWeight.Normal)
    {
        var tb = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = weight };
        var ctx = new InlineCtx();
        if (inline != null) AddInlines(tb.Inlines!, inline, ctx);
        Texts.Add((tb, ctx.Text.ToString()));

        if (ctx.Links.Count > 0)
        {
            LinkSpan? Hit(PointerEventArgs e)
            {
                var hit = tb.TextLayout.HitTestPoint(e.GetPosition(tb));
                if (!hit.IsInside) return null;
                return ctx.Links.FirstOrDefault(l => hit.TextPosition >= l.Start && hit.TextPosition < l.End);
            }

            tb.PointerMoved += (_, e) => tb.Cursor = Hit(e) != null ? new Cursor(StandardCursorType.Hand) : null;
            tb.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left || tb.SelectionStart != tb.SelectionEnd) return;
                if (Hit(e) is { } link) _onLink(link.Url);
            };
        }
        return tb;
    }

    void AddInlines(InlineCollection target, ContainerInline container, InlineCtx ctx)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                {
                    var text = lit.Content.ToString();
                    target.Add(new Run(text));
                    ctx.Text.Append(text);
                    break;
                }
                case CodeInline code:
                    target.Add(new Run(code.Content) { FontFamily = new FontFamily(MonoFont), Background = CodeBg });
                    ctx.Text.Append(code.Content);
                    break;
                case LineBreakInline lb:
                    if (lb.IsHard) target.Add(new LineBreak());
                    else target.Add(new Run(" "));
                    ctx.Text.Append(lb.IsHard ? '\n' : ' ');
                    break;
                case EmphasisInline em:
                {
                    var span = new Span();
                    if (em.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (em.DelimiterCount >= 2) span.FontWeight = FontWeight.Bold;
                    else span.FontStyle = FontStyle.Italic;
                    AddInlines(span.Inlines!, em, ctx);
                    target.Add(span);
                    break;
                }
                case LinkInline link when link.IsImage:
                    AddImage(target, link, ctx);
                    break;
                case LinkInline link:
                {
                    var span = new Span { Foreground = LinkBrush, TextDecorations = TextDecorations.Underline };
                    var start = ctx.Text.Length;
                    AddInlines(span.Inlines!, link, ctx);
                    target.Add(span);
                    if (!string.IsNullOrEmpty(link.Url)) ctx.Links.Add(new LinkSpan(start, ctx.Text.Length, link.Url));
                    break;
                }
                case AutolinkInline auto:
                {
                    target.Add(new Run(auto.Url) { Foreground = LinkBrush, TextDecorations = TextDecorations.Underline });
                    ctx.Links.Add(new LinkSpan(ctx.Text.Length, ctx.Text.Length + auto.Url.Length, auto.Url));
                    ctx.Text.Append(auto.Url);
                    break;
                }
                case TaskList task:
                    target.Add(new Run(task.Checked ? "☑ " : "☐ "));
                    ctx.Text.Append("☑ ");
                    break;
                case HtmlInline html:
                    if (html.Tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase))
                    {
                        target.Add(new LineBreak());
                        ctx.Text.Append('\n');
                    }
                    break;
                case ContainerInline other:
                    AddInlines(target, other, ctx);
                    break;
            }
        }
    }

    void AddImage(InlineCollection target, LinkInline link, InlineCtx ctx)
    {
        var alt = PlainText(link);
        Bitmap? bmp = null;
        var url = link.Url ?? "";
        if (!url.Contains("://"))
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(_baseDir, Uri.UnescapeDataString(url.Split('#')[0])));
                if (File.Exists(path)) bmp = new Bitmap(path);
            }
            catch { }
        }

        if (bmp != null)
        {
            target.Add(new InlineUIContainer(new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                MaxWidth = 800,
                MaxHeight = 600
            }));
            ctx.Text.Append('\uFFFC');
        }
        else
        {
            var text = $"[{(alt.Length > 0 ? alt : "imagen")}]";
            target.Add(new Run(text) { Foreground = Muted });
            ctx.Text.Append(text);
        }
    }

    static string PlainText(ContainerInline? c)
    {
        if (c == null) return "";
        var sb = new StringBuilder();
        foreach (var i in c)
        {
            switch (i)
            {
                case LiteralInline l: sb.Append(l.Content.ToString()); break;
                case CodeInline code: sb.Append(code.Content); break;
                case ContainerInline cc: sb.Append(PlainText(cc)); break;
            }
        }
        return sb.ToString();
    }

    public static string Slugify(string text)
    {
        var s = text.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^\p{L}\p{N}\s_-]", "");
        return Regex.Replace(s, @"\s", "-");
    }
}
