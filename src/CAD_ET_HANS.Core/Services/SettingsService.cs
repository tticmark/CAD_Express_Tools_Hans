using System.Text.Json;

namespace CAD_ET_HANS.Core.Services;

/// <summary>
/// 用户偏好设置。保存在 %LOCALAPPDATA%\CAD_ET_HANS\settings.json。
/// </summary>
public sealed class AppSettings
{
    /// <summary>启动后是否自动扫描（只读操作，不会写入任何文件）。</summary>
    public bool AutoScanOnStartup { get; set; }

    public bool LocalizeSourceTemplate { get; set; } = true;
    public bool LocalizeCuix { get; set; } = true;
    public bool LocalizeDcl { get; set; } = true;
    public bool LocalizeLsp { get; set; } = true;
    public bool Utf8WithBom { get; set; }
    public bool ForceLocalize { get; set; } = true;
}

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string Path { get; }

    public SettingsService(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CAD_ET_HANS", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions)
                       ?? new AppSettings();
            }
        }
        catch
        {
            // 设置文件损坏时退回默认值
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // 设置不可写时忽略
        }
    }
}
