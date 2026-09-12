namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 派发决策 — 队长派发前检查热点表，决定任务由队长自己揽还是派给Worker
/// </summary>
public sealed record DispatchDecision
{
    /// <summary>
    /// 队长是否应自己处理（热点文件契约改不派给Worker）
    /// </summary>
    public required bool ShouldCaptainHandle { get; init; }

    /// <summary>
    /// 决策原因
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// 触发热点的文件列表（队长需自己揽的文件）
    /// </summary>
    public required IReadOnlyList<string> HotSpotFiles { get; init; }
}
