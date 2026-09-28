using CAD_ET_HANS.Core.Models;

namespace CAD_ET_HANS.Core.Services;

public sealed class LocalizationOptions
{
    public string CuixPath { get; init; } = string.Empty;

    /// <summary>
    /// UserDataCache 下的源模板 CUIX。AutoCAD 启动时会用它重新生成用户配置里的 CUIX，
    /// 只改用户那份会被覆盖，所以必须一并汉化（需要管理员权限）。
    /// </summary>
    public string SourceCuixPath { get; init; } = string.Empty;

    public bool LocalizeSourceTemplate { get; init; } = true;

    public string ExpressPath { get; init; } = string.Empty;
    public bool LocalizeCuix { get; init; } = true;
    public bool LocalizeDcl { get; init; } = true;
    public bool LocalizeLsp { get; init; } = true;
    public bool Utf8WithBom { get; init; }
    public bool ForceLocalize { get; init; }
}

/// <summary>
/// 扫描 → 查词典 → 打补丁 的编排。UI 与未来的命令行版本都复用它。
/// </summary>
public sealed class LocalizationEngine
{
    private readonly DictionaryService _dictionary;

    public LocalizationEngine(DictionaryService dictionary) => _dictionary = dictionary;

    /// <summary>扫描全部目标并填入内置/用户词典译文。</summary>
    public List<TranslationEntry> Scan(LocalizationOptions options, Action<string>? log = null)
    {
        var entries = new List<TranslationEntry>();

        if (options.LocalizeCuix && File.Exists(options.CuixPath))
        {
            var cuix = new CuixPatcher().Scan(options.CuixPath);
            entries.AddRange(cuix);
            log?.Invoke($"CUIX 扫描完成：{cuix.Count} 条。");
        }

        if (!Directory.Exists(options.ExpressPath))
        {
            log?.Invoke($"Express 目录不存在：{options.ExpressPath}");
        }
        else
        {
            if (options.LocalizeDcl)
            {
                var dcl = TextFilePatcher.ScanDcl(options.ExpressPath);
                entries.AddRange(dcl);
                log?.Invoke($"DCL 扫描完成：{dcl.Count} 条。");
            }
            if (options.LocalizeLsp)
            {
                var lsp = TextFilePatcher.ScanLsp(options.ExpressPath);
                entries.AddRange(lsp);
                log?.Invoke($"LSP 扫描完成：{lsp.Count} 条。");
            }
        }

        var hit = 0;
        var already = 0;
        foreach (var e in entries)
        {
            if (_dictionary.TryGet(e.Kind, e.Source, out var target))
            {
                e.Target = target;
                hit++;
            }
            else if (TextUtil.ContainsCjk(e.Source))
            {
                // 目标文件里已经是中文，说明此前汉化过，不需要再处理
                e.AlreadyLocalized = true;
                e.Target = e.Source;
                e.Enabled = false;
                already++;
            }
        }

        var missing = entries.Count - hit - already;
        log?.Invoke($"词典命中 {hit} 条；已是中文 {already} 条（此前已汉化）；仍为英文 {missing} 条（保留原文）。");
        return entries;
    }

    /// <summary>备份后写入。返回统计结果。</summary>
    public PatchResult Apply(
        LocalizationOptions options,
        IEnumerable<TranslationEntry> entries,
        BackupService backup,
        string acadVersion,
        Action<string>? log = null,
        IProgress<double>? progress = null)
    {
        var result = new PatchResult();
        var list = entries.ToList();
        var map = list
            .Where(e => e.Enabled && e.IsTranslated)
            .GroupBy(e => DictionaryService.Key(e.Kind, e.Source))
            .ToDictionary(g => g.Key, g => g.First().Target, StringComparer.Ordinal);

        log?.Invoke($"启用译文 {map.Count} 条。");
        if (map.Count == 0)
        {
            log?.Invoke("没有启用的译文，未做任何修改。");
            return result;
        }

        var targets = new List<string>();
        if (options.LocalizeCuix)
        {
            if (File.Exists(options.CuixPath)) targets.Add(options.CuixPath);
            if (options.LocalizeSourceTemplate && File.Exists(options.SourceCuixPath) &&
                !string.Equals(options.SourceCuixPath, options.CuixPath, StringComparison.OrdinalIgnoreCase))
            {
                targets.Add(options.SourceCuixPath);
            }
        }
        if (Directory.Exists(options.ExpressPath))
        {
            if (options.LocalizeDcl) targets.AddRange(Directory.EnumerateFiles(options.ExpressPath, "*.dcl"));
            if (options.LocalizeLsp) targets.AddRange(Directory.EnumerateFiles(options.ExpressPath, "*.lsp"));
        }

        var manifest = backup.Create($"AutoCAD{acadVersion}", targets, acadVersion, options.CuixPath);
        log?.Invoke($"已备份 {targets.Count} 个文件到 {Path.GetDirectoryName(manifest)}");

        var total = targets.Count + 1.0;
        var done = 0.0;

        if (options.LocalizeCuix && File.Exists(options.CuixPath))
        {
            var r = new CuixPatcher().Apply(options.CuixPath, list, options.ForceLocalize, log);
            result.Merge(r);
            progress?.Report(++done / total);
        }

        // 源模板：AutoCAD 会用它重建用户 CUIX，不改的话重启后菜单会变回英文
        if (options.LocalizeCuix && options.LocalizeSourceTemplate &&
            File.Exists(options.SourceCuixPath) &&
            !string.Equals(options.SourceCuixPath, options.CuixPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var r = new CuixPatcher().Apply(options.SourceCuixPath, list, options.ForceLocalize, log);
                result.Merge(r);
                log?.Invoke($"源模板已处理：{options.SourceCuixPath}");
            }
            catch (Exception ex)
            {
                var msg = $"源模板 {Path.GetFileName(options.SourceCuixPath)}：{ex.Message}";
                result.Errors.Add(msg);
                log?.Invoke("错误 - " + msg);
            }
        }

        if (Directory.Exists(options.ExpressPath))
        {
            if (options.LocalizeDcl)
            {
                foreach (var file in Directory.EnumerateFiles(options.ExpressPath, "*.dcl"))
                {
                    try
                    {
                        var n = TextFilePatcher.ApplyDclFile(file, map, options.Utf8WithBom, log);
                        if (n > 0)
                        {
                            result.StringsReplaced += n;
                            result.FilesChanged++;
                        }
                    }
                    catch (Exception ex)
                    {
                        var msg = $"{Path.GetFileName(file)}：{ex.Message}";
                        result.Errors.Add(msg);
                        log?.Invoke("错误 - " + msg);
                    }
                    progress?.Report(++done / total);
                }
            }

            if (options.LocalizeLsp)
            {
                foreach (var file in Directory.EnumerateFiles(options.ExpressPath, "*.lsp"))
                {
                    try
                    {
                        var n = TextFilePatcher.ApplyLspFile(file, map, options.Utf8WithBom, log);
                        if (n > 0)
                        {
                            result.StringsReplaced += n;
                            result.FilesChanged++;
                        }
                    }
                    catch (Exception ex)
                    {
                        var msg = $"{Path.GetFileName(file)}：{ex.Message}";
                        result.Errors.Add(msg);
                        log?.Invoke("错误 - " + msg);
                    }
                    progress?.Report(++done / total);
                }
            }
        }

        if (result.StringsReplaced == 0)
        {
            log?.Invoke("!! 替换数为 0，输出诊断信息：");
            log?.Invoke($"   CUIX 路径：{options.CuixPath}（存在={File.Exists(options.CuixPath)}，勾选={options.LocalizeCuix}）");
            log?.Invoke($"   Express 目录：{options.ExpressPath}（存在={Directory.Exists(options.ExpressPath)}）");
            if (Directory.Exists(options.ExpressPath))
            {
                log?.Invoke($"   .dcl 文件数={Directory.EnumerateFiles(options.ExpressPath, "*.dcl").Count()}，勾选={options.LocalizeDcl}");
                log?.Invoke($"   .lsp 文件数={Directory.EnumerateFiles(options.ExpressPath, "*.lsp").Count()}，勾选={options.LocalizeLsp}");
            }
            foreach (var k in map.Keys.Take(5))
            {
                log?.Invoke($"   词典键示例：{k}");
            }
        }

        log?.Invoke($"完成：修改 {result.FilesChanged} 个文件，替换 {result.StringsReplaced} 处字符串。");
        return result;
    }
}
