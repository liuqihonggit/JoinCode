namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 合并队列项 — Worker 完成任务后提交产出到合并队列（不直接push）
/// </summary>
public sealed record MergeQueueItem
{
    public required string WorkerId { get; init; }
    public required string WorktreeBranch { get; init; }
    public required string TaskId { get; init; }
    public required DateTimeOffset EnqueuedAt { get; init; }
}

/// <summary>
/// 合并结果 — 队长串行处理一个队列项的结果
/// </summary>
public sealed record MergeResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public required string? MergedBranch { get; init; }
    public required string? FailedWorkerId { get; init; }

    public static MergeResult Ok(string branch, string workerId) => new()
    {
        Success = true,
        Message = $"合并成功: {branch}",
        MergedBranch = branch,
        FailedWorkerId = null
    };

    public static MergeResult CompileFailed(string workerId, string reason) => new()
    {
        Success = false,
        Message = $"编译校验失败: {reason}",
        MergedBranch = null,
        FailedWorkerId = workerId
    };

    public static MergeResult MergeFailed(string workerId, string reason) => new()
    {
        Success = false,
        Message = $"合并失败: {reason}",
        MergedBranch = null,
        FailedWorkerId = workerId
    };

    public static MergeResult Empty() => new()
    {
        Success = true,
        Message = "队列为空",
        MergedBranch = null,
        FailedWorkerId = null
    };
}
