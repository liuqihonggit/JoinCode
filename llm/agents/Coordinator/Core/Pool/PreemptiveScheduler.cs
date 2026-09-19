namespace Core.Agents.Coordinator.Pool;

/// <summary>
/// 抢塞结果
/// </summary>
public sealed record PreemptResult {
    /// <summary>是否成功抢塞</summary>
    public required bool Success { get; init; }
    /// <summary>被抢塞的子代理</summary>
    public AgentBase? Agent { get; init; }
    /// <summary>原因描述</summary>
    public string? Reason { get; init; }
    /// <summary>是否执行了上下文压缩</summary>
    public bool ContextCompressed { get; init; }

    /// <summary>抢塞成功</summary>
    public static PreemptResult Succeeded(AgentBase agent, bool compressed = false) => new() {
        Success = true,
        Agent = agent,
        ContextCompressed = compressed,
    };

    /// <summary>无可用子代理</summary>
    public static PreemptResult NoAvailableAgent() => new() {
        Success = false,
        Reason = "代理池无可用子代理",
    };

    /// <summary>窗口不足且压缩失败</summary>
    public static PreemptResult WindowInsufficient() => new() {
        Success = false,
        Reason = "上下文窗口不足且压缩失败",
    };
}

/// <summary>
/// 抢占式调度器 — L3 干预层（ADR 0106）
/// <para>
/// 子代理完成后回池，新任务到来时抢塞复用上下文：
/// 1. 从代理池获取上下文最匹配的已完成子代理
/// 2. 检查上下文窗口剩余空间
/// 3. 窗口充足 → 直接注入新任务 prompt（前缀不变，KV cache 命中）
/// 4. 窗口不足 → 先压缩再注入
/// </para>
/// </summary>
public sealed class PreemptiveScheduler {
    private readonly SubAgentPool _pool;
    private readonly IChatContextManager _contextManager;
    private readonly SubAgentLivenessOptions _options;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造抢占式调度器
    /// </summary>
    public PreemptiveScheduler(
        SubAgentPool pool,
        IChatContextManager contextManager,
        SubAgentLivenessOptions options,
        ILogger? logger = null) {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>
    /// 尝试抢塞新任务到已完成的子代理
    /// </summary>
    /// <param name="taskDescription">新任务描述</param>
    /// <param name="ct">取消令牌</param>
    public async Task<PreemptResult> TryPreemptAsync(string taskDescription, CancellationToken ct = default) {
        _logger?.LogDebug("[PreemptiveScheduler] 尝试抢塞任务: {Task}", taskDescription);

        var agent = _pool.TryAcquire(taskDescription);
        if (agent is null) {
            _logger?.LogDebug("[PreemptiveScheduler] 无可用子代理抢塞任务: {Task}", taskDescription);
            return PreemptResult.NoAvailableAgent();
        }

        var agentId = agent.ObjectId.UniqueId;

        // 检查上下文窗口剩余空间
        var (usedTokens, maxTokens) = EstimateTokenUsage(agent);
        var remainingRatio = maxTokens > 0 ? 1.0 - (double)usedTokens / maxTokens : 1.0;

        _logger?.LogDebug("[PreemptiveScheduler] Agent {AgentId} 窗口使用: {Used}/{Max}，剩余 {Ratio:P0}",
            agentId, usedTokens, maxTokens, remainingRatio);

        var compressed = false;
        if (remainingRatio < _options.PreemptMinWindowRatio) {
            _logger?.LogInformation("[PreemptiveScheduler] Agent {AgentId} 窗口剩余 {Ratio:P0} < {Threshold:P0}，先压缩",
                agentId, remainingRatio, _options.PreemptMinWindowRatio);

            var foldResult = await _contextManager.FoldIfNeededAsync(
                ContextFoldDecision.FoldNormal, agentId, ct).ConfigureAwait(false);

            compressed = foldResult.Folded;
            if (!compressed) {
                _logger?.LogWarning("[PreemptiveScheduler] Agent {AgentId} 压缩未执行（可能无需折叠），继续抢塞", agentId);
            }
        }

        // 注入新任务 prompt（接在旧上下文之后，前缀不变 → KV cache 命中）
        agent.ChatHistory.AddUserMessage($"[新任务] {taskDescription}");
        agent.Status = TaskExecutionStatus.Running;

        // 刷新活动时间
        if (agent is Entity entity)
            entity.Touch();

        _logger?.LogInformation("[PreemptiveScheduler] Agent {AgentId} 抢塞新任务: {Task}（压缩: {Compressed}）",
            agentId, taskDescription, compressed);

        return PreemptResult.Succeeded(agent, compressed);
    }

    /// <summary>
    /// 估算子代理的 token 用量 — 基于 AgentBase.Output.TokensUsed 和 Budget.TokenBudget
    /// </summary>
    private static (int used, int max) EstimateTokenUsage(AgentBase agent) {
        var used = agent.Output.TokensUsed;
        var max = agent.Budget.TokenBudget ?? 128000;
        return (used, max);
    }
}