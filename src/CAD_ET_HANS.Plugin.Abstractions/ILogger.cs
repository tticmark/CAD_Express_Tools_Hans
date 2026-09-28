namespace CAD_ET_HANS.Plugin.Abstractions;

/// <summary>插件统一的日志接口，由主程序宿主实现并注入。</summary>
public interface ILogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message);
}
