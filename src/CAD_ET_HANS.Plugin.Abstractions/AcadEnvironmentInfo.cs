namespace CAD_ET_HANS.Plugin.Abstractions;

/// <summary>已安装的 AutoCAD 环境摘要，供插件使用，避免插件直接依赖 Core。</summary>
public sealed record AcadEnvironmentInfo(
    string Version,
    string DisplayName,
    string InstallPath,
    bool IsValid);
