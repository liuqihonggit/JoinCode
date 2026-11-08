namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 上下文折叠执行器，调用摘要器对头部可折叠消息生成摘要并原地重写日志
/// </summary>
public sealed partial class ContextFoldExecutor {
    private const string HistoryFoldMarker =
        "[CONVERSATION HISTORY SUMMARY — earlier turns folded for context efficiency]\n\n";

    private readonly IFoldSummarizer _summarizer;
    private readonly ILogger<ContextFoldExecutor>? _logger;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="summarizer">折叠摘要器</param>
    /// <param name="logger">日志记录器（可选）</param>
    public ContextFoldExecutor(IFoldSummarizer summarizer, ILogger<ContextFoldExecutor>? logger = null) {
        _summarizer = summarizer ?? throw new ArgumentNullException(nameof(summarizer));
        _logger = logger;
    }

    /// <summary>
    /// 执行上下文折叠：计算边界、分区可折叠消息、生成摘要并原地重写日志
    /// </summary>
    /// <param name="log">会话消息日志，原地改写</param>
    /// <param name="ctxMax">上下文窗口大小</param>
    /// <param name="aggressive">是否使用激进折叠比例</param>
    /// <param name="thresholds">折叠阈值（可选，默认使用 Default）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>折叠结果，包含是否折叠、原始/头部/尾部消息数与摘要</returns>
    public async Task<ContextFoldResult> FoldAsync(
        AppendOnlyLog log,
        int ctxMax,
        bool aggressive,
        ContextFoldThresholds? thresholds = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(log);

        var t = thresholds ?? ContextFoldThresholds.Default;
        var messages = log.ToMessages();

        if (messages.Count == 0) {
            return new ContextFoldResult {
                Folded = false,
                OriginalMessageCount = 0,
                HeadMessageCount = 0,
                TailMessageCount = 0,
                Decision = aggressive ? ContextFoldDecision.FoldAggressive : ContextFoldDecision.FoldNormal
            };
        }

        var boundary = ContextFoldDecider.ComputeTailBoundary(messages, ctxMax, aggressive, t);

        if (boundary == 0) {
            _logger?.LogDebug("Fold boundary is 0, nothing to fold");
            return new ContextFoldResult {
                Folded = false,
                OriginalMessageCount = messages.Count,
                HeadMessageCount = 0,
                TailMessageCount = messages.Count,
                Decision = aggressive ? ContextFoldDecision.FoldAggressive : ContextFoldDecision.FoldNormal
            };
        }

        if (!ContextFoldDecider.ShouldFold(messages, 0, boundary, ctxMax, t)) {
            _logger?.LogDebug("Head portion too small to fold (boundary={Boundary}, total={Total})", boundary, messages.Count);
            return new ContextFoldResult {
                Folded = false,
                OriginalMessageCount = messages.Count,
                HeadMessageCount = boundary,
                TailMessageCount = messages.Count - boundary,
                Decision = aggressive ? ContextFoldDecision.FoldAggressive : ContextFoldDecision.FoldNormal
            };
        }

        var head = messages.Take(boundary).ToList();
        var tail = messages.Skip(boundary).ToList();

        var (kept, foldable) = PartitionFold(head);

        _logger?.LogInformation("Folding context: head={HeadCount} (kept={KeptCount}, foldable={FoldableCount}), tail={TailCount}, aggressive={Aggressive}",
            head.Count, kept.Count, foldable.Count, tail.Count, aggressive);

        if (foldable.Count == 0) {
            _logger?.LogDebug("No foldable messages in head, skipping fold");
            return new ContextFoldResult {
                Folded = false,
                OriginalMessageCount = messages.Count,
                HeadMessageCount = head.Count,
                TailMessageCount = tail.Count,
                Decision = aggressive ? ContextFoldDecision.FoldAggressive : ContextFoldDecision.FoldNormal
            };
        }

        var summary = await _summarizer.SummarizeForFoldAsync(foldable, cancellationToken).ConfigureAwait(false);

        var summaryMsg = new ApiMessage(MessageRole.Assistant, HistoryFoldMarker + summary);

        var replacement = new List<ApiMessage>(kept.Count + 1 + tail.Count);
        replacement.AddRange(kept);
        replacement.Add(summaryMsg);
        replacement.AddRange(tail);

        log.CompactInPlace(replacement);

        _logger?.LogInformation("Context folded: {OriginalCount} → {NewCount} messages (kept {KeptCount} user/summary messages)",
            messages.Count, replacement.Count, kept.Count);

        return new ContextFoldResult {
            Folded = true,
            OriginalMessageCount = messages.Count,
            HeadMessageCount = head.Count,
            TailMessageCount = tail.Count,
            Summary = summary,
            Decision = aggressive ? ContextFoldDecision.FoldAggressive : ContextFoldDecision.FoldNormal
        };
    }

    /// <summary>
    /// 裁剪末尾工具调用并准备以摘要退出
    /// </summary>
    /// <param name="log">会话消息日志，原地改写</param>
    /// <returns>折叠结果，标记为 ExitWithSummary 决策</returns>
    public ContextFoldResult TrimTrailingAndPrepareExit(AppendOnlyLog log) {
        ArgumentNullException.ThrowIfNull(log);

        var trimmed = ContextFoldDecider.TrimTrailingToolCalls(log);

        return new ContextFoldResult {
            Folded = trimmed,
            OriginalMessageCount = log.Count + (trimmed ? 1 : 0),
            HeadMessageCount = 0,
            TailMessageCount = log.Count,
            Decision = ContextFoldDecision.ExitWithSummary
        };
    }

    private static (List<ApiMessage> Kept, List<ApiMessage> Foldable) PartitionFold(IReadOnlyList<ApiMessage> head) {
        var kept = new List<ApiMessage>();
        var foldable = new List<ApiMessage>();

        foreach (var msg in head) {
            if (IsPinnable(msg)) {
                kept.Add(msg);
            } else {
                foldable.Add(msg);
            }
        }

        return (kept, foldable);
    }

    private static bool IsPinnable(ApiMessage msg) {
        if (IsCompactSummary(msg)) {
            return true;
        }

        if (msg.Role == MessageRole.User && PinnableUserTurn(msg)) {
            return true;
        }

        return false;
    }

    private static bool IsCompactSummary(ApiMessage msg) {
        return msg.Metadata != null
            && msg.Metadata.TryGetValue("isCompactSummary", out var val)
            && val.ValueKind == JsonValueKind.True;
    }

    private static bool PinnableUserTurn(ApiMessage msg) {
        if (string.IsNullOrEmpty(msg.Content)) {
            return false;
        }

        return msg.Content.Length <= 500;
    }
}