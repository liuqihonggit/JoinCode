namespace Core.Agents.Worktree;

/// <summary>
/// Worktree 创建管道共享上下文 — 在中间件各阶段间传递状态
/// </summary>
public sealed class WorktreeCreateContext : PipelineContextBase
{
    // === 输入 ===

    /// <summary>代理唯一标识</summary>
    public required string AgentId { get; init; }
    /// <summary>git 根目录（可选，未指定时自动查找）</summary>
    public string? GitRootPath { get; init; }
    /// <summary>worktree 创建选项</summary>
    public WorktreeOptions? Options { get; init; }
    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    // === WorktreeGitRootMiddleware 填充 ===

    /// <summary>解析后的 git 根目录</summary>
    public string GitRoot { get; set; } = string.Empty;
    /// <summary>原始工作目录</summary>
    public string OriginalCwd { get; set; } = string.Empty;

    // === WorktreeRecoveryMiddleware 填充 ===

    /// <summary>是否为恢复模式</summary>
    public bool IsRecovery { get; set; }
    /// <summary>恢复的 worktree 会话（恢复模式时填充）</summary>
    public AgentWorktreeSession? RecoveredSession { get; set; }

    // === WorktreeGitInfoMiddleware 填充 ===

    /// <summary>原始分支名称</summary>
    public string? OriginalBranch { get; set; }
    /// <summary>基准提交 SHA</summary>
    public string? BaseCommitSha { get; set; }
    /// <summary>基准分支名称</summary>
    public string? BaseBranch { get; set; }

    // === WorktreeCreateMiddleware 填充 ===

    /// <summary>worktree 路径</summary>
    public string WorktreePath { get; set; } = string.Empty;
    /// <summary>worktree 分支名称</summary>
    public string BranchName { get; set; } = string.Empty;
    /// <summary>创建开始时间</summary>
    public DateTime? CreationStartTime { get; set; }
    /// <summary>创建耗时（毫秒）</summary>
    public long? CreationDurationMs { get; set; }

    // === WorktreeSessionSaveMiddleware 填充 ===

    /// <summary>worktree 会话</summary>
    public AgentWorktreeSession? Session { get; set; }
    /// <summary>创建结果</summary>
    public WorktreeCreateResult? Result { get; set; }
}
