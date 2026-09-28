using System.Security.Cryptography;
using System.Text.Json;
using CAD_ET_HANS.Core.Models;

namespace CAD_ET_HANS.Core.Services;

public sealed record BackupInfo(string Directory, BackupManifest Manifest);

/// <summary>汉化前的快照备份与一键还原。</summary>
public sealed class BackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string Root { get; }

    public BackupService(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CAD_ET_HANS", "backups");
    }

    /// <summary>把给定文件原样快照到新的时间戳目录，返回 manifest 路径。</summary>
    public string Create(string title, IEnumerable<string> files, string acadVersion = "", string cuixPath = "")
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(Root, Sanitize(title) + "-" + stamp);
        Directory.CreateDirectory(dir);

        var manifest = new BackupManifest
        {
            AcadVersion = acadVersion,
            CuixPath = cuixPath
        };

        var index = 0;
        foreach (var file in files.Where(f => File.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            index++;
            var relative = index.ToString("D3") + "_" + Path.GetFileName(file);
            var dest = Path.Combine(dir, relative);
            File.Copy(file, dest, true);
            manifest.Files.Add(new BackupManifest.BackupFileItem
            {
                OriginalPath = file,
                RelativePath = relative,
                Sha256 = Sha256(file),
                Length = new FileInfo(file).Length
            });
        }

        var manifestPath = Path.Combine(dir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        return manifestPath;
    }

    public IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(Root)) return Array.Empty<BackupInfo>();

        var list = new List<BackupInfo>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            var mp = Path.Combine(dir, "manifest.json");
            if (!File.Exists(mp)) continue;
            try
            {
                var m = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(mp), JsonOptions);
                if (m is not null) list.Add(new BackupInfo(dir, m));
            }
            catch
            {
                // 忽略损坏的备份目录
            }
        }
        return list.OrderByDescending(b => b.Manifest.CreatedAt).ToList();
    }

    /// <summary>按 manifest 还原，返回成功还原的文件数。</summary>
    public int Restore(string manifestPath, Action<string>? log = null)
    {
        var dir = Path.GetDirectoryName(manifestPath);
        if (dir is null) throw new ArgumentException("manifest 路径无效", nameof(manifestPath));

        var m = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), JsonOptions);
        if (m is null) throw new InvalidDataException("无法解析 manifest.json");

        var count = 0;
        foreach (var item in m.Files)
        {
            var src = Path.Combine(dir, item.RelativePath);
            if (!File.Exists(src))
            {
                log?.Invoke($"备份文件缺失，已跳过：{item.RelativePath}");
                continue;
            }

            var targetDir = Path.GetDirectoryName(item.OriginalPath);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
            File.Copy(src, item.OriginalPath, true);
            count++;
            log?.Invoke($"已还原：{item.OriginalPath}");
        }
        return count;
    }

    public static string Sha256(string path)
    {
        using var fs = File.OpenRead(path);
        var hash = SHA256.HashData(fs);
        return Convert.ToHexString(hash);
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
