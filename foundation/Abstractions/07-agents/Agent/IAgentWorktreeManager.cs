
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Worktree 事件参数
/// </summary>
public sealed class WorktreeEventArgs : EventArgs
{
    /// <summary>
    /// 智能体 ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Worktree 路径
    /// </summary>
    public required string WorktreePath { get; init; }

    /// <summary>
    /// Git 分支名
    /// </summary>
    public string? BranchName { get; init; }
}

/// <summary>
/// Worktree 清理详情 — 对齐 TS cleanupWorktreeIfNeeded 返回值
/// </summary>
public sealed class WorktreeCleanupDetail
{
    /// <summary>
    /// Worktree 是否被保留（有变更或清理失败）
    /// </summary>
    public bool Kept { get; init; }

    /// <summary>
    /// Worktree 路径（保留时有值）
    /// </summary>
    public string? WorktreePath { get; init; }

    /// <summary>
    /// Worktree 分支名（保留时有值）
    /// </summary>
    public string? BranchName { get; init; }

    /// <summary>
    /// 保留原因: has_changes / hook-based / remove_failed / cleanup_error / no_session / not_isolated
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Worktree 已被删除（无变更时自动清理）
    /// </summary>
    public bool WasRemoved => !Kept && WorktreePath is null;

    /// <summary>
    /// 未启用隔离
    /// </summary>
    public static WorktreeCleanupDetail NotIsolated { get; } = new() { Reason = "not_isolated" };

    /// <summary>
    /// 无会话记录
    /// </summary>
    public static WorktreeCleanupDetail NoSession { get; } = new() { Reason = "no_session" };

    /// <summary>
    /// 已成功删除
    /// </summary>
    public static WorktreeCleanupDetail SuccessfullyRemoved { get; } = new();
}

/// <summary>
/// Agent Worktree 管理器接口 - 负责 Worktree 的创建和清理
/// </summary>
public interface IAgentWorktreeManager
{
    /// <summary>
    /// 为 Agent 创建 Worktree
    /// </summary>
    Task<bool> CreateWorktreeAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理 Agent 的 Worktree — 对齐 TS cleanupWorktreeIfNeeded
    /// 无变更时自动删除，有变更时保留 worktree 并返回路径/分支信息
    /// </summary>
    Task<WorktreeCleanupDetail> CleanupWorktreeAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取Agent的Worktree会话
    /// </summary>
    Task<AgentWorktreeSession?> GetWorktreeSessionAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有Worktree会话
    /// </summary>
    Task<IReadOnlyDictionary<string, AgentWorktreeSession>> GetAllWorktreeSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查是否启用了 Worktree 隔离
    /// </summary>
    bool IsWorktreeIsolationEnabled { get; }

    /// <summary>
    /// Worktree 创建完成事件
    /// </summary>
    event EventHandler<WorktreeEventArgs>? WorktreeCreated;

    /// <summary>
    /// Worktree 清理完成事件
    /// </summary>
    event EventHandler<WorktreeEventArgs>? WorktreeCleaned;
}
