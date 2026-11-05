namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 合并队列项 — Worker 完成任务后提交产出到合并队列（不直接push）
/// </summary>
public sealed record MergeQueueItem {
    /// <summary>获取工作器标识。</summary>
    public required string WorkerId { get; init; }
    /// <summary>获取工作树分支。</summary>
    public required string WorktreeBranch { get; init; }
    /// <summary>获取任务标识。</summary>
    public required string TaskId { get; init; }
    /// <summary>获取入队时间。</summary>
    public required DateTimeOffset EnqueuedAt { get; init; }
}

/// <summary>
/// 合并结果 — 队长串行处理一个队列项的结果
/// </summary>
public sealed record MergeResult {
    /// <summary>获取是否成功。</summary>
    public required bool Success { get; init; }
    /// <summary>获取结果消息。</summary>
    public required string Message { get; init; }
    /// <summary>获取合并后的分支名。</summary>
    public required string? MergedBranch { get; init; }
    /// <summary>获取失败的工作器标识。</summary>
    public required string? FailedWorkerId { get; init; }

    /// <summary>创建合并成功结果。</summary>
    public static MergeResult Ok(string branch, string workerId) => new() {
        Success = true,
        Message = $"合并成功: {branch}",
        MergedBranch = branch,
        FailedWorkerId = null
    };

    /// <summary>创建编译失败结果。</summary>
    public static MergeResult CompileFailed(string workerId, string reason) => new() {
        Success = false,
        Message = $"编译校验失败: {reason}",
        MergedBranch = null,
        FailedWorkerId = workerId
    };

    /// <summary>创建合并失败结果。</summary>
    public static MergeResult MergeFailed(string workerId, string reason) => new() {
        Success = false,
        Message = $"合并失败: {reason}",
        MergedBranch = null,
        FailedWorkerId = workerId
    };

    /// <summary>创建队列为空结果。</summary>
    public static MergeResult Empty() => new() {
        Success = true,
        Message = "队列为空",
        MergedBranch = null,
        FailedWorkerId = null
    };
}