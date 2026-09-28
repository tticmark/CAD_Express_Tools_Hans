using CAD_ET_HANS.Core.Models;

namespace CAD_ET_HANS.Core.Services;

/// <summary>探测本机安装的 AutoCAD 及 Express Tools 相关路径。</summary>
public static class AcadLocator
{
    private const string CuixName = "acetmain.cuix";

    /// <summary>已安装版本的候选根目录（Autodesk 固定安装在此）。</summary>
    public static IEnumerable<string> InstallRoots()
    {
        var roots = new List<string>();
        var pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
        var autodesk = Path.Combine(pf, "Autodesk");
        if (Directory.Exists(autodesk))
        {
            foreach (var dir in Directory.EnumerateDirectories(autodesk, "AutoCAD *"))
            {
                roots.Add(dir);
            }
        }
        return roots;
    }

    public static List<AcadEnvironment> DetectAll()
    {
        var result = new List<AcadEnvironment>();
        foreach (var root in InstallRoots())
        {
            var env = FromInstallPath(root);
            if (env is not null)
            {
                result.Add(env);
            }
        }
        return result
            .OrderByDescending(e => TryParseVersion(e.Version))
            .ToList();
    }

    public static AcadEnvironment? Detect(string version)
    {
        var all = DetectAll();
        return all.FirstOrDefault(e => e.Version == version)
               ?? all.FirstOrDefault(e => e.InstallPath.Contains(version));
    }

    public static AcadEnvironment? FromInstallPath(string installPath)
    {
        if (!Directory.Exists(installPath)) return null;

        var name = new DirectoryInfo(installPath).Name;              // AutoCAD 2026
        var version = name.Replace("AutoCAD", "", StringComparison.OrdinalIgnoreCase).Trim();
        var express = Path.Combine(installPath, "Express");
        var userDataCache = Path.Combine(installPath, "UserDataCache", "Support", CuixName);

        if (!Directory.Exists(express)) return null;

        var userCuix = FindUserCuix(name);

        return new AcadEnvironment
        {
            Version = version,
            InstallPath = installPath,
            ExpressPath = express,
            UserCuixPath = userCuix.path,
            Language = userCuix.language,
            SourceCuixPath = File.Exists(userDataCache) ? userDataCache : null
        };
    }

    /// <summary>
    /// 在 %APPDATA%\Autodesk\&lt;产品&gt;\Rxx.x\&lt;语言&gt;\Support 下查找实际生效的 CUIX。
    /// </summary>
    public static (string? path, string language) FindUserCuix(string productFolder)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseDir = Path.Combine(appData, "Autodesk", productFolder);
        if (!Directory.Exists(baseDir)) return (null, string.Empty);

        foreach (var release in Directory.EnumerateDirectories(baseDir))
        {
            // 优先中文配置目录
            var candidates = new[] { "chs", "zh-CN", "zh-cn", "enu", "en-US" };
            foreach (var lang in candidates)
            {
                var p = Path.Combine(release, lang, "Support", CuixName);
                if (File.Exists(p)) return (p, lang);
            }

            // 兜底：任意语言目录
            foreach (var lang in Directory.EnumerateDirectories(release))
            {
                var p = Path.Combine(lang, "Support", CuixName);
                if (File.Exists(p)) return (p, new DirectoryInfo(lang).Name);
            }
        }
        return (null, string.Empty);
    }

    private static int TryParseVersion(string v)
        => int.TryParse(v, out var n) ? n : 0;
}
