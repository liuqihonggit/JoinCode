
namespace Core.Plugins;

public interface IPluginManager : IDisposable
{
    IReadOnlyCollection<string> LoadedPluginNames { get; }

    IReadOnlyCollection<string> LoadedWorkflowPluginNames { get; }

    IReadOnlyCollection<string> LoadedExternalPluginNames { get; }

    IReadOnlyCollection<string> LoadedNativePluginNames { get; }

    event EventHandler<string>? PluginLoaded;
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

    Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, PluginUnloadOptions? options = null);

    Task<PluginUnloadResult> UnloadPluginAsync(string pluginName, CancellationToken cancellationToken);

    Task<IReadOnlyList<PluginUnloadResult>> UnloadAllPluginsAsync(PluginUnloadOptions? options = null, CancellationToken cancellationToken = default);

    bool IsPluginLoaded(string pluginName);

    bool IsWorkflowPluginLoaded(string pluginName);

    bool IsExternalPluginLoaded(string pluginName);

    bool IsNativePluginLoaded(string pluginName);
}
