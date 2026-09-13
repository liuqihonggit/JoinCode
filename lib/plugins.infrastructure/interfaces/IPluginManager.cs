
namespace Core.Plugins;

/// <summary>
/// 插件管理器接口 — 统一管理工作流插件、外部进程插件、native DLL 插件的加载、卸载和查询
/// </summary>
public interface IPluginManager : IDisposable
{
    /// <summary>已加载的全部插件名称（工作流 + 外部 + native）</summary>
    IReadOnlyCollection<string> LoadedPluginNames { get; }

    /// <summary>已加载的工作流插件名称</summary>
    IReadOnlyCollection<string> LoadedWorkflowPluginNames { get; }

    /// <summary>已加载的外部进程插件名称</summary>
    IReadOnlyCollection<string> LoadedExternalPluginNames { get; }

    /// <summary>已加载的 native DLL 插件名称</summary>
    IReadOnlyCollection<string> LoadedNativePluginNames { get; }

    /// <summary>插件加载完成事件 — 参数为插件名称</summary>
    event EventHandler<string>? PluginLoaded;
    /// <summary>插件卸载开始事件 — 参数为插件名称</summary>
    event EventHandler<string>? PluginUnloading;

    /// <summary>
    /// 加载编译时已知的内部工作流插件（AOT兼容）
    /// </summary>
    Task<WorkflowPluginHost> LoadWorkflowPluginAsync<TPlugin>(CancellationToken cancellationToken = default) where TPlugin : class, IWorkflowPlugin, new();

    /// <summary>
    /// 加载外部exe进程插件（AOT兼容，通过stdio通信）
    /// </summary>
    Task<ExternalPluginHost> LoadExternalPluginAsync(string exePath, string pluginName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载 native DLL 插件（AOT兼容，通过 NativeLibrary.Load + JSON IPC）(ADR 0099)
    /// </summary>
    Task<NativePluginHost> LoadNativePluginAsync(string dllPath, string pluginName, string? configJson = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取工作流插件宿主
    /// </summary>
    WorkflowPluginHost? GetWorkflowPlugin(string pluginName);

    /// <summary>
    /// 获取外部插件宿主
    /// </summary>
    ExternalPluginHost? GetExternalPlugin(string pluginName);

    /// <summary>
    /// 获取 native DLL 插件宿主
    /// </summary>
    NativePluginHost? GetNativePlugin(string pluginName);

    /// <summary>
    /// 获取工作流插件实例
    /// </summary>
    T? GetWorkflowPlugin<T>(string pluginName) where T : class, IWorkflowPlugin;

    /// <summary>
    /// 卸载指定插件 — 使用给定的卸载选项（超时、是否强制卸载 ALC）
    /// </summary>
    Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, PluginUnloadOptions? options = null);

    /// <summary>
    /// 卸载指定插件 — 使用给定的取消令牌控制协作卸载
    /// </summary>
    Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, CancellationToken cancellationToken);

    /// <summary>
    /// 卸载全部已加载插件 — 返回每个插件的卸载结果
    /// </summary>
    Task<IReadOnlyList<PluginUnloadResult>> UnloadAllPluginsAsync(PluginUnloadOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>查询指定名称的插件是否已加载（任意类型）</summary>
    bool IsPluginLoaded(string pluginName);

    /// <summary>查询指定名称的工作流插件是否已加载</summary>
    bool IsWorkflowPluginLoaded(string pluginName);

    /// <summary>查询指定名称的外部进程插件是否已加载</summary>
    bool IsExternalPluginLoaded(string pluginName);

    /// <summary>查询指定名称的 native DLL 插件是否已加载</summary>
    bool IsNativePluginLoaded(string pluginName);
}
