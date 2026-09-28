using System.Text;
using System.Text.RegularExpressions;
using CAD_ET_HANS.Core.Models;

namespace CAD_ET_HANS.Core.Services;

/// <summary>
/// Express 目录下 .dcl（对话框）与 .lsp（命令提示）的规则化汉化。
/// 只处理界面文本：DCL 的 label/text/title，LSP 中 UI 函数调用内的字符串字面量。
/// 函数名、命令名、(command ...) 参数、initget 关键字等一律不动。
/// </summary>
public sealed class TextFilePatcher
{
    static TextFilePatcher()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>承载界面文本的函数调用。</summary>
    private static readonly string[] LspUiFunctions =
    {
        "prompt", "princ", "alert", "terpri", "acet-ui-status",
        "getstring", "getkword", "getpoint", "getcorner", "getdist",
        "getreal", "getint", "getangle", "getorient"
    };

    private static readonly Regex CallStartRegex = new(
        @"\(\s*(" + string.Join("|", LspUiFunctions.Select(Regex.Escape)) + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LiteralRegex = new(
        @"""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex DclLabelRegex = new(
        @"(?im)^([ \t]*)(label|text|title)([ \t]*)=([ \t]*)""((?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled);

    /// <summary>AutoCAD 读取 LSP/DCL 使用的系统 ANSI 编码（中文环境即 GBK），GB18030 为其超集。</summary>
    public static Encoding AnsiEncoding => Encoding.GetEncoding("GB18030");

    public static Encoding Latin1 => Encoding.Latin1;

    public static Encoding ResolveEncoding(bool utf8WithBom)
        => utf8WithBom ? new UTF8Encoding(true) : AnsiEncoding;

    // ---------------- 扫描 ----------------

    public static List<TranslationEntry> ScanLsp(string directory)
    {
        var result = new List<TranslationEntry>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.lsp"))
        {
            var text = File.ReadAllText(file, Latin1);
            foreach (var lit in EnumerateUiLiterals(text))
            {
                var (_, core, _) = SplitDecorations(lit.Text);
                if (string.IsNullOrWhiteSpace(core)) continue;
                result.Add(new TranslationEntry(EntryKind.LspPrompt, core, Path.GetFileName(file)));
            }
        }
        return Dedupe(result);
    }

    public static List<TranslationEntry> ScanDcl(string directory)
    {
        var result = new List<TranslationEntry>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.dcl"))
        {
            var text = File.ReadAllText(file, Latin1);
            foreach (Match m in DclLabelRegex.Matches(text))
            {
                var value = m.Groups[5].Value;
                if (string.IsNullOrWhiteSpace(value)) continue;
                result.Add(new TranslationEntry(EntryKind.DclLabel, value, Path.GetFileName(file)));
            }
        }
        return Dedupe(result);
    }

    // ---------------- 改写 ----------------

    public static int ApplyLspFile(
        string file,
        IReadOnlyDictionary<string, string> map,
        bool utf8WithBom,
        Action<string>? log = null)
    {
        return Apply(file, utf8WithBom, text =>
        {
            var replacements = new List<(int index, int length, string value)>();
            foreach (var lit in EnumerateUiLiterals(text))
            {
                var (prefix, core, suffix) = SplitDecorations(lit.Text);
                if (string.IsNullOrWhiteSpace(core)) continue;
                if (!map.TryGetValue(DictionaryService.Key(EntryKind.LspPrompt, core), out var zh)) continue;
                // 只替换引号内的内容，保留两侧的引号
                replacements.Add((lit.Start + 1, lit.Length - 2, prefix + zh + suffix));
            }
            return replacements;
        }, log);
    }

    public static int ApplyDclFile(
        string file,
        IReadOnlyDictionary<string, string> map,
        bool utf8WithBom,
        Action<string>? log = null)
    {
        return Apply(file, utf8WithBom, text =>
        {
            var replacements = new List<(int index, int length, string value)>();
            foreach (Match m in DclLabelRegex.Matches(text))
            {
                var value = m.Groups[5].Value;
                if (string.IsNullOrWhiteSpace(value)) continue;
                if (!map.TryGetValue(DictionaryService.Key(EntryKind.DclLabel, value), out var zh)) continue;
                replacements.Add((m.Groups[5].Index, value.Length, zh));
            }
            return replacements;
        }, log);
    }

    private static int Apply(
        string file,
        bool utf8WithBom,
        Func<string, List<(int index, int length, string value)>> collector,
        Action<string>? log)
    {
        var original = File.ReadAllText(file, Latin1);
        var replacements = collector(original);
        if (replacements.Count == 0) return 0;

        var sb = new StringBuilder(original);
        foreach (var r in replacements.OrderByDescending(r => r.index))
        {
            sb.Remove(r.index, r.length);
            sb.Insert(r.index, r.value);
        }

        var encoding = ResolveEncoding(utf8WithBom);
        File.WriteAllText(file, sb.ToString(), encoding);
        log?.Invoke($"{Path.GetFileName(file)}：替换 {replacements.Count} 处。");
        return replacements.Count;
    }

    // ---------------- 辅助 ----------------

    private readonly record struct Literal(int Start, int Length, string Text);

    /// <summary>枚举属于界面函数调用的字符串字面量（含外层引号）。</summary>
    private static IEnumerable<Literal> EnumerateUiLiterals(string text)
    {
        var seen = new HashSet<int>();
        foreach (Match call in CallStartRegex.Matches(text))
        {
            var end = FindClosingParen(text, call.Index);
            if (end < 0) continue;

            foreach (Match lit in LiteralRegex.Matches(text.Substring(call.Index, end - call.Index + 1)))
            {
                var start = call.Index + lit.Index;
                if (!seen.Add(start)) continue;
                yield return new Literal(start, lit.Length, lit.Groups[1].Value);
            }
        }
    }

    /// <summary>从 '(' 位置开始找到配对的 ')'，正确处理字符串与注释。</summary>
    private static int FindClosingParen(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped) { escaped = false; }
                else if (c == '\\') { escaped = true; }
                else if (c == '"') { inString = false; }
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0) return i;
                    break;
                case ';':
                    // 行注释
                    while (i < text.Length && text[i] != '\n') i++;
                    break;
            }
        }
        return -1;
    }

    /// <summary>把首尾的 \n / \t / \r 转义序列与正文分离，使词典键不含反斜杠。</summary>
    public static (string prefix, string core, string suffix) SplitDecorations(string raw)
    {
        var i = 0;
        while (i + 1 < raw.Length && IsEscape(raw, i)) i += 2;
        var prefix = raw[..i];

        var j = raw.Length;
        while (j - 2 >= i && IsEscape(raw, j - 2)) j -= 2;
        var suffix = raw[j..];

        return (prefix, raw[i..j], suffix);

        static bool IsEscape(string s, int index)
            => s[index] == '\\' && index + 1 < s.Length && s[index + 1] is 'n' or 't' or 'r';
    }

    private static List<TranslationEntry> Dedupe(List<TranslationEntry> raw)
    {
        var result = new List<TranslationEntry>();
        var seen = new Dictionary<string, TranslationEntry>(StringComparer.Ordinal);

        foreach (var e in raw)
        {
            var key = DictionaryService.Key(e.Kind, e.Source);
            if (seen.TryGetValue(key, out var existing))
            {
                if (!existing.OriginFile.Contains(e.OriginFile, StringComparison.Ordinal))
                {
                    existing.OriginFile += ", " + e.OriginFile;
                }
                continue;
            }
            seen[key] = e;
            result.Add(e);
        }
        return result;
    }
}
