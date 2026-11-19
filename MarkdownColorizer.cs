using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace MdViewer;

// Coloreado de sintaxis Markdown para el editor, con la paleta de VS Code.
public sealed class MarkdownColorizer : DocumentColorizingTransformer
{
    static readonly Regex Fence = new(@"^\s{0,3}(```|~~~)");
    static readonly Regex Heading = new(@"^\s{0,3}#{1,6}(\s|$)");
    static readonly Regex Quote = new(@"^\s{0,3}>");
    static readonly Regex ListMarker = new(@"^\s*(?:[-*+]|\d+[.)])(?=\s)(?:\s+\[[ xX]\])?");
    static readonly Regex Rule = new(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$");
    static readonly Regex InlineCode = new(@"`[^`\n]+`");
    static readonly Regex Bold = new(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1");
    static readonly Regex Italic = new(@"(?<![*_\w])([*_])(?![\s*_])(.+?)(?<![\s*_])\1(?![*_\w])");
    static readonly Regex Strike = new(@"~~(?=\S)(.+?)(?<=\S)~~");
    static readonly Regex Link = new(@"!?\[[^\]\n]*\]\([^)\n]*\)");

    byte[]? _map;

    public bool Dark { get; set; } = true;

    public void Invalidate() => _map = null;

    static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

    readonly IBrush _dHead = B("#569CD6"), _dQuote = B("#6A9955"), _dCode = B("#CE9178"), _dLink = B("#3794FF"), _dMuted = B("#808080"), _dMarker = B("#6796E6");
    readonly IBrush _lHead = B("#800000"), _lQuote = B("#008000"), _lCode = B("#A31515"), _lLink = B("#006AB1"), _lMuted = B("#808080"), _lMarker = B("#0451A5");

    static byte[] BuildMap(TextDocument doc)
    {
        var map = new byte[doc.LineCount + 2];
        var inFence = false;
        foreach (var l in doc.Lines)
        {
            if (Fence.IsMatch(doc.GetText(l.Offset, l.Length)))
            {
                map[l.LineNumber] = 1;
                inFence = !inFence;
            }
            else if (inFence) map[l.LineNumber] = 2;
        }
        return map;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0) return;
        var doc = CurrentContext.Document;
        if (_map == null || _map.Length != doc.LineCount + 2) _map = BuildMap(doc);
        var kind = _map[line.LineNumber];
        var text = doc.GetText(line.Offset, line.Length);
        var head = Dark ? _dHead : _lHead;
        var code = Dark ? _dCode : _lCode;
        var muted = Dark ? _dMuted : _lMuted;

        if (kind == 1) { Paint(line.Offset, line.EndOffset, muted); return; }
        if (kind == 2) { Paint(line.Offset, line.EndOffset, code); return; }

        if (Heading.IsMatch(text))
        {
            Paint(line.Offset, line.EndOffset, head, bold: true);
            return;
        }
        if (Rule.IsMatch(text)) { Paint(line.Offset, line.EndOffset, muted); return; }
        if (Quote.IsMatch(text)) Paint(line.Offset, line.EndOffset, Dark ? _dQuote : _lQuote);

        var marker = ListMarker.Match(text);
        if (marker.Success) Paint(line.Offset, line.Offset + marker.Length, Dark ? _dMarker : _lMarker, bold: true);

        foreach (Match m in Link.Matches(text)) Paint(line.Offset + m.Index, line.Offset + m.Index + m.Length, Dark ? _dLink : _lLink);
        foreach (Match m in Bold.Matches(text)) Paint(line.Offset + m.Index, line.Offset + m.Index + m.Length, null, bold: true);
        foreach (Match m in Italic.Matches(text)) Paint(line.Offset + m.Index, line.Offset + m.Index + m.Length, null, italic: true);
        foreach (Match m in Strike.Matches(text)) Paint(line.Offset + m.Index, line.Offset + m.Index + m.Length, muted);
        foreach (Match m in InlineCode.Matches(text)) Paint(line.Offset + m.Index, line.Offset + m.Index + m.Length, code);
    }

    void Paint(int start, int end, IBrush? brush, bool bold = false, bool italic = false)
    {
        if (end <= start) return;
        ChangeLinePart(start, end, element =>
        {
            if (brush != null) element.TextRunProperties.SetForegroundBrush(brush);
            if (bold || italic)
            {
                var tf = element.TextRunProperties.Typeface;
                element.TextRunProperties.SetTypeface(new Typeface(
                    tf.FontFamily,
                    italic ? FontStyle.Italic : tf.Style,
                    bold ? FontWeight.Bold : tf.Weight));
            }
        });
    }
}
