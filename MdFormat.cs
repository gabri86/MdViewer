using System.Text.RegularExpressions;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace MdViewer;

public static class MdFormat
{
    static readonly Regex HeadingRx = new(@"^(\s{0,3})(#{1,6})\s*(.*)$");
    static readonly Regex AnyList = new(@"^(\s*)(?:[-*+]\s+\[[ xX]\]\s+|[-*+]\s+|\d+[.)]\s+)?(.*)$");
    static readonly Regex BulletRx = new(@"^(\s*)[-*+]\s+(?!\[[ xX]\])");
    static readonly Regex NumberRx = new(@"^(\s*)\d+[.)]\s+");
    static readonly Regex TaskRx = new(@"^(\s*)[-*+]\s+\[[ xX]\]\s+");
    static readonly Regex QuoteRx = new(@"^(\s*)>\s?");

    public static void Apply(TextEditor ed, string action)
    {
        switch (action)
        {
            case "bold": Wrap(ed, "**", "**", "negrita"); break;
            case "italic": Wrap(ed, "_", "_", "cursiva"); break;
            case "strike": Wrap(ed, "~~", "~~", "tachado"); break;
            case "code": Wrap(ed, "`", "`", "código"); break;
            case "h1": Heading(ed, 1); break;
            case "h2": Heading(ed, 2); break;
            case "h3": Heading(ed, 3); break;
            case "bullets": ListOp(ed, BulletRx, (l, _) => ListLine(l, "- ")); break;
            case "numbers": ListOp(ed, NumberRx, (l, n) => ListLine(l, n + ". ")); break;
            case "tasks": ListOp(ed, TaskRx, (l, _) => ListLine(l, "- [ ] ")); break;
            case "quote": ListOp(ed, QuoteRx, (l, _) => "> " + l); break;
            case "codeblock": CodeBlock(ed); break;
            case "link": Link(ed, false); break;
            case "image": Link(ed, true); break;
            case "table": Table(ed); break;
            case "hr": InsertBlock(ed, "---"); break;
        }
    }

    static string ListLine(string line, string marker)
    {
        var m = AnyList.Match(line);
        return m.Groups[1].Value + marker + m.Groups[2].Value;
    }

    static void Wrap(TextEditor ed, string left, string right, string placeholder)
    {
        var doc = ed.Document;
        int start = ed.SelectionStart, len = ed.SelectionLength;
        var sel = doc.GetText(start, len);
        int ll = left.Length, rl = right.Length;
        using (doc.RunUpdate())
        {
            if (len >= ll + rl && sel.StartsWith(left) && sel.EndsWith(right))
            {
                var inner = sel.Substring(ll, len - ll - rl);
                doc.Replace(start, len, inner);
                ed.Select(start, inner.Length);
            }
            else if (start >= ll && start + len + rl <= doc.TextLength &&
                     doc.GetText(start - ll, ll) == left && doc.GetText(start + len, rl) == right)
            {
                doc.Remove(start + len, rl);
                doc.Remove(start - ll, ll);
                ed.Select(start - ll, len);
            }
            else
            {
                var inner = len == 0 ? placeholder : sel;
                doc.Replace(start, len, left + inner + right);
                ed.Select(start + ll, inner.Length);
            }
        }
    }

    static (DocumentLine First, DocumentLine Last) SelectedLines(TextEditor ed)
    {
        var doc = ed.Document;
        int s = ed.SelectionStart, e = s + ed.SelectionLength;
        var first = doc.GetLineByOffset(s);
        var last = doc.GetLineByOffset(e);
        if (e > s && last.Offset == e && last.LineNumber > first.LineNumber) last = last.PreviousLine;
        return (first, last);
    }

    static string Newline(TextDocument doc)
    {
        foreach (var l in doc.Lines)
        {
            if (l.DelimiterLength == 2) return "\r\n";
            if (l.DelimiterLength == 1) return doc.GetText(l.EndOffset, 1);
        }
        return Environment.NewLine;
    }

    static void ReplaceLines(TextEditor ed, Func<List<string>, List<string>> transform)
    {
        var doc = ed.Document;
        var hadSelection = ed.SelectionLength > 0;
        var (first, last) = SelectedLines(ed);
        var lines = new List<string>();
        for (var l = first; ; l = l.NextLine)
        {
            lines.Add(doc.GetText(l.Offset, l.Length));
            if (l.LineNumber == last.LineNumber) break;
        }
        var text = string.Join(Newline(doc), transform(lines));
        using (doc.RunUpdate())
            doc.Replace(first.Offset, last.EndOffset - first.Offset, text);
        if (hadSelection) ed.Select(first.Offset, text.Length);
        else ed.CaretOffset = first.Offset + text.Length;
    }

    static void Heading(TextEditor ed, int level) => ReplaceLines(ed, lines => lines.Select(l =>
    {
        var m = HeadingRx.Match(l);
        var rest = m.Success ? m.Groups[3].Value : l.TrimStart();
        return m.Success && m.Groups[2].Length == level ? rest : new string('#', level) + " " + rest;
    }).ToList());

    static void ListOp(TextEditor ed, Regex own, Func<string, int, string> add) => ReplaceLines(ed, lines =>
    {
        var candidates = lines.Where(l => l.Trim().Length > 0).ToList();
        if (candidates.Count == 0) candidates = lines;
        var allOwn = candidates.All(own.IsMatch);
        var n = 1;
        return lines.Select(l =>
        {
            if (l.Trim().Length == 0 && lines.Count > 1) return l;
            return allOwn ? own.Replace(l, "$1") : add(l, n++);
        }).ToList();
    });

    static void CodeBlock(TextEditor ed)
    {
        var doc = ed.Document;
        var (first, last) = SelectedLines(ed);
        var nl = Newline(doc);
        var length = last.EndOffset - first.Offset;
        var text = doc.GetText(first.Offset, length);
        using (doc.RunUpdate())
        {
            if (text.Trim().Length == 0)
            {
                doc.Replace(first.Offset, length, "```" + nl + nl + "```");
                ed.CaretOffset = first.Offset + 3 + nl.Length;
            }
            else
            {
                doc.Replace(first.Offset, length, "```" + nl + text + nl + "```");
                ed.Select(first.Offset + 3 + nl.Length, text.Length);
            }
        }
    }

    static void Link(TextEditor ed, bool image)
    {
        var doc = ed.Document;
        int start = ed.SelectionStart, len = ed.SelectionLength;
        var sel = doc.GetText(start, len);
        var prefix = image ? "![" : "[";
        var isUrl = sel.StartsWith("http://") || sel.StartsWith("https://");
        var label = len == 0 || isUrl ? (image ? "descripción" : "texto") : sel;
        var url = isUrl ? sel : image ? "ruta/imagen.png" : "https://";
        var text = $"{prefix}{label}]({url})";
        using (doc.RunUpdate())
            doc.Replace(start, len, text);
        if (len > 0 && !isUrl) ed.Select(start + prefix.Length + label.Length + 2, url.Length);
        else ed.Select(start + prefix.Length, label.Length);
    }

    static void Table(TextEditor ed)
    {
        var nl = Newline(ed.Document);
        var text = string.Join(nl, "| Columna 1 | Columna 2 | Columna 3 |", "| --- | --- | --- |", "| Dato | Dato | Dato |");
        var offset = InsertBlock(ed, text);
        ed.Select(offset + 2, "Columna 1".Length);
    }

    static int InsertBlock(TextEditor ed, string text)
    {
        var doc = ed.Document;
        var (first, last) = SelectedLines(ed);
        var nl = Newline(doc);
        int offset;
        using (doc.RunUpdate())
        {
            if (first.LineNumber == last.LineNumber && doc.GetText(last.Offset, last.Length).Trim().Length == 0)
            {
                offset = last.Offset;
                doc.Replace(last.Offset, last.Length, text);
            }
            else
            {
                offset = last.EndOffset + nl.Length;
                doc.Insert(last.EndOffset, nl + text);
            }
        }
        ed.CaretOffset = offset + text.Length;
        return offset;
    }
}
