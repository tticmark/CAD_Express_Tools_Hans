using System.Collections.Generic;

namespace CAD_ET_HANS.Plugin.Abstractions;

/// <summary>插件宿主（主程序）向插件提供的共享能力。</summary>
public interface IPluginHost
{
    /// <summary>统一日志。</summary>
    ILogger Log { get; }

    /// <summary>检测本机已安装的 AutoCAD 环境。</summary>
    IReadOnlyList<AcadEnvironmentInfo> DetectEnvironments();
}
