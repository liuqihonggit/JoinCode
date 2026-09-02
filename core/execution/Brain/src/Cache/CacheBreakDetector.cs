namespace JoinCode.Abstractions.LLM.Chat;

public class CacheBreakDetector
{
    private const double CacheEvictionRelativeThreshold = 0.95;
    private const int CacheEvictionAbsoluteThreshold = 2000;
    private static readonly TimeSpan Ttl5Min = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Ttl1Hour = TimeSpan.FromHours(1);

    private readonly Func<DateTimeOffset>? _clock;
    private bool _hasPreviousCacheHit;
    private bool _pendingCompaction;
    private bool _cacheDeletionsPending;
    private int? _prevCacheReadTokens;
    private DateTimeOffset? _lastCallTimestamp;

    public CacheBreakDetector(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock;
    }

    private DateTimeOffset Now => _clock?.Invoke() ?? DateTimeOffset.UtcNow;

    /// <summary>
    /// 通知检测器：cached microcompact 已发送 cache_edits deletions。
    /// 下一次 API 响应的 cache read tokens 会预期性下降，不应报为缓存破坏。
    /// </summary>
    public void NotifyCacheDeletion()
    {
        _cacheDeletionsPending = true;
    }

    /// <summary>
    /// 通知检测器：前缀已被主动压缩/折叠重写。重置缓存命中基线并标记待上报的压缩事件，
    /// 使随后的 cache miss 被归因为 <see cref="CacheBreakKind.CompactionEntered"/> 而非 <see cref="CacheBreakKind.CacheEviction"/>。
    /// </summary>
    public void NotifyCompaction()
    {
        _hasPreviousCacheHit = false;
        _pendingCompaction = true;
        _prevCacheReadTokens = null;
        _lastCallTimestamp = null;
    }

    /// <summary>
    /// 复位内部状态（新会话/重置缓存统计时调用）。
    /// </summary>
    public void Reset()
    {
        _hasPreviousCacheHit = false;
        _pendingCompaction = false;
        _cacheDeletionsPending = false;
        _prevCacheReadTokens = null;
        _lastCallTimestamp = null;
    }

    public PromptStateSnapshot RecordPromptState(
        ImmutablePrefix prefix,
        string dynamicContent,
        IReadOnlyList<ApiMessage>? conversation = null,
        string? modelId = null,
        bool? fastMode = null)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(dynamicContent);

        var conversationCount = conversation?.Count ?? 0;
        return new PromptStateSnapshot
        {
            SystemPromptHash = ContentHash.Compute(prefix.System),
            ToolSpecsHash = ContentHash.ComputeToolSpecs(prefix.ToolSpecs),
            ToolCount = prefix.ToolSpecs.Count(),
            ToolNamesHash = ContentHash.ComputeToolNames(prefix.ToolSpecs),
            DynamicContentHash = ContentHash.Compute(dynamicContent),
            ConversationHash = conversationCount > 0 ? ContentHash.ComputeConversation(conversation!) : string.Empty,
            ConversationCount = conversationCount,
            ToolSpecs = prefix.ToolSpecs.ToList(),
            ModelId = modelId,
            FastMode = fastMode
        };
    }

    public CacheBreakResult CheckCacheBreak(
        PromptStateSnapshot snapshot,
        ImmutablePrefix currentPrefix,
        string currentDynamicContent,
        TokenUsage usage,
        IReadOnlyList<ApiMessage>? currentConversation = null,
        string? currentModelId = null,
        bool? currentFastMode = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(currentPrefix);
        ArgumentNullException.ThrowIfNull(usage);

        if (IsExcludedModel(snapshot.ModelId))
        {
            return CacheBreakResult.NoBreak();
        }

        if (_cacheDeletionsPending)
        {
            _cacheDeletionsPending = false;
            _prevCacheReadTokens = usage.CacheReadInputTokens;
            _lastCallTimestamp = Now;
            return CacheBreakResult.NoBreak();
        }

        var now = Now;
        var timeSinceLastCall = _lastCallTimestamp is not null ? now - _lastCallTimestamp.Value : (TimeSpan?)null;
        _lastCallTimestamp = now;

        var prevCacheRead = _prevCacheReadTokens;
        _prevCacheReadTokens = usage.CacheReadInputTokens;

        if (usage.CacheReadInputTokens > 0)
        {
            _hasPreviousCacheHit = true;
        }

        if (IsModelChanged(snapshot, currentModelId))
        {
            return CacheBreakResult.Break(CacheBreakKind.ModelChanged,
                $"Model changed: {snapshot.ModelId ?? "(null)"} → {currentModelId ?? "(null)"}");
        }

        if (IsFastModeChanged(snapshot, currentFastMode))
        {
            return CacheBreakResult.Break(CacheBreakKind.FastModeChanged,
                $"Fast mode changed: {snapshot.FastMode?.ToString() ?? "(null)"} → {currentFastMode?.ToString() ?? "(null)"}");
        }

        var currentSystemHash = ContentHash.Compute(currentPrefix.System);
        if (snapshot.SystemPromptHash != currentSystemHash)
        {
            return CacheBreakResult.Break(CacheBreakKind.SystemPromptChanged,
                $"System prompt changed: hash {snapshot.SystemPromptHash} → {currentSystemHash}");
        }

        var currentToolSpecsHash = ContentHash.ComputeToolSpecs(currentPrefix.ToolSpecs);
        ToolDriftReport? toolDrift = null;
        if (snapshot.ToolSpecsHash != currentToolSpecsHash || snapshot.ToolCount != currentPrefix.ToolSpecs.Count())
        {
            toolDrift = ToolListDriftClassifier.Classify(snapshot.ToolSpecs, currentPrefix.ToolSpecs.ToList());

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

        // 消息序列前缀检测 — 对齐线上真实字节前缀。
        // 只比对快照时已存在的前 N 条消息：尾部追加（多轮增长）不破坏前缀，前缀变短（撤回）仍是可命中前缀，
        // 唯有既有前缀中的消息被篡改/插入会破坏真实线上前缀，必须上报。
        if (snapshot.ConversationCount > 0 && !string.IsNullOrEmpty(snapshot.ConversationHash))
        {
            var currentCount = currentConversation?.Count ?? 0;
            if (currentCount >= snapshot.ConversationCount)
            {
                var preserved = currentConversation!.Take(snapshot.ConversationCount).ToList();
                var preservedHash = ContentHash.ComputeConversation(preserved);
                if (preservedHash != snapshot.ConversationHash)
                {
                    return CacheBreakResult.Break(CacheBreakKind.ConversationHistoryChanged,
                        $"Conversation history prefix changed: hash {snapshot.ConversationHash} → {preservedHash} (first {snapshot.ConversationCount} messages)");
                }
            }
        }

        var allHashesMatch = snapshot.SystemPromptHash == currentSystemHash
            && snapshot.ToolSpecsHash == currentToolSpecsHash
            && snapshot.DynamicContentHash == currentDynamicHash;

        // 主动压缩后的首次全量 miss：归因为 CompactionEntered（本项目发起的重建），与驱逐无关
        if (_pendingCompaction
            && usage.CacheReadInputTokens == 0
            && usage.CacheCreationInputTokens > 0)
        {
            _pendingCompaction = false;
            return CacheBreakResult.Break(CacheBreakKind.CompactionEntered,
                "Cache miss after context compaction — prefix rebuilt by this session");
        }

        if (ShouldReportCacheEviction(usage, allHashesMatch, prevCacheRead))
        {
            var (kind, detail) = ClassifyCacheMiss(timeSinceLastCall);
            return CacheBreakResult.Break(kind, detail);
        }

        // 未发现失效：若此前压缩事件未触发到上报（本轮有缓存命中），清除待上报标记
        _pendingCompaction = false;
        return new CacheBreakResult { BreakDetected = false, Kind = CacheBreakKind.None, ToolDrift = toolDrift };
    }

    protected virtual bool ShouldReportToolSpecsBreak(ToolDriftReport drift, TokenUsage usage)
    {
        if (!drift.IsCacheSafe) return true;
        if (!_hasPreviousCacheHit) return false;
        return usage.CacheReadInputTokens == 0;
    }

    protected virtual bool ShouldReportCacheEviction(TokenUsage usage, bool allHashesMatch, int? prevCacheRead)
    {
        if (!_hasPreviousCacheHit) return false;
        if (!allHashesMatch) return false;

        if (prevCacheRead is null or 0)
        {
            return usage.CacheReadInputTokens == 0 && usage.CacheCreationInputTokens > 0;
        }

        var tokenDrop = prevCacheRead.Value - usage.CacheReadInputTokens;
        return usage.CacheReadInputTokens < prevCacheRead.Value * CacheEvictionRelativeThreshold
            && tokenDrop >= CacheEvictionAbsoluteThreshold;
    }

    private static bool IsModelChanged(PromptStateSnapshot snapshot, string? currentModelId)
    {
        if (snapshot.ModelId is null && currentModelId is null) return false;
        if (snapshot.ModelId is null || currentModelId is null) return true;
        return !string.Equals(snapshot.ModelId, currentModelId, StringComparison.Ordinal);
    }

    private static bool IsFastModeChanged(PromptStateSnapshot snapshot, bool? currentFastMode)
    {
        if (snapshot.FastMode is null && currentFastMode is null) return false;
        if (snapshot.FastMode is null || currentFastMode is null) return false;
        return snapshot.FastMode != currentFastMode;
    }

    private static bool IsExcludedModel(string? modelId)
        => modelId is not null && modelId.Contains("haiku", StringComparison.OrdinalIgnoreCase);

    private static (CacheBreakKind Kind, string Detail) ClassifyCacheMiss(TimeSpan? timeSinceLastCall)
    {
        if (timeSinceLastCall is null)
        {
            return (CacheBreakKind.CacheEviction, "Cache miss despite identical prefix — no previous call timestamp");
        }

        var gap = timeSinceLastCall.Value;
        if (gap > Ttl1Hour)
        {
            return (CacheBreakKind.TtlExpiration1Hour, "Cache miss — possible 1h TTL expiry (prompt unchanged)");
        }

        if (gap > Ttl5Min)
        {
            return (CacheBreakKind.TtlExpiration5Min, "Cache miss — possible 5min TTL expiry (prompt unchanged)");
        }

        return (CacheBreakKind.ServerSideRouting, "Cache miss — likely server-side routing/eviction (prompt unchanged, <5min gap)");
    }
}

// <!-- 🤖 Auto Decision: 2026-08-06 -->
// <!-- 决策: 为 CacheBreakDetector 增加"消息序列前缀"hash 检测 -->
// <!-- 原因: 原检测器只对 system/tools/dynamic 逻辑构件做 hash，从不核对线上实际对话消息序列。
//        若中途某条既有历史消息被篡改/插入，真实线上前缀已被破坏，但检测器误报"无失效" -->
// <!-- 实现: RecordPromptState/CheckCacheBreak 新增可选 conversation 参数；照 MockServer
//       ExtractConversationPrefix 的逐条编码（role\x01 content\x00）计算联合 hash。
//       只比对快照时已存在的前 N 条消息——尾部追加(多轮增长)/前缀变短(撤回)均不误报，
//       唯有既有前缀被篡改/插入才报 ConversationHistoryChanged -->
// <!-- 替代方案: 直接比对整段序列化字节(需 provider 特定的序列化器、开销大且耦合；弃用 -->
// <!-- 验证: 编译通过，243 个 PrefixCache 测试 + 11 个 CacheBreakMonitor 测试 + 6 个新测试全绿 ✅ -->

