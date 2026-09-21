namespace JoinCode.Abstractions.LLM.Chat;

public sealed class RewindResult {
    /// <summary>获取一个值，指示撤回是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>获取已移除的消息数。</summary>
    public int RemovedCount { get; init; }
    /// <summary>获取剩余的消息数。</summary>
    public int RemainingCount { get; init; }
    /// <summary>获取撤回类型。</summary>
    public RewindKind Kind { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>创建撤回成功结果。</summary>
    public static RewindResult Ok(RewindKind kind, int removed, int remaining) => new() {
        Success = true,
        Kind = kind,
        RemovedCount = removed,
        RemainingCount = remaining
    };

    /// <summary>创建撤回失败结果。</summary>
    public static RewindResult Fail(string message) => new() {
        Success = false,
        ErrorMessage = message
    };
}

public enum RewindKind {
    [EnumValue("trim_last_turn")]
    TrimLastTurn,
    [EnumValue("truncate_to_index")]
    TruncateToIndex,
    [EnumValue("clear_history")]
    ClearHistory
}