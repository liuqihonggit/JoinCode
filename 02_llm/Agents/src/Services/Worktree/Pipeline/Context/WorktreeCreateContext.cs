namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 创建管道共享上下文 — 在中间件各阶段间传递状态
/// </summary>
public sealed class WorktreeCreateContext : PipelineContextBase
{
    // === 输入 ===

    public required string AgentId { get; init; }
    public string? GitRootPath { get; init; }
    public WorktreeOptions? Options { get; init; }
    public CancellationToken CancellationToken { get; init; }

    // === WorktreeGitRootMiddleware 填充 ===

    public string GitRoot { get; set; } = string.Empty;
    public string OriginalCwd { get; set; } = string.Empty;

    // === WorktreeRecoveryMiddleware 填充 ===

    public bool IsRecovery { get; set; }
    public AgentWorktreeSession? RecoveredSession { get; set; }

    // === WorktreeGitInfoMiddleware 填充 ===

    public string? OriginalBranch { get; set; }
    public string? BaseCommitSha { get; set; }
    public string? BaseBranch { get; set; }

    // === WorktreeCreateMiddleware 填充 ===

    public string WorktreePath { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public DateTime? CreationStartTime { get; set; }
    public long? CreationDurationMs { get; set; }

    // === WorktreeSessionSaveMiddleware 填充 ===

    public AgentWorktreeSession? Session { get; set; }
    public WorktreeCreateResult? Result { get; set; }
}
