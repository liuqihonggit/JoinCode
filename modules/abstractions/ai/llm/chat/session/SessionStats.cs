namespace JoinCode.Abstractions.LLM.Chat;

[Register(typeof(ISessionStats), ServiceLifetime.Singleton)]
public sealed partial class SessionStats : ServiceEntity, ISessionStats {
    /// <summary>获取结转缓存命中 Token 数。</summary>
    public long CarryoverCacheHitTokens { get; private set; }
    /// <summary>获取结转缓存未命中 Token 数。</summary>
    public long CarryoverCacheMissTokens { get; private set; }
    /// <summary>获取提示词 Token 总数。</summary>
    public long TotalPromptTokens { get; private set; }
    /// <summary>获取补全 Token 总数。</summary>
    public long TotalCompletionTokens { get; private set; }
    /// <summary>获取缓存命中 Token 总数。</summary>
    public long TotalCacheHitTokens { get; private set; }
    /// <summary>获取缓存未命中 Token 总数。</summary>
    public long TotalCacheMissTokens { get; private set; }
    /// <summary>获取对话轮数。</summary>
    public int TurnCount { get; private set; }
    /// <summary>获取系统提示词缓存中断次数。</summary>
    public int SystemPromptCacheBreaks { get; private set; }
    /// <summary>获取工具规格缓存中断次数。</summary>
    public int ToolSpecsCacheBreaks { get; private set; }
    /// <summary>获取动态内容缓存中断次数。</summary>
    public int DynamicContentCacheBreaks { get; private set; }
    /// <summary>获取缓存驱逐中断次数。</summary>
    public int CacheEvictionBreaks { get; private set; }
    /// <summary>获取压缩进入中断次数。</summary>
    public int CompactionEnteredBreaks { get; private set; }
    /// <summary>获取 5 分钟 TTL 过期中断次数。</summary>
    public int TtlExpiration5MinBreaks { get; private set; }
    /// <summary>获取 1 小时 TTL 过期中断次数。</summary>
    public int TtlExpiration1HourBreaks { get; private set; }
    /// <summary>获取服务端路由中断次数。</summary>
    public int ServerSideRoutingBreaks { get; private set; }
    /// <summary>获取总成本（美元）。</summary>
    public decimal TotalCostUsd => _carryoverCostUsd + _turnsCostUsd;
    private decimal _carryoverCostUsd;
    private decimal _turnsCostUsd;

    /// <summary>获取聚合缓存命中率。</summary>
    public double AggregateCacheHitRatio {
        get {
            var hit = CarryoverCacheHitTokens + TotalCacheHitTokens;
            var miss = CarryoverCacheMissTokens + TotalCacheMissTokens;
            var total = hit + miss;
            return total == 0 ? 0 : (double)hit / total;
        }
    }

    /// <summary>种子结转缓存数据。</summary>
    public void SeedCarryover(long cacheHitTokens, long cacheMissTokens, decimal totalCostUsd = 0) {
        CarryoverCacheHitTokens = cacheHitTokens;
        CarryoverCacheMissTokens = cacheMissTokens;
        _carryoverCostUsd = totalCostUsd;
    }

    /// <summary>记录一轮对话的 Token 用量和缓存中断情况。</summary>
    public void RecordTurn(TokenUsage usage, decimal costUsd = 0, CacheBreakResult? cacheBreak = null) {
        ArgumentNullException.ThrowIfNull(usage);

        TotalPromptTokens += usage.PromptTokens;
        TotalCompletionTokens += usage.CompletionTokens;
        TotalCacheHitTokens += usage.CacheReadInputTokens;
        TotalCacheMissTokens += usage.CacheCreationInputTokens;
        _turnsCostUsd += costUsd;
        TurnCount++;

        if (cacheBreak is not null && cacheBreak.BreakDetected) {
            switch (cacheBreak.Kind) {
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
                case CacheBreakKind.CompactionEntered:
                CompactionEnteredBreaks++;
                break;
                case CacheBreakKind.TtlExpiration5Min:
                TtlExpiration5MinBreaks++;
                break;
                case CacheBreakKind.TtlExpiration1Hour:
                TtlExpiration1HourBreaks++;
                break;
                case CacheBreakKind.ServerSideRouting:
                ServerSideRoutingBreaks++;
                break;
            }
        }
    }

    /// <summary>重置所有统计信息。</summary>
    public void Reset() {
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
        CompactionEnteredBreaks = 0;
        TtlExpiration5MinBreaks = 0;
        TtlExpiration1HourBreaks = 0;
        ServerSideRoutingBreaks = 0;
    }

    /// <summary>转换为会话元数据。</summary>
    public SessionMeta ToMeta(long updatedAtUtcTicks = 0) {
        return new SessionMeta {
            CacheHitTokens = CarryoverCacheHitTokens + TotalCacheHitTokens,
            CacheMissTokens = CarryoverCacheMissTokens + TotalCacheMissTokens,
            LastPromptTokens = TurnCount > 0 ? (int)(TotalPromptTokens / TurnCount) : 0,
            TurnCount = TurnCount,
            TotalCostUsd = _carryoverCostUsd + TotalCostUsd,
            UpdatedAtUtcTicks = updatedAtUtcTicks
        };
    }
}