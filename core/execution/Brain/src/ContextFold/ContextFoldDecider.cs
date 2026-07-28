namespace JoinCode.Abstractions.LLM.Chat;

public static class ContextFoldDecider
{
    public static ContextFoldDecision DecideAfterUsage(
        TokenUsage usage,
        int ctxMax,
        bool alreadyFoldedThisTurn,
        ContextFoldThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (ctxMax <= 0) throw new ArgumentOutOfRangeException(nameof(ctxMax));

        var t = thresholds ?? ContextFoldThresholds.Default;
        var ratio = (double)usage.PromptTokens / ctxMax;

        if (ratio > t.ForceSummaryThreshold)
            return ContextFoldDecision.ExitWithSummary;

        if (alreadyFoldedThisTurn)
            return ContextFoldDecision.None;

        if (ratio > t.AggressiveThreshold)
            return ContextFoldDecision.FoldAggressive;

        if (ratio > t.FoldThreshold)
            return ContextFoldDecision.FoldNormal;

        return ContextFoldDecision.None;
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
