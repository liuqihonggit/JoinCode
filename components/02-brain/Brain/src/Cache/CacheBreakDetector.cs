namespace JoinCode.Abstractions.LLM.Chat;

public class CacheBreakDetector
{
    private bool _hasPreviousCacheHit;

    public PromptStateSnapshot RecordPromptState(ImmutablePrefix prefix, string dynamicContent)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(dynamicContent);

        return new PromptStateSnapshot
        {
            SystemPromptHash = ContentHash.Compute(prefix.System),
            ToolSpecsHash = ContentHash.ComputeToolSpecs(prefix.ToolSpecs),
            ToolCount = prefix.ToolSpecs.Count,
            ToolNamesHash = ContentHash.ComputeToolNames(prefix.ToolSpecs),
            DynamicContentHash = ContentHash.Compute(dynamicContent),
            ToolSpecs = prefix.ToolSpecs.ToList()
        };
    }

    public CacheBreakResult CheckCacheBreak(
        PromptStateSnapshot snapshot,
        ImmutablePrefix currentPrefix,
        string currentDynamicContent,
        TokenUsage usage)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentPrefix);
        ArgumentNullException.ThrowIfNull(usage);

        if (usage.CacheReadInputTokens > 0)
        {
            _hasPreviousCacheHit = true;
        }

        var currentSystemHash = ContentHash.Compute(currentPrefix.System);
        if (snapshot.SystemPromptHash != currentSystemHash)
        {
            return CacheBreakResult.Break(CacheBreakKind.SystemPromptChanged,
                $"System prompt changed: hash {snapshot.SystemPromptHash} → {currentSystemHash}");
        }

        var currentToolSpecsHash = ContentHash.ComputeToolSpecs(currentPrefix.ToolSpecs);
        ToolDriftReport? toolDrift = null;
        if (snapshot.ToolSpecsHash != currentToolSpecsHash || snapshot.ToolCount != currentPrefix.ToolSpecs.Count)
        {
            toolDrift = ToolListDriftClassifier.Classify(snapshot.ToolSpecs, currentPrefix.ToolSpecs);

            if (ShouldReportToolSpecsBreak(toolDrift, usage))
            {
                return CacheBreakResult.Break(CacheBreakKind.ToolSpecsChanged,
                    $"Tool specs changed: {toolDrift.Kind} — {toolDrift.Summary}, cache hit={usage.CacheReadInputTokens}",
                    toolDrift);
            }
        }

        var currentDynamicHash = ContentHash.Compute(currentDynamicContent);
        if (snapshot.DynamicContentHash != currentDynamicHash)
        {
            return CacheBreakResult.Break(CacheBreakKind.DynamicContentChanged,
                "Dynamic system content changed");
        }

        var allHashesMatch = snapshot.SystemPromptHash == currentSystemHash
            && snapshot.ToolSpecsHash == currentToolSpecsHash
            && snapshot.DynamicContentHash == currentDynamicHash;
        if (ShouldReportCacheEviction(usage, allHashesMatch))
        {
            return CacheBreakResult.Break(CacheBreakKind.CacheEviction,
                "Cache miss despite identical prefix — likely TTL eviction");
        }

        return new CacheBreakResult { BreakDetected = false, Kind = CacheBreakKind.None, ToolDrift = toolDrift };
    }

    protected virtual bool ShouldReportToolSpecsBreak(ToolDriftReport drift, TokenUsage usage)
    {
        if (!drift.IsCacheSafe) return true;
        if (!_hasPreviousCacheHit) return false;
        return usage.CacheReadInputTokens == 0;
    }

    protected virtual bool ShouldReportCacheEviction(TokenUsage usage, bool allHashesMatch)
    {
        if (!_hasPreviousCacheHit) return false;
        return allHashesMatch && usage.CacheReadInputTokens == 0 && usage.CacheCreationInputTokens > 0;
    }
}
