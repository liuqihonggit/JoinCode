namespace Core.Context;

/// <summary>
/// 聊天用量处理器 — 从 ChatService 提取
/// 负责费率限制提取、成本计算、Usage 处理、指标记录
/// </summary>
[Register]
public sealed partial class ChatUsageProcessor : IChatUsageProcessor
{
    private readonly ISessionStats _sessionStats;
    private readonly IChatContextManager _contextManager;
    private readonly ICostTracker? _costTracker;
    private readonly IRateLimitTracker? _rateLimitTracker;
    [Inject] private readonly ILogger<ChatUsageProcessor>? _logger;

    /// <summary>
    /// 初始化用量处理器
    /// </summary>
    public ChatUsageProcessor(
        ISessionStats sessionStats,
        IChatContextManager contextManager,
        ICostTracker? costTracker = null,
        IRateLimitTracker? rateLimitTracker = null,
        ILogger<ChatUsageProcessor>? logger = null)
    {
        _sessionStats = sessionStats;
        _contextManager = contextManager;
        _costTracker = costTracker;
        _rateLimitTracker = rateLimitTracker;
        _logger = logger;
    }

    /// <summary>
    /// 处理 Usage 数据：计算成本、记录统计、检查缓存失效、上下文折叠
    /// </summary>
    public async Task ProcessUsageAsync(TokenUsage usage, string? modelId, PromptStateSnapshot promptSnapshot, CancellationToken ct)
    {
        var costUsd = ComputeCostUsd(modelId, usage);

        var cacheBreakResult = await _contextManager.CheckCacheBreakAsync(promptSnapshot, usage, ct).ConfigureAwait(false);
        if (cacheBreakResult.BreakDetected)
        {
            _logger?.LogWarning("缓存失效: Kind={Kind}, Detail={Detail}", cacheBreakResult.Kind, cacheBreakResult.Detail);
        }

        _sessionStats.RecordTurn(usage, costUsd, cacheBreakResult);

        var foldDecision = _contextManager.DecideAfterUsage(usage);
        if (foldDecision != ContextFoldDecision.None)
        {
            var foldResult = await _contextManager.FoldIfNeededAsync(foldDecision, ct).ConfigureAwait(false);
            if (foldResult.Folded)
            {
                _logger?.LogInformation("上下文折叠已执行: {Decision}, 原始 {Original} 条 → 保留 {Tail} 条 + 摘要",
                    foldDecision, foldResult.OriginalMessageCount, foldResult.TailMessageCount);
            }
        }
    }

    /// <summary>
    /// 从流式响应元数据中提取费率限制数据
    /// </summary>
    public void TryExtractRateLimitData(IReadOnlyDictionary<string, JsonElement> metadata)
    {
        var snapshot = new RateLimitSnapshot();
        var rateLimitTracker = _rateLimitTracker ?? throw new InvalidOperationException("RateLimitTracker not available.");

        if (metadata.TryGetValue("ratelimit_x-ratelimit-limit-requests", out var limitReqEl) && limitReqEl.ValueKind == JsonValueKind.String && int.TryParse(limitReqEl.GetString(), out var limitReq))
            snapshot = snapshot with { RequestLimit = limitReq };

        if (metadata.TryGetValue("ratelimit_x-ratelimit-remaining-requests", out var remReqEl) && remReqEl.ValueKind == JsonValueKind.String && int.TryParse(remReqEl.GetString(), out var remReq))
            snapshot = snapshot with { RequestRemaining = remReq };

        if (metadata.TryGetValue("ratelimit_x-ratelimit-reset-requests", out var resetReqEl) && resetReqEl.ValueKind == JsonValueKind.String && DateTime.TryParse(resetReqEl.GetString(), out var resetReq))
            snapshot = snapshot with { RequestResetsAt = resetReq.ToUniversalTime() };

        if (metadata.TryGetValue("ratelimit_x-ratelimit-limit-tokens", out var limitTokEl) && limitTokEl.ValueKind == JsonValueKind.String && int.TryParse(limitTokEl.GetString(), out var limitTok))
            snapshot = snapshot with { TokenLimit = limitTok };

        if (metadata.TryGetValue("ratelimit_x-ratelimit-remaining-tokens", out var remTokEl) && remTokEl.ValueKind == JsonValueKind.String && int.TryParse(remTokEl.GetString(), out var remTok))
            snapshot = snapshot with { TokenRemaining = remTok };

        if (metadata.TryGetValue("ratelimit_x-ratelimit-reset-tokens", out var resetTokEl) && resetTokEl.ValueKind == JsonValueKind.String && DateTime.TryParse(resetTokEl.GetString(), out var resetTok))
            snapshot = snapshot with { TokenResetsAt = resetTok.ToUniversalTime() };

        if (snapshot.RequestLimit.HasValue || snapshot.TokenLimit.HasValue)
        {
            rateLimitTracker.Update(snapshot);
        }
    }

    /// <summary>
    /// 计算成本（美元）
    /// </summary>
    public decimal ComputeCostUsd(string? modelId, TokenUsage usage)
    {
        if (string.IsNullOrEmpty(modelId))
        {
            return 0;
        }

        if (_costTracker is not null)
        {
            _costTracker.RecordUsage(
                modelId,
                usage.PromptTokens,
                usage.CompletionTokens,
                usage.CacheCreationInputTokens,
                usage.CacheReadInputTokens);
        }

        var promptCostPer1K = ModelPricingTable.GetPromptCostPer1K(modelId);
        var completionCostPer1K = ModelPricingTable.GetCompletionCostPer1K(modelId);
        var promptCost = usage.PromptTokens / 1000m * promptCostPer1K;
        var completionCost = usage.CompletionTokens / 1000m * completionCostPer1K;
        var cacheCreationCost = usage.CacheCreationInputTokens / 1000m * promptCostPer1K * 1.25m;
        var cacheReadCost = usage.CacheReadInputTokens / 1000m * promptCostPer1K * 0.1m;

        return promptCost + completionCost + cacheCreationCost + cacheReadCost;
    }
}
