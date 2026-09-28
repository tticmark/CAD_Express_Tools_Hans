using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CAD_ET_HANS.Core.Models;

namespace CAD_ET_HANS.Core.Services;

public sealed class DictionaryRecord
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "CommandName";
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("target")] public string Target { get; set; } = string.Empty;

    public EntryKind KindValue => Enum.TryParse<EntryKind>(Kind, true, out var k) ? k : EntryKind.CommandName;
}

/// <summary>
/// 内置离线词典。以 (类别, 原文) 为键，完全离线工作；
/// 用户校正结果保存在 %LOCALAPPDATA%\CAD_ET_HANS\user-dictionary.json，优先级高于内置词典。
/// </summary>
public sealed class DictionaryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, string> _builtIn = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _user = new(StringComparer.Ordinal);

    public string UserDictionaryPath { get; }

    public int BuiltInCount => _builtIn.Count;

    public DictionaryService(string? userDictionaryPath = null)
    {
        UserDictionaryPath = userDictionaryPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CAD_ET_HANS", "user-dictionary.json");

        LoadBuiltIn();
        LoadUser();
    }

    private static readonly string[] BuiltInResources =
    {
        "CAD_ET_HANS.Core.Resources.dictionary.cuix.json",
        "CAD_ET_HANS.Core.Resources.dictionary.dcl.json",
        "CAD_ET_HANS.Core.Resources.dictionary.lsp.json"
    };

    private void LoadBuiltIn()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var resourceName in BuiltInResources)
        {
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null) continue;

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var list = JsonSerializer.Deserialize<List<DictionaryRecord>>(reader.ReadToEnd(), JsonOptions);
            if (list is null) continue;

            foreach (var r in list)
            {
                if (string.IsNullOrEmpty(r.Source) || string.IsNullOrEmpty(r.Target)) continue;
                _builtIn[Key(r.KindValue, r.Source)] = r.Target;
            }
        }
    }

    private void LoadUser()
    {
        if (!File.Exists(UserDictionaryPath)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<DictionaryRecord>>(
                File.ReadAllText(UserDictionaryPath, Encoding.UTF8), JsonOptions);
            if (list is null) return;
            foreach (var r in list)
            {
                if (string.IsNullOrEmpty(r.Source) || string.IsNullOrEmpty(r.Target)) continue;
                _user[Key(r.KindValue, r.Source)] = r.Target;
            }
        }
        catch
        {
            // 用户词典损坏时忽略，不影响内置词典
        }
    }

    public static string Key(EntryKind kind, string source) => $"{(int)kind}|{source}";

    public bool TryGet(EntryKind kind, string source, out string target)
    {
        var key = Key(kind, source);
        return _user.TryGetValue(key, out target!) || _builtIn.TryGetValue(key, out target!);
    }

    /// <summary>把当前条目与词典的差异保存为用户覆盖词典。</summary>
    public int SaveOverrides(IEnumerable<TranslationEntry> entries)
    {
        var records = new List<DictionaryRecord>();
        foreach (var e in entries)
        {
            if (!e.IsTranslated) continue;
            var key = Key(e.Kind, e.Source);
            if (_builtIn.TryGetValue(key, out var builtIn) && builtIn == e.Target) continue;
            records.Add(new DictionaryRecord
            {
                Kind = e.Kind.ToString(),
                Source = e.Source,
                Target = e.Target
            });
        }

        var dir = Path.GetDirectoryName(UserDictionaryPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(UserDictionaryPath,
            JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
        return records.Count;
    }

    public void Merge(IEnumerable<TranslationEntry> entries)
    {
        foreach (var e in entries)
        {
            if (e.IsTranslated) _user[Key(e.Kind, e.Source)] = e.Target;
        }
    }

    // ---------- 导入导出 ----------

    public static void ExportJson(string path, IEnumerable<TranslationEntry> entries)
    {
        var records = entries.Select(e => new DictionaryRecord
        {
            Kind = e.Kind.ToString(),
            Source = e.Source,
            Target = e.Target
        }).ToList();

        File.WriteAllText(path, JsonSerializer.Serialize(records, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }), new UTF8Encoding(false));
    }

    public static void ExportCsv(string path, IEnumerable<TranslationEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Kind,Source,Target,OriginFile");
        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(',',
                Quote(e.Kind.ToString()), Quote(e.Source), Quote(e.Target), Quote(e.OriginFile)));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));

        static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>导入 CSV/JSON，返回 (更新条数, 新增条数)。</summary>
    public static (int updated, int added) ImportInto(
        string path,
        ICollection<TranslationEntry> entries,
        Action<string>? log = null)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var records = ext switch
        {
            ".json" => JsonSerializer.Deserialize<List<DictionaryRecord>>(
                File.ReadAllText(path, Encoding.UTF8), JsonOptions) ?? new(),
            ".csv" => ParseCsv(File.ReadAllText(path, Encoding.UTF8)),
            _ => throw new NotSupportedException($"不支持的文件格式：{ext}")
        };

        var map = entries.ToDictionary(e => Key(e.Kind, e.Source), e => e, StringComparer.Ordinal);
        var updated = 0;
        var added = 0;

        foreach (var r in records)
        {
            if (string.IsNullOrEmpty(r.Source) || string.IsNullOrEmpty(r.Target)) continue;
            var key = Key(r.KindValue, r.Source);
            if (map.TryGetValue(key, out var existing))
            {
                existing.Target = r.Target;
                updated++;
            }
            else
            {
                var e = new TranslationEntry(r.KindValue, r.Source, "导入")
                {
                    Target = r.Target
                };
                entries.Add(e);
                map[key] = e;
                added++;
            }
        }

        log?.Invoke($"导入完成：更新 {updated} 条，新增 {added} 条。");
        return (updated, added);
    }

    private static List<DictionaryRecord> ParseCsv(string text)
    {
        var list = new List<DictionaryRecord>();
        var lines = SplitCsvLines(text);
        foreach (var line in lines.Skip(1))
        {
            var cols = SplitCsvLine(line);
            if (cols.Count < 3) continue;
            list.Add(new DictionaryRecord
            {
                Kind = cols[0],
                Source = cols[1],
                Target = cols[2]
            });
        }
        return list;
    }

    private static List<string> SplitCsvLines(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
               .Where(l => l.Length > 0).ToList();

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
