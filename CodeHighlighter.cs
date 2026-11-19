using System.Text.RegularExpressions;
using Avalonia.Media;

namespace MdViewer;

// Resaltador genérico basado en regex (comentarios, strings, números y palabras clave comunes).
public static class CodeHighlighter
{
    static readonly string Keywords = string.Join('|', new[]
    {
        "abstract","as","async","await","base","bool","break","case","catch","char","class","const","continue",
        "def","default","del","delegate","do","double","elif","else","enum","except","export","extends","extern",
        "false","final","finally","fn","for","foreach","from","func","function","get","go","if","impl","implements",
        "import","in","int","interface","internal","is","lambda","let","long","match","namespace","new","nil","none",
        "null","object","operator","out","override","package","pass","private","protected","public","raise","readonly",
        "ref","return","sealed","select","self","set","static","string","struct","super","switch","this","throw",
        "true","try","type","typeof","using","val","var","virtual","void","where","while","with","yield","None","True","False",
        "SELECT","FROM","WHERE","INSERT","UPDATE","DELETE","CREATE","TABLE","JOIN","ON","AND","OR","NOT","INTO","VALUES"
    });

    static readonly HashSet<string> HashLangs = new(StringComparer.OrdinalIgnoreCase)
    {
        "python","py","bash","sh","shell","zsh","yaml","yml","ruby","rb","powershell","ps1","pwsh","toml","dockerfile","ini","r","perl"
    };

    static readonly Regex Slash = Build(false);
    static readonly Regex Hash = Build(true);

    static Regex Build(bool hash) => new(
        "(?<c>" + (hash ? @"#[^\n]*" : @"//[^\n]*|/\*[\s\S]*?\*/|<!--[\s\S]*?-->") + ")" +
        @"|(?<s>""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])*'|`(?:\\.|[^`\\])*`)" +
        @"|(?<n>\b\d+(?:\.\d+)?\b)" +
        @"|(?<k>\b(?:" + Keywords + @")\b)",
        RegexOptions.Compiled);

    public static IEnumerable<(string Text, IBrush? Brush)> Highlight(string code, string? lang, bool dark)
    {
        if (string.IsNullOrWhiteSpace(lang) || lang is "text" or "txt" or "plain" or "plaintext")
        {
            yield return (code, null);
            yield break;
        }

        var comment = Brush(dark ? "#6A9955" : "#008000");
        var str = Brush(dark ? "#CE9178" : "#A31515");
        var num = Brush(dark ? "#B5CEA8" : "#098658");
        var kw = Brush(dark ? "#569CD6" : "#0000FF");

        var regex = HashLangs.Contains(lang) ? Hash : Slash;
        var last = 0;
        foreach (Match m in regex.Matches(code))
        {
            if (m.Index > last) yield return (code[last..m.Index], null);
            var brush = m.Groups["c"].Success ? comment
                : m.Groups["s"].Success ? str
                : m.Groups["n"].Success ? num : kw;
            yield return (m.Value, brush);
            last = m.Index + m.Length;
        }
        if (last < code.Length) yield return (code[last..], null);
    }

    static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
