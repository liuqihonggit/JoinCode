namespace JoinCode.Abstractions.LLM.Chat;

public sealed class CacheBreakDetector
{
    public PromptStateSnapshot RecordPromptState(ImmutablePrefix prefix, string dynamicContent)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(dynamicContent);

        return new PromptStateSnapshot
        {
            SystemPromptHash = ComputeContentHash(prefix.System),
            ToolSpecsHash = ComputeToolSpecsHash(prefix),
            ToolCount = prefix.ToolSpecs.Count,
            ToolNamesHash = ComputeToolNamesHash(prefix),
            DynamicContentHash = ComputeContentHash(dynamicContent),
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

        var currentSystemHash = ComputeContentHash(currentPrefix.System);
        if (snapshot.SystemPromptHash != currentSystemHash)
        {
            return CacheBreakResult.Break(CacheBreakKind.SystemPromptChanged,
                $"System prompt changed: hash {snapshot.SystemPromptHash} → {currentSystemHash}");
        }

        var currentToolSpecsHash = ComputeToolSpecsHash(currentPrefix);
        ToolDriftReport? toolDrift = null;
        if (snapshot.ToolSpecsHash != currentToolSpecsHash || snapshot.ToolCount != currentPrefix.ToolSpecs.Count)
        {
            toolDrift = ToolListDriftClassifier.Classify(snapshot.ToolSpecs, currentPrefix.ToolSpecs);

            if (!toolDrift.IsCacheSafe || usage.CacheReadInputTokens == 0)
            {
                return CacheBreakResult.Break(CacheBreakKind.ToolSpecsChanged,
                    $"Tool specs changed: {toolDrift.Kind} — {toolDrift.Summary}, cache hit={usage.CacheReadInputTokens}",
                    toolDrift);
            }
        }

        var currentDynamicHash = ComputeContentHash(currentDynamicContent);
        if (snapshot.DynamicContentHash != currentDynamicHash)
        {
            return CacheBreakResult.Break(CacheBreakKind.DynamicContentChanged,
                "Dynamic system content changed");
        }

        if (usage.CacheReadInputTokens == 0 && usage.CacheCreationInputTokens > 0
            && snapshot.SystemPromptHash == currentSystemHash
            && snapshot.ToolSpecsHash == currentToolSpecsHash
            && snapshot.DynamicContentHash == currentDynamicHash)
        {
            return CacheBreakResult.Break(CacheBreakKind.CacheEviction,
                "Cache miss despite identical prefix — likely TTL eviction");
        }

        return new CacheBreakResult { BreakDetected = false, Kind = CacheBreakKind.None, ToolDrift = toolDrift };
    }

    private static string ComputeToolNamesHash(ImmutablePrefix prefix)
    {
        var blob = string.Join(",", prefix.ToolSpecs.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal));
        return ComputeContentHash(blob);
    }

    private static string ComputeToolSpecsHash(ImmutablePrefix prefix)
    {
        var sortedSpecs = prefix.ToolSpecs.OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.Description, StringComparer.Ordinal);
        var blob = string.Join("|", sortedSpecs.Select(t => $"{t.Name}:{t.Description}"));
        return ComputeContentHash(blob);
    }

    private static string ComputeContentHash(string content)
    {
        var hash = global::System.Security.Cryptography.SHA256.HashData(
            global::System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash)[..16];
    }
}
