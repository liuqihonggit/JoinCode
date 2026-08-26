namespace JoinCode.Abstractions.LLM.Chat;

public static class ContextFoldDecider
{
    public static ContextFoldDecision DecideAfterUsage(
        TokenUsage usage,
        int ctxMax,
        bool alreadyFoldedThisTurn,
        ContextFoldThresholds? thresholds = null,
        int deferralCount = 0)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (ctxMax <= 0) throw new ArgumentOutOfRangeException(nameof(ctxMax));

        var t = thresholds ?? ContextFoldThresholds.Default;
        var ratio = (double)usage.PromptTokens / ctxMax;

        if (ratio > t.ForceSummaryThreshold)
            return ContextFoldDecision.ExitWithSummary;

        if (alreadyFoldedThisTurn)
            return ContextFoldDecision.None;

        var action = ContextFoldDecision.None;
        if (ratio > t.AggressiveThreshold)
            action = ContextFoldDecision.FoldAggressive;
        else if (ratio > t.FoldThreshold)
            action = ContextFoldDecision.FoldNormal;

        if (action != ContextFoldDecision.None
            && usage.CacheReadInputTokens > 0
            && deferralCount < t.DeferFoldLimit)
        {
            return ContextFoldDecision.Deferred;
        }

        return action;
    }

    public static PreflightDecision DecidePreflight(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlyList<ToolSpec> toolSpecs,
        int ctxMax,
        ContextFoldThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(toolSpecs);
        if (ctxMax <= 0) throw new ArgumentOutOfRangeException(nameof(ctxMax));

        var t = thresholds ?? ContextFoldThresholds.Default;
        var estimate = EstimateTokenCount(messages, toolSpecs, t);
        var ratio = (double)estimate / ctxMax;

        return new PreflightDecision
        {
            NeedsAction = ratio > t.EmergencyThreshold,
            EstimatedRatio = ratio
        };
    }

    public static int EstimateTokenCount(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlyList<ToolSpec> toolSpecs,
        ContextFoldThresholds? thresholds = null)
    {
        var t = thresholds ?? ContextFoldThresholds.Default;
        var totalChars = 0;

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (msg.Content != null)
                totalChars += msg.Content.Length;
        }

        for (var i = 0; i < toolSpecs.Count; i++)
        {
            var spec = toolSpecs[i];
            totalChars += spec.Name.Length;
            if (spec.Description != null) totalChars += spec.Description.Length;
            if (spec.InputSchemaJson != null) totalChars += spec.InputSchemaJson.Length;
        }

        return totalChars / t.CharsPerToken;
    }

    public static int ComputeTailBoundary(
        IReadOnlyList<ApiMessage> messages,
        int ctxMax,
        bool aggressive,
        ContextFoldThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (ctxMax <= 0) throw new ArgumentOutOfRangeException(nameof(ctxMax));

        var t = thresholds ?? ContextFoldThresholds.Default;
        var tailFraction = aggressive ? t.AggressiveTailFraction : t.TailFraction;
        var tailTokenBudget = (int)(ctxMax * tailFraction);
        var tailCharBudget = tailTokenBudget * t.CharsPerToken;

        var charCount = 0;
        var boundary = messages.Count;

        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            var msgChars = msg.Content?.Length ?? 0;

            if (charCount + msgChars > tailCharBudget)
            {
                boundary = i + 1;
                break;
            }

            charCount += msgChars;

            if (msg.Role == MessageRole.User)
            {
                boundary = i;
            }
        }

        if (boundary == messages.Count && charCount <= tailCharBudget)
        {
            boundary = 0;
        }

        return boundary;
    }

    public static bool ShouldFold(
        IReadOnlyList<ApiMessage> messages,
        int headStart,
        int headEnd,
        int ctxMax,
        ContextFoldThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (ctxMax <= 0) throw new ArgumentOutOfRangeException(nameof(ctxMax));

        var t = thresholds ?? ContextFoldThresholds.Default;
        var headChars = 0;
        var totalChars = 0;

        for (var i = 0; i < messages.Count; i++)
        {
            var msgChars = messages[i].Content?.Length ?? 0;
            totalChars += msgChars;
            if (i >= headStart && i < headEnd)
                headChars += msgChars;
        }

        if (totalChars == 0) return false;

        var headFraction = (double)headChars / totalChars;
        return headFraction >= t.MinSavingsFraction;
    }

    /// <summary>
    /// Determines whether repeated folding has made no progress (stuck).
    /// Mirrors the upstream Go reference's <c>compactStuck</c> guard: when the
    /// window is too small for a fold to reduce pressure, repeated attempts
    /// would re-fire every turn, so after <paramref name="limit"/> consecutive
    /// no-progress folds the caller should pause auto-folding.
    /// </summary>
    /// <param name="consecutiveNoProgressFolds">Number of consecutive folds that
    /// returned no saved tokens.</param>
    /// <param name="limit">Maximum tolerated consecutive no-progress folds before
    /// declaring the fold stuck.</param>
    /// <returns>True when the fold is considered stuck.</returns>
    public static bool IsFoldStuck(int consecutiveNoProgressFolds, int limit)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        return consecutiveNoProgressFolds >= limit;
    }

    public static bool TrimTrailingToolCalls(AppendOnlyLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        if (log.Count == 0) return false;

        var last = log[log.Count - 1];
        if (last.Role != MessageRole.Assistant || !HasToolCalls(last))
            return false;

        var hasContent = !string.IsNullOrWhiteSpace(last.Content);

        log.CompactInPlace(RemoveLastAndKeepText(log, hasContent, last));

        return true;
    }

/// <summary>
    /// 剪裁折叠保护区（tail boundary）之前的过期大工具结果 — 对齐 Reasonix Go 版 SnipStaleToolResults。
    /// 工具结果可重派生，重写其内容无需调用摘要器、不丢弃消息，只把超长内容压成"头尾行保留"的占位符。
    /// 幂等：已剪裁（带 snipped 标记）的结果不再重复剪裁；保护区内的结果原样保留。
    /// 多模态工具结果（含 ContentBlocks）不剪裁 — 只改文本会静默丢失图片/二进制块并破坏 tool_call 配对。
    /// </summary>
    /// <param name="log">会话消息日志，原地改写。</param>
    /// <param name="ctxMax">上下文窗口大小。</param>
    /// <param name="thresholds">折叠阈值（默认使用 <see cref="ContextFoldThresholds.Default"/>）。</param>
    /// <returns>本次剪裁统计。</returns>
    public static SnipStats SnipStaleToolResults(AppendOnlyLog log, int ctxMax, ContextFoldThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (ctxMax <= 0) throw new ArgumentOutOfRangeException(nameof(ctxMax));

        var t = thresholds ?? ContextFoldThresholds.Default;
        var messages = log.ToMessages();
        if (messages.Count == 0) return new SnipStats();

        // L4 prune 门槛：PruneMinimumTokens 按 ctxMax 比例缩放，避免小上下文场景门槛过高
        var pruneMinTokens = Math.Min(t.PruneMinimumTokens, ctxMax / 10);
        var minSnipChars = Math.Max(t.MinSnipChars, pruneMinTokens * t.CharsPerToken);

        var boundary = ComputeTailBoundary(messages, ctxMax, aggressive: false, t);

        // 兜底：末条消息单独超预算时 ComputeTailBoundary 归零（整个日志被视作保护区）。
        // 对齐 Go tailStart 的 minKeep 下限，仍保护最近 RecentKeepTailMessages 条，
        // 允许剪裁更早的过期大工具结果 — 否则末条巨大时前面永不再剪。
        if (boundary == 0 && messages.Count > t.RecentKeepTailMessages)
        {
            boundary = messages.Count - t.RecentKeepTailMessages;
        }

        var index = 0;
        var saved = 0;
        var changed = false;
        var rewritten = new List<ApiMessage>(messages.Count);

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            if (i < boundary && msg.Role == MessageRole.Tool
                && (msg.Content?.Length ?? 0) >= minSnipChars
                && msg.ContentBlocks is null or { Count: 0 }
                && !IsSnipped(msg))
            {
                var replacement = RewriteSnipped(msg, t);

                // 剪裁必须承诺严格变短：行数仅略超 head+tail 阈值时，保留的
                // 80 行加上 marker 头可能反超原文（SavedChars 变负、上下文膨胀）。
                // 此时保持原文不动，跳过本轮剪裁。
                if (replacement.Length >= (msg.Content?.Length ?? 0))
                {
                    rewritten.Add(msg);
                    continue;
                }

                saved += (msg.Content?.Length ?? 0) - replacement.Length;
                rewritten.Add(new ApiMessage(msg.Role, replacement, msg.Metadata, msg.ModelId, msg.TokenUsage)
                {
                    ContentBlocks = msg.ContentBlocks ?? []
                });
                index++;
                changed = true;
                continue;
            }

            rewritten.Add(msg);
        }

        if (changed)
        {
            log.CompactInPlace(rewritten);
        }

        return new SnipStats { Results = index, SavedChars = saved };
    }

    /// <summary>判定消息是否已被剪裁过（内容带 snipped 标记）。</summary>
    private static bool IsSnipped(ApiMessage msg) =>
        msg.Content != null && msg.Content.StartsWith("snipped:", StringComparison.Ordinal);

    /// <summary>
    /// 把工具结果压缩为"头 N 行 + 省略标记 + 尾 M 行"占位符，保留 head/tail 行语义。
    /// 对齐 Reasonix Go 版 snipToolResult 的头尾行保留策略（side-effecting 默认 40/40）。
    /// </summary>
    private static string RewriteSnipped(ApiMessage msg, ContextFoldThresholds t)
    {
        var content = (msg.Content ?? string.Empty).TrimEnd('\n', '\r');
        var toolName = msg.ExtractToolName() ?? "tool";
        var lines = content.Split('\n');

        string head;
        string tail;
        int omitted;

        if (lines.Length > t.SnipHeadLines + t.SnipTailLines)
        {
            head = string.Join("\n", lines.Take(t.SnipHeadLines));
            tail = string.Join("\n", lines.TakeLast(t.SnipTailLines));
            omitted = lines.Length - t.SnipHeadLines - t.SnipTailLines;
            return $"snipped: {toolName} ({content.Length} chars, {omitted} lines omitted; rerun tool to restore)\n" +
                   $"{head}\n[... {omitted} lines omitted ...]\n{tail}";
        }

        var headChars = Math.Min(t.SnipHeadChars, content.Length / 2);
        var tailChars = Math.Min(t.SnipTailChars, content.Length / 4);
        head = content[..headChars];
        tail = content[^tailChars..];
        return $"snipped: {toolName} ({content.Length} chars; rerun tool to restore)\n" +
               $"{head}\n[... {content.Length - headChars - tailChars} chars omitted ...]\n{tail}";
    }

    private static bool HasToolCalls(ApiMessage msg)
    {
        return msg.Metadata != null &&
            (msg.Metadata.ContainsKey("ToolCall") || msg.Metadata.ContainsKey("ToolCalls"));
    }

    private static IReadOnlyList<ApiMessage> RemoveLastAndKeepText(AppendOnlyLog log, bool keepText, ApiMessage last)
    {
        var result = new List<ApiMessage>(log.Count);

        for (var i = 0; i < log.Count - 1; i++)
        {
            result.Add(log[i]);
        }

        if (keepText)
        {
            result.Add(new ApiMessage(MessageRole.Assistant, last.Content));
        }

        return result;
    }
}
