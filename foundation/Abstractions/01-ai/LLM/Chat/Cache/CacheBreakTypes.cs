namespace JoinCode.Abstractions.LLM.Chat;

public sealed class PromptStateSnapshot
{
    public required string SystemPromptHash { get; init; }
    public required string ToolSpecsHash { get; init; }
    public required int ToolCount { get; init; }
    public required string ToolNamesHash { get; init; }
    public required string DynamicContentHash { get; init; }
    /// <summary>快照时对话消息序列的联合 hash，空串表示快照时无对话消息（跳过历史检测）</summary>
    public string ConversationHash { get; init; } = string.Empty;
    /// <summary>快照时对话消息数量（消息序列前缀的长度基准）</summary>
    public int ConversationCount { get; init; }
    public IReadOnlyList<ToolSpec> ToolSpecs { get; init; } = [];
    public string? ModelId { get; init; }
    public bool? FastMode { get; init; }
}

public enum CacheBreakKind
{
    None,
    SystemPromptChanged,
    ToolSpecsChanged,
    DynamicContentChanged,
    CacheEviction,
    ModelChanged,
    FastModeChanged,
    /// <summary>对话消息序列中的既有前缀被篡改/插入（真实线上前缀已破坏）</summary>
    ConversationHistoryChanged
}

public sealed class CacheBreakResult
{
    public bool BreakDetected { get; init; }
    public CacheBreakKind Kind { get; init; }
    public string? Detail { get; init; }
    public ToolDriftReport? ToolDrift { get; init; }

    public static CacheBreakResult NoBreak() => new() { BreakDetected = false, Kind = CacheBreakKind.None };

    public static CacheBreakResult Break(CacheBreakKind kind, string detail, ToolDriftReport? toolDrift = null) => new()
    {
        BreakDetected = true,
        Kind = kind,
        Detail = detail,
        ToolDrift = toolDrift
    };
}
