
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Lead 合并编排器 — 监控 Worker 完成状态，按依赖顺序合并，每步测试
/// </summary>
public interface ILeadMergeOrchestrator
{
    Task<LeadMergeResult> MergeCompletedWorkersAsync(LeadMergeContext context, CancellationToken ct = default);
}

/// <summary>
/// Lead 合并上下文
/// </summary>
public sealed class LeadMergeContext
{
    public required string SessionId { get; init; }
    public required ClusterPlan Plan { get; init; }
    public required IReadOnlyList<WorkerCompletion> CompletedWorkers { get; init; }
    public required string MainBranch { get; init; }
    public required string WorkingDirectory { get; init; }
}

/// <summary>
/// Worker 完成信息
/// </summary>
public sealed class WorkerCompletion
{
    public required string SubTaskId { get; init; }
    public required string AgentId { get; init; }
    public required string WorktreeBranch { get; init; }
    public required string WorktreePath { get; init; }
    public bool IsSuccess { get; init; }
    public double GradingScore { get; init; }
    public string? Output { get; init; }
}

/// <summary>
/// Lead 合并结果
/// </summary>
public sealed class LeadMergeResult
{
    public bool Success { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<MergeStepResult> Steps { get; init; } = [];

    public static LeadMergeResult Succeeded(IReadOnlyList<MergeStepResult> steps) =>
        new() { Success = true, Reason = "全部合并成功", Steps = steps };

    public static LeadMergeResult PartiallySucceeded(IReadOnlyList<MergeStepResult> steps, string reason) =>
        new() { Success = true, Reason = reason, Steps = steps };

    public static LeadMergeResult Failed(string reason, IReadOnlyList<MergeStepResult> steps) =>
        new() { Success = false, Reason = reason, Steps = steps };
}

/// <summary>
/// 单步合并结果
/// </summary>
public sealed class MergeStepResult
{
    public required string SubTaskId { get; init; }
    public bool Merged { get; init; }
    public bool TestsPassed { get; init; }
    public string? MergeStrategy { get; init; }
    public string? Message { get; init; }
}
