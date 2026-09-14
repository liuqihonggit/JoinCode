namespace Core.Agents.Coordinator.Liveness;

/// <summary>
/// 压缩级别 — 渐进式 escalation 三级
/// </summary>
public enum CompactionLevel
{
    /// <summary>未压缩</summary>
    None,

    /// <summary>轻度压缩 — 仅剪裁过期大工具结果（FoldNormal）</summary>
    Light,

    /// <summary>激进压缩 — 头部消息摘要化，保留最近 N 轮 + 关键决策点（FoldAggressive）</summary>
    Aggressive,

    /// <summary>退出并输出摘要 — 最后手段（ExitWithSummary）</summary>
    ExitWithSummary,
}

/// <summary>
/// 渐进式压缩结果
/// </summary>
public sealed record CompactionResult
{
    /// <summary>子代理 ID</summary>
    public required string AgentId { get; init; }

    /// <summary>最终达到的压缩级别</summary>
    public required CompactionLevel Level { get; init; }

    /// <summary>是否成功压缩</summary>
    public required bool Success { get; init; }

    /// <summary>压缩后的摘要（仅 ExitWithSummary 级别有值）</summary>
    public string? Summary { get; init; }

    /// <summary>折叠结果（Light/Aggressive 级别有值）</summary>
    public ContextFoldResult? FoldResult { get; init; }

    /// <summary>压缩成功（Light 级别）</summary>
    public static CompactionResult LightSucceeded(string agentId, ContextFoldResult result) => new()
    {
        AgentId = agentId,
        Level = CompactionLevel.Light,
        Success = true,
        FoldResult = result,
    };

    /// <summary>压缩成功（Aggressive 级别）</summary>
    public static CompactionResult AggressiveSucceeded(string agentId, ContextFoldResult result) => new()
    {
        AgentId = agentId,
        Level = CompactionLevel.Aggressive,
        Success = true,
        FoldResult = result,
    };

    /// <summary>退出并输出摘要</summary>
    public static CompactionResult ExitedWithSummary(string agentId, string summary) => new()
    {
        AgentId = agentId,
        Level = CompactionLevel.ExitWithSummary,
        Success = true,
        Summary = summary,
    };
}

/// <summary>
/// 渐进式压缩编排器 — L4 恢复层（ADR 0106）
/// <para>
/// 三级 escalation：
/// 1. Light（FoldNormal）— 仅剪裁过期大工具结果，信息损失小
/// 2. Aggressive（FoldAggressive）— 头部消息摘要化，保留最近 N 轮 + 关键决策点
/// 3. ExitWithSummary — 退出并输出任务摘要
/// 复用 IChatContextManager.FoldIfNeededAsync(decision, agentId) — 已支持 agentId 参数
/// </para>
/// </summary>
public sealed class ProgressiveCompactor
{
    private readonly IChatContextManager _contextManager;
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造渐进式压缩编排器
    /// </summary>
    public ProgressiveCompactor(IChatContextManager contextManager, ILogger? logger = null)
    {
        _contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
        _logger = logger;
    }

    /// <summary>
    /// 渐进式压缩：Light → Aggressive → ExitWithSummary
    /// </summary>
    /// <param name="agentId">卡死的子代理 ID</param>
    /// <param name="ct">取消令牌</param>
    public async Task<CompactionResult> CompactProgressiveAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        _logger?.LogDebug("[ProgressiveCompactor] 开始渐进式压缩 Agent {AgentId}", agentId);

        // Level 1: Light 压缩
        var lightResult = await TryCompactAsync(agentId, ContextFoldDecision.FoldNormal, ct).ConfigureAwait(false);
        if (lightResult.Folded)
        {
            _logger?.LogInformation("[ProgressiveCompactor] Agent {AgentId} Light 压缩成功", agentId);
            return CompactionResult.LightSucceeded(agentId, lightResult);
        }

        _logger?.LogWarning("[ProgressiveCompactor] Agent {AgentId} Light 压缩未执行，升级到 Aggressive", agentId);

        // Level 2: Aggressive 压缩
        var aggressiveResult = await TryCompactAsync(agentId, ContextFoldDecision.FoldAggressive, ct).ConfigureAwait(false);
        if (aggressiveResult.Folded)
        {
            _logger?.LogInformation("[ProgressiveCompactor] Agent {AgentId} Aggressive 压缩成功", agentId);
            return CompactionResult.AggressiveSucceeded(agentId, aggressiveResult);
        }

        _logger?.LogWarning("[ProgressiveCompactor] Agent {AgentId} Aggressive 压缩未执行，升级到 ExitWithSummary", agentId);

        // Level 3: ExitWithSummary — 生成摘要并退出
        var summary = await GenerateSummaryAsync(agentId, ct).ConfigureAwait(false);
        _logger?.LogWarning("[ProgressiveCompactor] Agent {AgentId} ExitWithSummary，摘要长度: {Length}", agentId, summary.Length);
        return CompactionResult.ExitedWithSummary(agentId, summary);
    }

    /// <summary>
    /// 尝试指定级别的压缩
    /// </summary>
    private async Task<ContextFoldResult> TryCompactAsync(string agentId, ContextFoldDecision decision, CancellationToken ct)
    {
        return await _contextManager.FoldIfNeededAsync(decision, agentId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 生成任务摘要 — ExitWithSummary 级别使用
    /// </summary>
    private async Task<string> GenerateSummaryAsync(string agentId, CancellationToken ct)
    {
        var messages = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
        var recentMessages = messages.TakeLast(10).ToList();
        var summary = string.Join("\n", recentMessages.Select(m => $"[{m.Role}] {m.Content}"));
        _logger?.LogDebug("[ProgressiveCompactor] Agent {AgentId} 生成摘要，取最近 {Count} 条消息，摘要长度: {Length}",
            agentId, recentMessages.Count, summary.Length);
        return $"[任务摘要 - Agent {agentId}]\n{summary}";
    }
}
