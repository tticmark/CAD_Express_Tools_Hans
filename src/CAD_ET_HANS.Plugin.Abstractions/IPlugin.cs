using System.Windows;

namespace CAD_ET_HANS.Plugin.Abstractions;

/// <summary>
/// 外挂插件契约。主程序通过反射发现并实现此接口的程序集，热加载到 plugins/ 目录。
/// </summary>
public interface IPlugin
{
    /// <summary>插件元数据（由 PluginMetadataAttribute 提供）。</summary>
    PluginMetadataAttribute Metadata { get; }

    /// <summary>
    /// 创建插件主界面。host 由主程序传入，提供日志与共享服务。
    /// 建议在此缓存并返回同一个实例，避免每次切换导航重建。
    /// </summary>
    FrameworkElement CreateView(IPluginHost host);
}
