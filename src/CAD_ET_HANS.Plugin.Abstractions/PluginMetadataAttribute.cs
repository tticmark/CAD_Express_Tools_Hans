namespace CAD_ET_HANS.Plugin.Abstractions;

/// <summary>
/// 标记一个类为 CAD ET HANS 外挂插件，并提供其元数据。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PluginMetadataAttribute : Attribute
{
    public PluginMetadataAttribute(string id, string name, string? description = null, int order = 0)
    {
        Id = id;
        Name = name;
        Description = description;
        Order = order;
    }

    /// <summary>插件唯一标识（建议小写短横线，如 "localize"）。</summary>
    public string Id { get; }

    /// <summary>插件显示名称（用于导航）。</summary>
    public string Name { get; }

    /// <summary>插件描述。</summary>
    public string? Description { get; }

    /// <summary>排序，越小越靠前。</summary>
    public int Order { get; }
}
