namespace Core.Hooks.Execution;

/// <summary>
/// 工具修正 Hook 注册表 — 管理所有修正器，基于 ToolHealthMonitor 错误计数自动触发
/// <para>
/// 核心价值：
/// 1. 统一管理所有工具修正器
/// 2. 基于 ToolHealthMonitor.ConsecutiveFailures 自动触发
/// 3. 按优先级执行修正
/// 4. 记录修正日志
/// </para>
/// </summary>
[Register(typeof(ToolFixHookRegistry), ServiceLifetime.Singleton)]
public sealed partial class ToolFixHookRegistry : ServiceEntity
{
    private readonly List<IToolFixHook> _hooks = [];
    private readonly IToolHealthMonitor _healthMonitor;
    private readonly ILogger<ToolFixHookRegistry>? _logger;
    private readonly int _threshold;

    public ToolFixHookRegistry(
        IToolHealthMonitor healthMonitor,
        ILogger<ToolFixHookRegistry>? logger = null,
        int threshold = 3)
    {
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _logger = logger;
        _threshold = threshold;
        RegisterDefaultFixHooks();
    }

    /// <summary>
    /// 注册默认修正器 — GhPrBodyFixHook + JsonFixHook + GhTimeoutFixHook
    /// </summary>
    private void RegisterDefaultFixHooks()
    {
        Register(new FixHooks.GhPrBodyFixHook());
        Register(new FixHooks.JsonFixHook());
        Register(new FixHooks.GhTimeoutFixHook());
    }

    /// <summary>
    /// 注册修正器
    /// </summary>
    public void Register(IToolFixHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks.Add(hook);
        _logger?.LogDebug("注册工具修正器: {Name} (优先级: {Priority})", hook.Name, hook.Priority);
    }

    /// <summary>
    /// 尝试自动修正 — 当工具错误次数达到阈值时触发
    /// </summary>
    public async Task<ToolFixResult> TryFixAsync(
        string toolName,
        Exception error,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(error);

        // 检查是否应该自动修正
        var shouldFix = await _healthMonitor.ShouldAutoFixAsync(toolName, _threshold, ct).ConfigureAwait(false);
        if (!shouldFix)
        {
            return new ToolFixResult { Success = false, Description = "错误次数未达阈值" };
        }

        // 按优先级从高到低尝试修正
        foreach (var hook in _hooks.OrderByDescending(static h => h.Priority))
        {
            if (!hook.CanFix(toolName, error)) continue;

            try
            {
                var result = await hook.FixAsync(toolName, error, ct).ConfigureAwait(false);
                if (result.Success)
                {
                    _logger?.LogInformation(
                        "工具 {ToolName} 自动修正成功 (修正器: {Name}): {Description}",
                        toolName,
                        hook.Name,
                        result.Description);

                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "工具 {ToolName} 修正器 {Name} 执行失败", toolName, hook.Name);
            }
        }

        return new ToolFixResult { Success = false, Description = "无匹配的修正器" };
    }

    /// <summary>
    /// 获取所有已注册的修正器
    /// </summary>
    public IReadOnlyList<IToolFixHook> GetHooks() => _hooks;
}
