namespace Core.Agents;

/// <summary>
/// 子智能体摘要状态 — L2 自摘要结果
/// </summary>
public enum SubAgentSummaryStatus
{
    /// <summary>
    /// 输出在预算内，不需要摘要
    /// </summary>
    NotNeeded,

    /// <summary>
    /// 配置关闭或客户端不可用，跳过 L2
    /// </summary>
    Skipped,

    /// <summary>
    /// 自摘要成功，Summary 有值且在预算内
    /// </summary>
    Success,

    /// <summary>
    /// 尝试失败（LLM 返回 null 或摘要仍超预算），调用方应走 L3 落盘
    /// </summary>
    Failed,
}

/// <summary>
/// 子智能体摘要结果
/// </summary>
public sealed record SubAgentSummaryResult(SubAgentSummaryStatus Status, string? Summary = null)
{
    /// <summary>
    /// 跳过（配置关或客户端不可用）
    /// </summary>
    public static readonly SubAgentSummaryResult Skipped = new(SubAgentSummaryStatus.Skipped);

    /// <summary>
    /// 不需要（输出在预算内）
    /// </summary>
    public static readonly SubAgentSummaryResult NotNeeded = new(SubAgentSummaryStatus.NotNeeded);
}

/// <summary>
/// 子智能体摘要生成器 — L2 自摘要层
/// <para>当子智能体输出超过剩余预算时，调 ISubAgentSummaryClient（LLM）压成 ≤预算 的连贯摘要。</para>
/// <para>失败时返回 Failed，调用方应走 L3 落盘指针兜底。</para>
/// </summary>
[Register(typeof(SubAgentSummaryGenerator), ServiceLifetime.Singleton)]
public sealed partial class SubAgentSummaryGenerator : ServiceEntity
{
    private readonly ILogger<SubAgentSummaryGenerator>? _logger;
    private readonly ISubAgentSummaryClient? _client;
    private readonly SubAgentSummaryConfig _config;

    public SubAgentSummaryGenerator(
        ISubAgentSummaryClient? client = null,
        SubAgentSummaryConfig? config = null,
        ILogger<SubAgentSummaryGenerator>? logger = null)
    {
        _client = client;
        _config = config ?? new SubAgentSummaryConfig();
        _logger = logger;
    }

    /// <summary>
    /// 尝试将子智能体输出自摘要为不超过 remainingTokenBudget 的连贯摘要
    /// </summary>
    /// <param name="agentId">子智能体标识</param>
    /// <param name="output">子智能体完整输出</param>
    /// <param name="remainingTokenBudget">主智能体剩余 token 预算</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task<SubAgentSummaryResult> TrySummarizeAsync(
        string agentId,
        string output,
        int remainingTokenBudget,
        CancellationToken cancellationToken = default)
    {
        if (_client is null || !_config.Auto)
            return SubAgentSummaryResult.Skipped;

        var outputTokens = SubAgentOutputTruncator.EstimateTokens(output);
        if (outputTokens <= remainingTokenBudget)
            return SubAgentSummaryResult.NotNeeded;

        var summary = await CallWithRetriesAsync(agentId, output, remainingTokenBudget, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(summary))
        {
            _logger?.LogWarning("子智能体 {AgentId} L2 自摘要失败：LLM 返回空", agentId);
            return new SubAgentSummaryResult(SubAgentSummaryStatus.Failed);
        }

        var summaryTokens = SubAgentOutputTruncator.EstimateTokens(summary);
        if (summaryTokens > remainingTokenBudget)
        {
            _logger?.LogWarning("子智能体 {AgentId} L2 自摘要仍超预算：{SummaryTokens} > {Budget}", agentId, summaryTokens, remainingTokenBudget);
            return new SubAgentSummaryResult(SubAgentSummaryStatus.Failed);
        }

        _logger?.LogInformation("子智能体 {AgentId} L2 自摘要成功：{Original} → {Summary} tokens", agentId, outputTokens, summaryTokens);
        return new SubAgentSummaryResult(SubAgentSummaryStatus.Success, summary);
    }

    private async Task<string?> CallWithRetriesAsync(
        string agentId,
        string output,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _config.MaxRetries + 1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = await _client!.SummarizeAsync(output, agentId, maxOutputTokens, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(result))
                    return result;

                _logger?.LogDebug("子智能体 {AgentId} L2 自摘要第 {Attempt} 次返回空", agentId, attempt);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "子智能体 {AgentId} L2 自摘要第 {Attempt} 次异常", agentId, attempt);
            }
        }

        return null;
    }
}
