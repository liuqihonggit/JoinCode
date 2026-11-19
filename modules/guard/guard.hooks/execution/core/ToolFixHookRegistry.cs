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
public sealed partial class ToolFixHookRegistry : ServiceEntity {
    /// <summary>修正器链在 profile 中的消费点 ID（contract: guard）。</summary>
    public const string ConsumerId = "tool-fix-chain";

    private readonly List<IToolFixHook> _hooks;
    private readonly IToolHealthMonitor _healthMonitor;
    private readonly ILogger<ToolFixHookRegistry>? _logger;
    private readonly int _threshold;
    private readonly int _declaredCount;

    /// <summary>
    /// 构造工具修正 Hook 注册表
    /// <para>TASK100：修正器由 DI 收集（各实现标 <c>[Register(typeof(IToolFixHook))]</c>），
    /// 执行顺序由 profile 消费点 <c>tool-fix-chain</c> 决定；不再硬编码 <c>new</c> 三个。</para>
    /// </summary>
    /// <param name="healthMonitor">工具健康监控器,提供错误计数与自动修正判定</param>
    /// <param name="hooks">DI 收集的修正器；为 null 时为空链（单测/无宿主场景）</param>
    /// <param name="profiles">Profile 管理器(可选) — 提供修正器链顺序声明</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="threshold">触发自动修正的连续失败次数阈值,默认 3 次</param>
    public ToolFixHookRegistry(
        IToolHealthMonitor healthMonitor,
        IEnumerable<IToolFixHook>? hooks = null,
        JoinCode.Abstractions.Composition.IProfileManager? profiles = null,
        ILogger<ToolFixHookRegistry>? logger = null,
        int threshold = 3) {
        _healthMonitor = healthMonitor ?? throw new ArgumentNullException(nameof(healthMonitor));
        _hooks = NodeChainOrder.Order(
            hooks ?? [], profiles, ConsumerId, static h => h.Name, static h => h.Priority, "工具修正器").ToList();
        _declaredCount = _hooks.Count;
        _logger = logger;
        _threshold = threshold;
        Diag.WriteLine($"[ORDER] tool_fix_hooks: {string.Join(" > ", _hooks.Select(static h => h.Name))}");
    }

    /// <summary>
    /// 撤销注册指定名称的修正器 — 插件卸载时调用
    /// </summary>
    public bool Unregister(string hookName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(hookName);
        var removed = _hooks.RemoveAll(h => h.Name == hookName);
        if (removed > 0) {
            _logger?.LogDebug("撤销工具修正器: {Name} (移除 {Count} 个)", hookName, removed);
        }
        return removed > 0;
    }

    /// <summary>
    /// 注册修正器 — 运行期动态追加（插件/测试）。
    /// <para>profile 声明的保留在前（顺序由 tool-fix-chain 决定），动态追加的按 Priority 降序插在其后，
    /// 保证「先声明者先尝试」与「同段内高位次优先」两条语义同时成立。</para>
    /// </summary>
    public void Register(IToolFixHook hook) {
        ArgumentNullException.ThrowIfNull(hook);
        var index = _hooks.Count;
        while (index > _declaredCount && _hooks[index - 1].Priority < hook.Priority) index--;
        _hooks.Insert(index, hook);
        _logger?.LogDebug("注册工具修正器: {Name} (位次: {Priority})", hook.Name, hook.Priority);
    }

    /// <summary>
    /// 尝试自动修正 — 当工具错误次数达到阈值时触发
    /// </summary>
    public async Task<ToolFixResult> TryFixAsync(
        string toolName,
        Exception error,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(error);

        // 检查是否应该自动修正
        var shouldFix = await _healthMonitor.ShouldAutoFixAsync(toolName, _threshold, ct).ConfigureAwait(false);
        if (!shouldFix) {
            return new ToolFixResult { Success = false, Description = "错误次数未达阈值" };
        }

        // 顺序在构造期由 profile 决定（tool-fix-chain），Register 追加的排在其后。
        foreach (var hook in _hooks) {
            if (!hook.CanFix(toolName, error)) continue;

            try {
                var result = await hook.FixAsync(toolName, error, ct).ConfigureAwait(false);
                if (result.Success) {
                    _logger?.LogInformation(
                        "工具 {ToolName} 自动修正成功 (修正器: {Name}): {Description}",
                        toolName,
                        hook.Name,
                        result.Description);

                    return result;
                }
            } catch (Exception ex) {
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