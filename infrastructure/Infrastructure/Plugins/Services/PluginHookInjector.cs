namespace Core.Plugins;

public interface IPluginHookInjector
{
    /// <summary>注入 Hooks — 返回撤销函数(可逆效应)</summary>
    Task<Action> InjectHooksAsync(string pluginName, IReadOnlyList<PluginHookDefinition> hooks, CancellationToken ct = default);
    Task RemoveHooksAsync(string pluginName, CancellationToken ct = default);
    IEnumerable<PluginHookDefinition> GetInjectedHooks(string pluginName);
}

public sealed partial class PluginHookDefinition
{
    public required string HookName { get; init; }
    public required string TargetEvent { get; init; }
    public required string HookType { get; init; }
    public string? Command { get; init; }
    public string? Matcher { get; init; }
    public string? Condition { get; init; }
}

[Register(typeof(IPluginHookInjector), ServiceLifetime.Singleton)]
public sealed partial class PluginHookInjector : ServiceEntity, IPluginHookInjector
{
    private readonly IPluginManager _pluginManager;
    private readonly ILogger<PluginHookInjector>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly ConcurrentDictionary<string, List<PluginHookDefinition>> _injectedHooks;

    public PluginHookInjector(
        IPluginManager pluginManager,
        ILogger<PluginHookInjector>? logger = null,
        ITelemetryService? telemetryService = null)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _logger = logger;
        _telemetryService = telemetryService;
        _injectedHooks = new ConcurrentDictionary<string, List<PluginHookDefinition>>();
    }

    public async Task<Action> InjectHooksAsync(string pluginName, IReadOnlyList<PluginHookDefinition> hooks, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(hooks);

        if (!_pluginManager.IsPluginLoaded(pluginName))
        {
            throw new InvalidOperationException($"[INF029] 插件 '{pluginName}' 未加载，无法注入 Hook");
        }

        var hookList = new List<PluginHookDefinition>(hooks);

        _injectedHooks[pluginName] = hookList;

        RecordHookInjectorMetrics("inject", pluginName, hooks.Count, true);

        foreach (var hook in hooks)
        {
            _logger?.LogInformation(
                "[PluginHookInjector] 注入 Hook: {HookName} -> {TargetEvent} (插件: {Plugin})",
                hook.HookName, hook.TargetEvent, pluginName);
        }

        await Task.CompletedTask.ConfigureAwait(false);

        return () =>
        {
            if (_injectedHooks.TryRemove(pluginName, out var removed))
            {
                RecordHookInjectorMetrics("remove", pluginName, removed.Count, true);
            }
        };
    }

    public async Task RemoveHooksAsync(string pluginName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        if (_injectedHooks.TryRemove(pluginName, out var hooks))
        {
            RecordHookInjectorMetrics("remove", pluginName, hooks.Count, true);
            foreach (var hook in hooks)
            {
                _logger?.LogInformation(
                    "[PluginHookInjector] 移除 Hook: {HookName} (插件: {Plugin})",
                    hook.HookName, pluginName);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public IEnumerable<PluginHookDefinition> GetInjectedHooks(string pluginName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);

        return _injectedHooks.TryGetValue(pluginName, out var hooks)
            ? hooks
            : Array.Empty<PluginHookDefinition>();
    }

    private void RecordHookInjectorMetrics(string operation, string pluginName, int hookCount, bool isSuccess)
    {
        var tags = new Dictionary<string, string> { ["operation"] = operation, ["plugin"] = pluginName, ["success"] = isSuccess.ToString() };
        _telemetryService?.RecordCount("plugin.hook.count", tags, "count", "Plugin hook operation count");
        _telemetryService?.RecordHistogram("plugin.hook.hook_count", hookCount, new Dictionary<string, string> { ["operation"] = operation, ["plugin"] = pluginName }, "count", "Number of hooks in operation");
    }
}
