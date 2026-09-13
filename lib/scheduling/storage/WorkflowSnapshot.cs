
namespace Core.Scheduling;

/// <summary>
/// Workflow 执行快照 — 用于断点续跑持久化
/// </summary>
public sealed partial class WorkflowSnapshot
{
    /// <summary>Workflow 唯一标识</summary>
    public required string WorkflowId { get; init; }

    /// <summary>各步骤执行状态（StepId → StepState），断点续跑只需状态不需完整结果</summary>
    public required Dictionary<string, StepState> StepStates { get; init; }

    /// <summary>跳过原因（StepId → 原因），依赖失败传播时记录</summary>
    public Dictionary<string, string> SkipReasons { get; init; } = new();

    /// <summary>最后更新时间</summary>
    public DateTimeOffset LastUpdated { get; init; }
}
