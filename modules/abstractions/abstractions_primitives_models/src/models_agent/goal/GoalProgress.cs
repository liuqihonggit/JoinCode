namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// Goal 进度快照 — 供 GUI 显示节点级进度
/// </summary>
public sealed record GoalProgress {
    /// <summary>已完成节点数（含失败和跳过）</summary>
    public required int CompletedNodes { get; init; }
    /// <summary>总节点数</summary>
    public required int TotalNodes { get; init; }
    /// <summary>当前执行中节点名称（无则 null）</summary>
    public string? CurrentNodeName { get; init; }
    /// <summary>进度百分比文本（如 "2/5"）</summary>
    public string ProgressText => $"{CompletedNodes}/{TotalNodes}";
}
