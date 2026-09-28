namespace CAD_ET_HANS.Core.Models;

/// <summary>探测到的 AutoCAD 环境信息。</summary>
public sealed class AcadEnvironment
{
    /// <summary>版本号，如 2026。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>安装根目录，如 C:\Program Files\Autodesk\AutoCAD 2026。</summary>
    public string InstallPath { get; init; } = string.Empty;

    /// <summary>Express Tools 目录。</summary>
    public string ExpressPath { get; init; } = string.Empty;

    /// <summary>实际生效的用户配置 CUIX（优先，免管理员权限）。</summary>
    public string? UserCuixPath { get; init; }

    /// <summary>UserDataCache 中的源模板 CUIX（影响新建用户配置，需管理员权限）。</summary>
    public string? SourceCuixPath { get; init; }

    /// <summary>用户配置语言目录名，如 chs / enu。</summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>汉化时使用的 CUIX（用户配置优先）。</summary>
    public string? EffectiveCuixPath => UserCuixPath ?? SourceCuixPath;

    public string DisplayName => $"AutoCAD {Version}{(Language.Length > 0 ? $" ({Language})" : "")}";

    public bool IsValid =>
        !string.IsNullOrEmpty(InstallPath) &&
        Directory.Exists(ExpressPath) &&
        EffectiveCuixPath is not null;
}
