namespace Core.Plugins;

/// <summary>
/// 插件钩子注入器接口 — 管理插件向宿主事件注入的钩子，支持注入、移除和查询
/// </summary>
public interface IPluginHookInjector {
    /// <summary>注入 Hooks — 返回撤销函数(可逆效应)</summary>
    Task<Action> InjectHooksAsync(string pluginName, IReadOnlyList<PluginHookDefinition> hooks, CancellationToken ct = default);
    /// <summary>移除指定插件注入的全部钩子</summary>
    Task RemoveHooksAsync(string pluginName, CancellationToken ct = default);
    /// <summary>获取指定插件已注入的钩子列表</summary>
    IEnumerable<PluginHookDefinition> GetInjectedHooks(string pluginName);
}

/// <summary>
/// 插件钩子定义 — 描述钩子名称、目标事件、类型以及可选的命令、匹配器和条件
/// </summary>
public sealed partial class PluginHookDefinition {
    /// <summary>钩子名称</summary>
    public required string HookName { get; init; }
    /// <summary>目标事件名称</summary>
    public required string TargetEvent { get; init; }
    /// <summary>钩子类型</summary>
    public required string HookType { get; init; }
    /// <summary>钩子触发的命令（可选）</summary>
    public string? Command { get; init; }
    /// <summary>匹配器表达式（可选）</summary>
    public string? Matcher { get; init; }
    /// <summary>触发条件表达式（可选）</summary>
    public string? Condition { get; init; }
}

/// <summary>
/// 插件钩子注入器 — 管理插件钩子的注入、移除和查询，依赖 PluginManager 判断插件是否已加载
/// </summary>
[Register(typeof(IPluginHookInjector), ServiceLifetime.Singleton)]
public sealed partial class PluginHookInjector : ServiceEntity, IPluginHookInjector {
    private readonly IPluginManager _pluginManager;
    private readonly ILogger<PluginHookInjector>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly ConcurrentDictionary<string, List<PluginHookDefinition>> _injectedHooks;

    /// <summary>
    /// 构造插件钩子注入器
    /// </summary>
    /// <param name="pluginManager">插件管理器</param>
    /// <param name="logger">可选日志器</param>
    /// <param name="telemetryService">可选遥测服务</param>
    public PluginHookInjector(
        IPluginManager pluginManager,
        ILogger<PluginHookInjector>? logger = null,
        ITelemetryService? telemetryService = null) {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _logger = logger;
        _telemetryService = telemetryService;
        _injectedHooks = new ConcurrentDictionary<string, List<PluginHookDefinition>>();
    }

    /// <summary>
    /// 注入钩子 — 要求插件已加载，注入后返回撤销函数
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="hooks">钩子定义列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>撤销函数，调用后移除该插件注入的全部钩子</returns>
    public async Task<Action> InjectHooksAsync(string pluginName, IReadOnlyList<PluginHookDefinition> hooks, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(hooks);

        if (!_pluginManager.IsPluginLoaded(pluginName)) {
            throw new InvalidOperationException(PluginErrors.NotLoadedForHook(pluginName));
        }

        var hookList = new List<PluginHookDefinition>(hooks);

        _injectedHooks[pluginName] = hookList;

        RecordHookInjectorMetrics("inject", pluginName, hooks.Count, true);

        foreach (var hook in hooks) {
            _logger?.LogInformation(
                "[PluginHookInjector] 注入 Hook: {HookName} -> {TargetEvent} (插件: {Plugin})",
                hook.HookName, hook.TargetEvent, pluginName);
        }

        await Task.CompletedTask.ConfigureAwait(false);

        return () => {
            if (_injectedHooks.TryRemove(pluginName, out var removed)) {
                RecordHookInjectorMetrics("remove", pluginName, removed.Count, true);
            }
        };
    }

    /// <summary>
    /// 移除指定插件注入的全部钩子
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="ct">取消令牌</param>
    public async Task RemoveHooksAsync(string pluginName, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        if (_injectedHooks.TryRemove(pluginName, out var hooks)) {
            RecordHookInjectorMetrics("remove", pluginName, hooks.Count, true);
            foreach (var hook in hooks) {
                _logger?.LogInformation(
                    "[PluginHookInjector] 移除 Hook: {HookName} (插件: {Plugin})",
                    hook.HookName, pluginName);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 获取指定插件已注入的钩子列表
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>钩子定义列表，未注入则返回空集合</returns>
    public IEnumerable<PluginHookDefinition> GetInjectedHooks(string pluginName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        return _injectedHooks.TryGetValue(pluginName, out var hooks)
            ? hooks
            : Array.Empty<PluginHookDefinition>();
    }

    private void RecordHookInjectorMetrics(string operation, string pluginName, int hookCount, bool isSuccess) {
        var tags = new Dictionary<string, string> { ["operation"] = operation, ["plugin"] = pluginName, ["success"] = isSuccess.ToString() };
        _telemetryService?.RecordCount("plugin.hook.count", tags, "count", "Plugin hook operation count");
        _telemetryService?.RecordHistogram("plugin.hook.hook_count", hookCount, new Dictionary<string, string> { ["operation"] = operation, ["plugin"] = pluginName }, "count", "Number of hooks in operation");
    }
}