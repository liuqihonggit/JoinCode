namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 自举 worktree — Doctor 专属的 git worktree 隔离环境
/// </summary>
public sealed record BootstrapWorktree
{
    /// <summary>worktree 路径</summary>
    public required string WorktreePath { get; init; }

    /// <summary>worktree 分支名</summary>
    public required string BranchName { get; init; }

    /// <summary>基于的分支或 commit</summary>
    public required string BaseRef { get; init; }

    /// <summary>git 仓库根目录</summary>
    public required string GitRoot { get; init; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// worktree 提交结果
/// </summary>
public sealed record WorktreeCommitResult
{
    /// <summary>是否成功</summary>
    public required bool Success { get; init; }

    /// <summary>提交 hash</summary>
    public string? CommitHash { get; init; }

    /// <summary>变更 diff</summary>
    public string? Diff { get; init; }

    /// <summary>变更文件列表</summary>
    public IEnumerable<string> ChangedFiles { get; init; } = [];

    /// <summary>失败原因</summary>
    public string? FailureReason { get; init; }
}
