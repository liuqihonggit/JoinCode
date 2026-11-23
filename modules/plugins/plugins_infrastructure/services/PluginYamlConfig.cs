namespace JoinCode.Plugins.Infrastructure.Services;

/// <summary>
/// 外部插件 YAML 配置 — 描述 ~/.jcc/plugins/ 约定文件夹中的 plugin.yaml 格式
/// <para>每个插件子目录包含一个 plugin.yaml + 一个 DLL 文件</para>
/// </summary>
public sealed class PluginYamlConfig {
    /// <summary>插件名（唯一键，与 DynamicPluginRegistry.DefinePlugin 的 name 参数对应）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>版本号</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>节点 ID — 与 ToolCategory.[EnumValue] 对齐（如 "shell"、"file"、"web"）</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>DLL 程序集路径（相对于 plugin.yaml 所在目录）</summary>
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>入口类型全名（如 "MyPlugin.Handlers.MyToolHandlers"）</summary>
    public string EntryType { get; set; } = string.Empty;

    /// <summary>capability 声明（该插件要求访问的服务名集合）</summary>
    public List<string> Capabilities { get; set; } = [];

    /// <summary>方法列表 — 每个方法对应一个 handler</summary>
    public List<PluginMethodConfig> Methods { get; set; } = [];
}

/// <summary>
/// 插件方法配置 — 描述 plugin.yaml 中 methods 列表的每一项
/// </summary>
public sealed class PluginMethodConfig {
    /// <summary>方法名（与 DynamicPluginRegistry.Invoke 的 method 参数对应）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>入口方法名（DLL 中的实际方法名，如 "ExecuteAsync"）</summary>
    public string EntryMethod { get; set; } = string.Empty;
}
