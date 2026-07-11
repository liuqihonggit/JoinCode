namespace JoinCode.Abstractions.LLM.Chat;

[Register(typeof(ISessionStats))]
public sealed partial class SessionStats : ISessionStats
{
    public long CarryoverCacheHitTokens { get; private set; }
    public long CarryoverCacheMissTokens { get; private set; }
    public long TotalPromptTokens { get; private set; }
    public long TotalCompletionTokens { get; private set; }
    public long TotalCacheHitTokens { get; private set; }
    public long TotalCacheMissTokens { get; private set; }
    public int TurnCount { get; private set; }
    public int SystemPromptCacheBreaks { get; private set; }
    public int ToolSpecsCacheBreaks { get; private set; }
    public int DynamicContentCacheBreaks { get; private set; }
    public int CacheEvictionBreaks { get; private set; }
    public decimal TotalCostUsd => _carryoverCostUsd + _turnsCostUsd;
    private decimal _carryoverCostUsd;
    private decimal _turnsCostUsd;

    public double AggregateCacheHitRatio
    {
        get
        {
            var hit = CarryoverCacheHitTokens + TotalCacheHitTokens;
            var miss = CarryoverCacheMissTokens + TotalCacheMissTokens;
            var total = hit + miss;
            return total == 0 ? 0 : (double)hit / total;
        }
    }

    public void SeedCarryover(long cacheHitTokens, long cacheMissTokens, decimal totalCostUsd = 0)
    {
        CarryoverCacheHitTokens = cacheHitTokens;
        CarryoverCacheMissTokens = cacheMissTokens;
        _carryoverCostUsd = totalCostUsd;
    }

    public void RecordTurn(TokenUsage usage, decimal costUsd = 0, CacheBreakResult? cacheBreak = null)
    {
        ArgumentNullException.ThrowIfNull(usage);

        TotalPromptTokens += usage.PromptTokens;
        TotalCompletionTokens += usage.CompletionTokens;
        TotalCacheHitTokens += usage.CacheReadInputTokens;
        TotalCacheMissTokens += usage.CacheCreationInputTokens;
        _turnsCostUsd += costUsd;
        TurnCount++;

        if (cacheBreak is not null && cacheBreak.BreakDetected)
        {
            switch (cacheBreak.Kind)
            {
                case CacheBreakKind.SystemPromptChanged:
                    SystemPromptCacheBreaks++;
                    break;
                case CacheBreakKind.ToolSpecsChanged:
                    ToolSpecsCacheBreaks++;
                    break;
                case CacheBreakKind.DynamicContentChanged:
                    DynamicContentCacheBreaks++;
                    break;
                case CacheBreakKind.CacheEviction:
                    CacheEvictionBreaks++;
                    break;
            }
        }
    }

    public void Reset()
    {
        CarryoverCacheHitTokens = 0;
        CarryoverCacheMissTokens = 0;
        TotalPromptTokens = 0;
        TotalCompletionTokens = 0;
        TotalCacheHitTokens = 0;
        TotalCacheMissTokens = 0;
        _carryoverCostUsd = 0;
        _turnsCostUsd = 0;
        TurnCount = 0;
        SystemPromptCacheBreaks = 0;
        ToolSpecsCacheBreaks = 0;
        DynamicContentCacheBreaks = 0;
        CacheEvictionBreaks = 0;
    }

    public SessionMeta ToMeta()
    {
        return new SessionMeta
        {
            CacheHitTokens = CarryoverCacheHitTokens + TotalCacheHitTokens,
            CacheMissTokens = CarryoverCacheMissTokens + TotalCacheMissTokens,
            LastPromptTokens = TurnCount > 0 ? (int)(TotalPromptTokens / TurnCount) : 0,
            TurnCount = TurnCount,
            TotalCostUsd = _carryoverCostUsd + TotalCostUsd
        };
    }
}
