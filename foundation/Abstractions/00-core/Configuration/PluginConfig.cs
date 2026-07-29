namespace JoinCode.Abstractions.Configuration;

/// <summary>
/// 插件配置
/// </summary>
public sealed class PluginConfig
{
    /// <summary>
    /// 外部插件目录路径（相对于应用程序根目录或绝对路径）
    /// </summary>
    public string ExternalPluginDirectory { get; set; } = "plugins";

    /// <summary>
    /// 启动时自动加载的外部插件名称列表
    /// </summary>
    public List<string> AutoLoadExternalPlugins { get; set; } = new();

    /// <summary>
    /// 禁用的插件名称列表
    /// </summary>
    public List<string> DisabledPlugins { get; set; } = new();
}
