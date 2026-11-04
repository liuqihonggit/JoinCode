namespace JoinCode.Abstractions.LLM.Chat;

public sealed class PromptStateSnapshot {
    /// <summary>获取系统提示词 hash。</summary>
    public required string SystemPromptHash { get; init; }
    /// <summary>获取工具规格联合 hash。</summary>
    public required string ToolSpecsHash { get; init; }
    /// <summary>获取工具数量。</summary>
    public required int ToolCount { get; init; }
    /// <summary>获取工具名称联合 hash。</summary>
    public required string ToolNamesHash { get; init; }
    /// <summary>获取动态内容 hash。</summary>
    public required string DynamicContentHash { get; init; }
    /// <summary>快照时对话消息序列的联合 hash，空串表示快照时无对话消息（跳过历史检测）</summary>
    public string ConversationHash { get; init; } = string.Empty;
    /// <summary>快照时对话消息数量（消息序列前缀的长度基准）</summary>
    public int ConversationCount { get; init; }
    /// <summary>获取工具规格快照列表。</summary>
    public IReadOnlyList<ToolSpec> ToolSpecs { get; init; } = [];
    /// <summary>获取模型标识。</summary>
    public string? ModelId { get; init; }
    /// <summary>获取是否快速模式。</summary>
    public bool? FastMode { get; init; }
}

public enum CacheBreakKind {
    [EnumValue("none")]
    None,
    [EnumValue("system_prompt_changed")]
    SystemPromptChanged,
    [EnumValue("tool_specs_changed")]
    ToolSpecsChanged,
    [EnumValue("dynamic_content_changed")]
    DynamicContentChanged,
    [EnumValue("cache_eviction")]
    CacheEviction,
    [EnumValue("model_changed")]
    ModelChanged,
    [EnumValue("fast_mode_changed")]
    FastModeChanged,
    /// <summary>对话消息序列中的既有前缀被篡改/插入（真实线上前缀已破坏）</summary>
    [EnumValue("conversation_history_changed")]
    ConversationHistoryChanged,
    /// <summary>上下文已被主动压缩/折叠，前缀被重写 —— 是本项目发起的缓存重建，非驱逐</summary>
    [EnumValue("compaction_entered")]
    CompactionEntered,
    /// <summary>缓存驱逐归因为 5min TTL 过期（gap &gt; 5min 且 ≤ 1h）</summary>
    [EnumValue("ttl_expiration_5min")]
    TtlExpiration5Min,
    /// <summary>缓存驱逐归因为 1h TTL 过期（gap &gt; 1h）</summary>
    [EnumValue("ttl_expiration_1hour")]
    TtlExpiration1Hour,
    /// <summary>缓存驱逐归因为服务端路由/驱逐（gap &lt; 5min，非客户端原因）</summary>
    [EnumValue("server_side_routing")]
    ServerSideRouting
}

public sealed class CacheBreakResult {
    /// <summary>获取是否检测到缓存断裂。</summary>
    public bool BreakDetected { get; init; }
    /// <summary>获取断裂类型。</summary>
    public CacheBreakKind Kind { get; init; }
    /// <summary>获取详细信息。</summary>
    public string? Detail { get; init; }
    /// <summary>获取工具漂移报告。</summary>
    public ToolDriftReport? ToolDrift { get; init; }

    /// <summary>构造无断裂结果。</summary>
    public static CacheBreakResult NoBreak() => new() { BreakDetected = false, Kind = CacheBreakKind.None };

    /// <summary>构造断裂结果。</summary>
    /// <param name="kind">断裂类型。</param>
    /// <param name="detail">详细信息。</param>
    /// <param name="toolDrift">工具漂移报告。</param>
    public static CacheBreakResult Break(CacheBreakKind kind, string detail, ToolDriftReport? toolDrift = null) => new() {
        BreakDetected = true,
        Kind = kind,
        Detail = detail,
        ToolDrift = toolDrift
    };
}