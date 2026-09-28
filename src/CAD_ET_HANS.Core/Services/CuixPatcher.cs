using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using CAD_ET_HANS.Core.Models;

namespace CAD_ET_HANS.Core.Services;

public sealed class PatchResult
{
    public int FilesChanged { get; set; }
    public int StringsReplaced { get; set; }
    public List<string> Errors { get; } = new();

    public void Merge(PatchResult other)
    {
        FilesChanged += other.FilesChanged;
        StringsReplaced += other.StringsReplaced;
        Errors.AddRange(other.Errors);
    }
}

/// <summary>
/// acetmain.cuix 的扫描与汉化。CUIX 为标准 ZIP/OPC 包，
/// 内含 MenuGroup.cui / PopMenuRoot.cui / RibbonRoot.cui / ToolbarRoot.cui 四个 XML。
/// 只替换显示文本，命令宏、图片资源名、UID 等一律原样保留。
/// </summary>
public sealed class CuixPatcher
{
    public static readonly string[] CuiFiles =
    {
        "MenuGroup.cui", "PopMenuRoot.cui", "RibbonRoot.cui", "ToolbarRoot.cui"
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>读取 CUIX，返回去重后的可汉化条目（不修改磁盘文件）。</summary>
    public List<TranslationEntry> Scan(string cuixPath)
    {
        var raw = new List<TranslationEntry>();
        using var zip = ZipFile.OpenRead(cuixPath);

        foreach (var name in CuiFiles)
        {
            var entry = zip.GetEntry(name);
            if (entry is null) continue;
            using var reader = new StreamReader(entry.Open(), Utf8NoBom);
            var doc = XDocument.Parse(reader.ReadToEnd(), LoadOptions.None);
            Collect(doc, name, raw);
        }

        return Dedupe(raw);
    }

    /// <summary>按条目改写 CUIX。forceLocalize 为 true 时把被改节点的 xlate 置为 false。</summary>
    public PatchResult Apply(
        string cuixPath,
        IEnumerable<TranslationEntry> entries,
        bool forceLocalize = false,
        Action<string>? log = null)
    {
        var map = entries
            .Where(e => e.Enabled && e.IsTranslated)
            .GroupBy(e => DictionaryService.Key(e.Kind, e.Source))
            .ToDictionary(g => g.Key, g => g.First().Target, StringComparer.Ordinal);

        var result = new PatchResult();
        if (map.Count == 0)
        {
            log?.Invoke("没有启用的译文，跳过 CUIX。");
            return result;
        }

        var temp = cuixPath + ".hans.tmp";

        try
        {
            using (var src = ZipFile.OpenRead(cuixPath))
            using (var dst = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                foreach (var name in CuiFiles)
                {
                    var entry = src.GetEntry(name);
                    if (entry is null) continue;

                    using var reader = new StreamReader(entry.Open(), Utf8NoBom);
                    var doc = XDocument.Parse(reader.ReadToEnd(), LoadOptions.None);
                    var changed = Transform(doc, name, map, forceLocalize);
                    result.StringsReplaced += changed;

                    var newEntry = dst.CreateEntry(name, CompressionLevel.Optimal);
                    using var writer = new StreamWriter(newEntry.Open(), Utf8NoBom);
                    doc.Save(writer, SaveOptions.None);

                    if (changed > 0)
                    {
                        result.FilesChanged++;
                        log?.Invoke($"CUIX/{name}：替换 {changed} 处。");
                    }
                }

                // 其余条目（图片、OPC 元数据等）原样复制，确保零丢失
                foreach (var entry in src.Entries)
                {
                    if (CuiFiles.Contains(entry.FullName)) continue;
                    var newEntry = dst.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    using var s = entry.Open();
                    using var d = newEntry.Open();
                    s.CopyTo(d);
                }
            }

            File.Copy(temp, cuixPath, true);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"CUIX 写入失败：{ex.Message}");
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* 忽略 */ }
            }
        }

        return result;
    }

    // ---------------- 扫描 ----------------

    private static void Collect(XDocument doc, string file, List<TranslationEntry> sink)
    {
        void AddText(XElement? el, EntryKind kind)
        {
            if (el is null) return;
            var value = el.Value;
            if (string.IsNullOrWhiteSpace(value)) return;
            sink.Add(new TranslationEntry(kind, value, file));
        }

        void AddAttr(XElement el, string attrName, EntryKind kind)
        {
            var attr = el.Attribute(attrName);
            if (attr is null) return;
            if (string.IsNullOrWhiteSpace(attr.Value)) return;
            sink.Add(new TranslationEntry(kind, attr.Value, file));
        }

        switch (file)
        {
            case "MenuGroup.cui":
                foreach (var g in doc.Descendants("MenuGroup"))
                {
                    AddAttr(g, "DisplayName", EntryKind.MenuItem);
                }
                foreach (var macro in doc.Descendants("Macro"))
                {
                    AddText(macro.Element("Name"), EntryKind.CommandName);
                    AddText(macro.Element("HelpString"), EntryKind.HelpString);
                }
                break;

            case "PopMenuRoot.cui":
                foreach (var menu in doc.Descendants("PopMenu"))
                {
                    AddText(menu.Element("Name"), EntryKind.MenuItem);
                }
                break;

            case "RibbonRoot.cui":
                foreach (var el in doc.Descendants("RibbonPanelSource"))
                {
                    AddText(el.Element("Name"), EntryKind.RibbonItem);
                    AddAttr(el, "Text", EntryKind.RibbonItem);
                }
                foreach (var el in doc.Descendants("RibbonTabSource"))
                {
                    AddText(el.Element("Name"), EntryKind.RibbonItem);
                    AddAttr(el, "Text", EntryKind.RibbonItem);
                }
                foreach (var el in doc.Descendants("RibbonCommandButton"))
                {
                    AddAttr(el, "Text", EntryKind.RibbonItem);
                    AddText(el.Element("TooltipTitle"), EntryKind.RibbonItem);
                }
                foreach (var el in doc.Descendants("RibbonSplitButton"))
                {
                    AddAttr(el, "Text", EntryKind.RibbonItem);
                }
                break;

            case "ToolbarRoot.cui":
                foreach (var el in doc.Descendants("Toolbar"))
                {
                    AddText(el.Element("Name"), EntryKind.ToolbarItem);
                }
                foreach (var el in doc.Descendants("ToolbarButton"))
                {
                    AddText(el.Element("Name"), EntryKind.ToolbarItem);
                }
                break;
        }
    }

    // ---------------- 改写 ----------------

    private static int Transform(
        XDocument doc, string file, Dictionary<string, string> map, bool forceLocalize)
    {
        var count = 0;

        bool TryReplace(XElement el, XName name, EntryKind kind)
        {
            var target = el.Element(name);
            if (target is null) return false;
            var value = target.Value;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (!map.TryGetValue(DictionaryService.Key(kind, value), out var zh)) return false;
            target.Value = zh;
            if (forceLocalize) target.SetAttributeValue("xlate", "false");
            return true;
        }

        bool TryReplaceAttr(XElement el, string attrName, EntryKind kind)
        {
            var attr = el.Attribute(attrName);
            if (attr is null) return false;
            if (string.IsNullOrWhiteSpace(attr.Value)) return false;
            if (!map.TryGetValue(DictionaryService.Key(kind, attr.Value), out var zh)) return false;
            attr.Value = zh;
            return true;
        }

        switch (file)
        {
            case "MenuGroup.cui":
                foreach (var g in doc.Descendants("MenuGroup"))
                {
                    if (TryReplaceAttr(g, "DisplayName", EntryKind.MenuItem)) count++;
                }
                foreach (var macro in doc.Descendants("Macro"))
                {
                    if (TryReplace(macro, "Name", EntryKind.CommandName)) count++;
                    if (TryReplace(macro, "HelpString", EntryKind.HelpString)) count++;
                }
                break;

            case "PopMenuRoot.cui":
                foreach (var menu in doc.Descendants("PopMenu"))
                {
                    if (TryReplace(menu, "Name", EntryKind.MenuItem)) count++;
                }
                break;

            case "RibbonRoot.cui":
                foreach (var el in doc.Descendants("RibbonPanelSource"))
                {
                    if (TryReplace(el, "Name", EntryKind.RibbonItem)) count++;
                    if (TryReplaceAttr(el, "Text", EntryKind.RibbonItem)) count++;
                }
                foreach (var el in doc.Descendants("RibbonTabSource"))
                {
                    if (TryReplace(el, "Name", EntryKind.RibbonItem)) count++;
                    if (TryReplaceAttr(el, "Text", EntryKind.RibbonItem)) count++;
                }
                foreach (var el in doc.Descendants("RibbonCommandButton"))
                {
                    if (TryReplaceAttr(el, "Text", EntryKind.RibbonItem)) count++;
                    if (TryReplace(el, "TooltipTitle", EntryKind.RibbonItem)) count++;
                }
                foreach (var el in doc.Descendants("RibbonSplitButton"))
                {
                    if (TryReplaceAttr(el, "Text", EntryKind.RibbonItem)) count++;
                }
                break;

            case "ToolbarRoot.cui":
                foreach (var el in doc.Descendants("Toolbar"))
                {
                    if (TryReplace(el, "Name", EntryKind.ToolbarItem)) count++;
                }
                foreach (var el in doc.Descendants("ToolbarButton"))
                {
                    if (TryReplace(el, "Name", EntryKind.ToolbarItem)) count++;
                }
                break;
        }

        return count;
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
