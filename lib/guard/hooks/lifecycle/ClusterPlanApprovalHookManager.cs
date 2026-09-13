namespace Core.Hooks.Lifecycle;


/// <summary>
/// 集群计划审批钩子管理器接口 — 在集群计划执行前触发审批钩子,根据钩子结果决定是否继续执行
/// </summary>
public interface IClusterPlanApprovalHookManager
{
    /// <summary>
    /// 异步触发集群计划审批钩子 — 根据上下文执行已注册的审批钩子并返回审批结果
    /// </summary>
    /// <param name="context">集群计划审批钩子上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>审批结果,指示是否应继续执行计划</returns>
    Task<ClusterPlanApprovalHookResult> OnClusterPlanApprovalAsync(ClusterPlanApprovalHookContext context, CancellationToken ct = default);
}

/// <summary>
/// 集群计划审批钩子上下文 — 携带会话标识、目标、计划与元数据
/// </summary>
public sealed partial class ClusterPlanApprovalHookContext
{
    /// <summary>会话标识</summary>
    public required string SessionId { get; init; }
    /// <summary>计划目标描述</summary>
    public required string Objective { get; init; }
    /// <summary>集群计划实例</summary>
    public required ClusterPlan Plan { get; init; }
    /// <summary>附加元数据,传递给审批钩子</summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

/// <summary>
/// 集群计划审批钩子结果 — 指示是否应继续执行计划,可携带消息与附加数据
/// </summary>
public sealed partial class ClusterPlanApprovalHookResult
{
    /// <summary>是否应继续执行计划,默认为 true</summary>
    public bool ShouldProceed { get; init; } = true;
    /// <summary>结果消息(可选)</summary>
    public string? Message { get; init; }
    /// <summary>附加数据,供下游消费</summary>
    public Dictionary<string, JsonElement> AdditionalData { get; init; } = new();

    /// <summary>
    /// 创建"继续执行"结果
    /// </summary>
    /// <param name="message">结果消息(可选)</param>
    /// <returns>指示继续执行的审批结果</returns>
    public static ClusterPlanApprovalHookResult Proceed(string? message = null) => new() { ShouldProceed = true, Message = message };

    /// <summary>
    /// 创建"阻止执行"结果
    /// </summary>
    /// <param name="message">结果消息(可选)</param>
    /// <returns>指示阻止执行的审批结果</returns>
    public static ClusterPlanApprovalHookResult Block(string? message = null) => new() { ShouldProceed = false, Message = message };
}

/// <summary>
/// 集群计划审批钩子管理器实现 — 触发审批钩子并按阻塞结果决定计划是否继续,超时自动放行,异常安全阻塞
/// </summary>
[Register(typeof(IClusterPlanApprovalHookManager), ServiceLifetime.Singleton)]
public sealed partial class ClusterPlanApprovalHookManager : ServiceEntity, IClusterPlanApprovalHookManager
{
    private readonly IHookOrchestrator _orchestrator;
    private readonly ILogger<ClusterPlanApprovalHookManager>? _logger;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 构造集群计划审批钩子管理器
    /// </summary>
    /// <param name="orchestrator">钩子编排器,用于执行匹配的审批钩子</param>
    /// <param name="logger">日志记录器(可选)</param>
    public ClusterPlanApprovalHookManager(IHookOrchestrator orchestrator, ILogger<ClusterPlanApprovalHookManager>? logger = null)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ClusterPlanApprovalHookResult> OnClusterPlanApprovalAsync(
        ClusterPlanApprovalHookContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var payload = new Dictionary<string, JsonElement>
        {
            ["session_id"] = JsonElementHelper.FromString(context.SessionId),
            ["objective"] = JsonElementHelper.FromString(context.Objective),
            ["is_decomposable"] = JsonElementHelper.FromBoolean(context.Plan.Decomposition.IsDecomposable),
            ["sub_task_count"] = JsonElementHelper.FromInt64(context.Plan.Decomposition.SubTasks.Count),
        };

        if (context.Plan.ValidationResult is not null)
        {
            payload["is_valid"] = JsonElementHelper.FromBoolean(context.Plan.ValidationResult.IsValid);
        }

        foreach (var (key, value) in context.Metadata)
        {
            payload[key] = value;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultTimeout);

        try
        {
            await foreach (var hookResult in _orchestrator.ExecuteHooksAsync(
                HookEvent.ClusterPlanApproval,
                payload,
                sessionId: context.SessionId,
                cancellationToken: cts.Token).ConfigureAwait(false))
            {
                if (hookResult.Outcome == HookOutcome.Blocking)
                {
                    _logger?.LogInformation("Cluster plan approval blocked by hook: {Message}", hookResult.Message);
                    return ClusterPlanApprovalHookResult.Block(hookResult.Message);
                }

                if (hookResult.PreventContinuation)
                {
                    return ClusterPlanApprovalHookResult.Block(hookResult.Message);
                }
            }

            return ClusterPlanApprovalHookResult.Proceed();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger?.LogWarning("Cluster plan approval hook timed out after {Timeout}s, auto-proceeding", DefaultTimeout.TotalSeconds);
            return ClusterPlanApprovalHookResult.Proceed($"审批超时 {DefaultTimeout.TotalSeconds}s，自动放行");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Cluster plan approval hook failed, blocking for safety");
            return ClusterPlanApprovalHookResult.Block($"审批异常，安全阻塞: {ex.Message}");
        }
    }
}
