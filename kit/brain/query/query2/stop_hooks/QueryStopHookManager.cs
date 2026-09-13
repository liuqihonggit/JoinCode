namespace Core.Query.StopHooks;

/// <summary>
/// 查询停止 Hook 管理器接口 — 扩展通用 Hook 管理器，负责停止 Hook 的注册与执行
/// </summary>
public interface IQueryStopHookManager : IHookManager
{
    /// <summary>
    /// 按优先级依次执行停止 Hook，首个要求停止的结果决定返回值
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="reason">停止原因</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>停止 Hook 执行结果</returns>
    Task<StopHookResult> ExecuteStopHooksAsync(string sessionId, string reason, CancellationToken ct = default);

    /// <summary>
    /// 注册停止 Hook
    /// </summary>
    /// <param name="hook">停止 Hook 实例</param>
    void RegisterStopHook(IQueryStopHook hook);

    /// <summary>
    /// 注销停止 Hook
    /// </summary>
    /// <param name="hookName">Hook 名称</param>
    void UnregisterStopHook(string hookName);
}

/// <summary>
/// 查询停止 Hook 接口 — 单个 Hook 的执行契约
/// </summary>
public interface IQueryStopHook
{
    /// <summary>
    /// Hook 名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 优先级（数值越小越先执行）
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 停止回调
    /// </summary>
    /// <param name="context">停止上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>停止 Hook 执行结果</returns>
    Task<StopHookResult> OnStopAsync(StopHookContext context, CancellationToken ct = default);
}

/// <summary>
/// 停止 Hook 上下文
/// </summary>
public sealed partial class StopHookContext
{
    /// <summary>
    /// 会话 ID
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 停止原因
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// 元数据
    /// </summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

/// <summary>
/// 停止 Hook 执行结果
/// </summary>
public sealed partial class StopHookResult
{
    /// <summary>
    /// 是否应停止
    /// </summary>
    public bool ShouldStop { get; init; } = true;

    /// <summary>
    /// 消息（可选）
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 附加数据
    /// </summary>
    public Dictionary<string, JsonElement> AdditionalData { get; init; } = new();

    /// <summary>
    /// 创建"继续"结果
    /// </summary>
    /// <param name="message">消息（可选）</param>
    /// <returns>ShouldStop=false 的结果</returns>
    public static StopHookResult Continue(string? message = null) => new() { ShouldStop = false, Message = message };

    /// <summary>
    /// 创建"停止"结果
    /// </summary>
    /// <param name="message">消息（可选）</param>
    /// <returns>ShouldStop=true 的结果</returns>
    public static StopHookResult Stop(string? message = null) => new() { ShouldStop = true, Message = message };
}

/// <summary>
/// 查询停止 Hook 管理器实现 — 按优先级排序执行，首个要求停止的 Hook 决定结果
/// </summary>
[Register(typeof(IQueryStopHookManager), ServiceLifetime.Singleton)]
public sealed partial class QueryStopHookManager : ServiceEntity, IQueryStopHookManager
{
    private readonly ConcurrentDictionary<string, IQueryStopHook> _hooks;
    private readonly ILogger<QueryStopHookManager>? _logger;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造函数 — 注入日志和遥测服务（均可选）
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="telemetryService">遥测服务</param>
    public QueryStopHookManager(ILogger<QueryStopHookManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _hooks = new ConcurrentDictionary<string, IQueryStopHook>(StringComparer.Ordinal);
        _logger = logger;
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// 按优先级依次执行停止 Hook，首个要求停止的结果决定返回值
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="reason">停止原因</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>停止 Hook 执行结果</returns>
    public async Task<StopHookResult> ExecuteStopHooksAsync(string sessionId, string reason, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(reason);

        var context = new StopHookContext
        {
            SessionId = sessionId,
            Reason = reason
        };

        var sortedHooks = _hooks.Values.OrderBy(h => h.Priority).ToList();

        foreach (var hook in sortedHooks)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await hook.OnStopAsync(context, ct).ConfigureAwait(false);
                if (result.ShouldStop)
                {
                    _logger?.LogInformation("[QueryStopHookManager] Hook '{HookName}' requested stop: {Message}", hook.Name, result.Message);
                    RecordStopHookMetrics(hook.Name, true);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[QueryStopHookManager] Hook '{HookName}' threw exception", hook.Name);
                RecordStopHookMetrics(hook.Name, false);
            }
        }

        return StopHookResult.Stop();
    }

    /// <summary>
    /// 注册停止 Hook — 同名 Hook 会被覆盖
    /// </summary>
    /// <param name="hook">停止 Hook 实例</param>
    public void RegisterStopHook(IQueryStopHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks[hook.Name] = hook;
        _logger?.LogDebug("[QueryStopHookManager] Registered stop hook: {HookName} (Priority: {Priority})", hook.Name, hook.Priority);
    }

    /// <summary>
    /// 注销停止 Hook
    /// </summary>
    /// <param name="hookName">Hook 名称</param>
    public void UnregisterStopHook(string hookName)
    {
        ArgumentNullException.ThrowIfNull(hookName);
        _hooks.TryRemove(hookName, out _);
        _logger?.LogDebug("[QueryStopHookManager] Unregistered stop hook: {HookName}", hookName);
    }

    private void RecordStopHookMetrics(string hookName, bool isSuccess)
        => _telemetryService?.RecordCount("query.stophook.count", new() { ["hook"] = hookName, ["success"] = isSuccess.ToString() }, "count", "Query stop hook execution count");
}
