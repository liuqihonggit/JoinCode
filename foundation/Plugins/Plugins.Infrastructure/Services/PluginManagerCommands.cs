namespace Core.Plugins;

/// <summary>
/// PluginManager Actor 命令基类(ADR 0098 维度9)
/// <para>所有命令通过 Channel mailbox 串行处理,消除 AsyncLock</para>
/// </summary>
public abstract record PluginManagerCommand;

/// <summary>
/// 加载工作流插件命令 — PluginFactory 创建插件实例(支持泛型方法)
/// </summary>
public sealed record LoadWorkflowCmd(
    Func<IWorkflowPlugin> PluginFactory,
    TaskCompletionSource<WorkflowPluginHost> Reply,
    CancellationToken CancellationToken) : PluginManagerCommand;

/// <summary>
/// 加载外部插件命令
/// </summary>
public sealed record LoadExternalCmd(
    string ExePath,
    string PluginName,
    TaskCompletionSource<ExternalPluginHost> Reply,
    CancellationToken CancellationToken) : PluginManagerCommand;

/// <summary>
/// 加载 native DLL 插件命令 (ADR 0099)
/// </summary>
public sealed record LoadNativeCmd(
    string DllPath,
    string PluginName,
    string? ConfigJson,
    TaskCompletionSource<NativePluginHost> Reply,
    CancellationToken CancellationToken) : PluginManagerCommand;

/// <summary>
/// 卸载插件命令
/// </summary>
public sealed record UnloadCmd(
    string PluginName,
    TaskCompletionSource<PluginUnloadResult> Reply,
    CancellationToken CancellationToken) : PluginManagerCommand;

/// <summary>
/// 卸载所有插件命令
/// </summary>
public sealed record UnloadAllCmd(
    TaskCompletionSource<IReadOnlyList<PluginUnloadResult>> Reply,
    CancellationToken CancellationToken) : PluginManagerCommand;

/// <summary>
/// PluginManager Actor 输出 — 不使用输出通道,所有回复通过 TaskCompletionSource
/// </summary>
public sealed record PluginManagerOutput;
