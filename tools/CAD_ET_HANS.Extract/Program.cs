using System.Text;
using CAD_ET_HANS.Core.Models;
using CAD_ET_HANS.Core.Services;

// 开发期辅助工具：扫描真实文件，导出全部英文原文，用于维护内置离线词典。
// 用法：CAD_ET_HANS.Extract <AutoCAD安装目录> [输出文件]

var installPath = args.Length > 0
    ? args[0]
    : @"C:\Program Files\Autodesk\AutoCAD 2026";
var output = args.Length > 1 ? args[1] : "extracted-strings.tsv";

var env = AcadLocator.FromInstallPath(installPath);
if (env is null)
{
    Console.Error.WriteLine($"未找到有效的 Express 目录：{installPath}");
    return 1;
}

Console.WriteLine($"AutoCAD {env.Version}　{env.InstallPath}");

var entries = new List<TranslationEntry>();

if (env.EffectiveCuixPath is not null && File.Exists(env.EffectiveCuixPath))
{
    entries.AddRange(new CuixPatcher().Scan(env.EffectiveCuixPath));
    Console.WriteLine($"CUIX 扫描完成：{env.EffectiveCuixPath}");
}

entries.AddRange(TextFilePatcher.ScanDcl(env.ExpressPath));
entries.AddRange(TextFilePatcher.ScanLsp(env.ExpressPath));

var sb = new StringBuilder();
sb.AppendLine("Kind\tSource\tOriginFile\tDictionaryHit");
var dictionary = new DictionaryService();
var hit = 0;

foreach (var e in entries.OrderBy(e => e.Kind).ThenBy(e => e.Source))
{
    var has = dictionary.TryGet(e.Kind, e.Source, out var target);
    if (has)
    {
        e.Target = target;
        hit++;
    }
    sb.AppendLine($"{e.Kind}\t{e.Source}\t{e.OriginFile}\t{(has ? "Y" : "N")}");
}

File.WriteAllText(output, sb.ToString(), new UTF8Encoding(false));
Console.WriteLine($"共 {entries.Count} 条，词典命中 {hit} 条，未命中 {entries.Count - hit} 条。");
Console.WriteLine($"已写入：{Path.GetFullPath(output)}");

// --apply：把当前词典结果写入目标（供开发期验证，默认不写）
if (!args.Contains("--apply")) return 0;

var backup = new BackupService(Path.Combine(Path.GetTempPath(), "CAD_ET_HANS-test-backups"));
var engine = new LocalizationEngine(dictionary);
var options = new LocalizationOptions
{
    CuixPath = env.EffectiveCuixPath ?? string.Empty,
    ExpressPath = env.ExpressPath,
    ForceLocalize = true          // 与界面默认一致
};

var result = engine.Apply(options, entries, backup, env.Version, Console.WriteLine);
Console.WriteLine($"写入完成：文件 {result.FilesChanged}，字符串 {result.StringsReplaced}，错误 {result.Errors.Count}");
foreach (var err in result.Errors) Console.WriteLine("  错误：" + err);
return 0;
