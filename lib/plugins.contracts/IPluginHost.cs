namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 插件宿主公共接口 — 三种 Host (Workflow/External/Native) 的公共契约
/// <para>用于 PluginManager 统一管理三种插件 host 字典,用 PluginKind 区分类型</para>
/// <para>统一字典前: _workflowPlugins + _externalPlugins + _nativePlugins 三个 ConcurrentDictionary</para>
/// <para>统一字典后: _plugins (ConcurrentDictionary&lt;string, IPluginHost&gt;),按 PluginType 分发</para>
/// </summary>
public interface IPluginHost : IDisposable
{
    /// <summary>插件名称 — 唯一标识,跨三种 Host 一致</summary>
    string PluginName { get; }

    /// <summary>插件类型 — 区分 Workflow/External/Native,用于统一字典中按类型分发</summary>
    PluginKind PluginType { get; }
}
