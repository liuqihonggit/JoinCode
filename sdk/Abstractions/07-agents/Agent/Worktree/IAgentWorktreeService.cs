
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 智能体 Worktree 服务接口，管理智能体的 Git Worktree 隔离
/// </summary>
public interface IAgentWorktreeService
{
    /// <summary>
    /// 为智能体创建或恢复 Git Worktree
    /// </summary>
    /// <param name="agentId">智能体 ID</param>
    /// <param name="gitRootPath">Git 仓库根目录（可选，默认自动检测）</param>
    /// <param name="options">Worktree 选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建结果</returns>
    Task<WorktreeCreateResult> CreateAgentWorktreeAsync(
        string agentId,
        string? gitRootPath = null,
        WorktreeOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除智能体的 Worktree
    /// </summary>
    /// <param name="agentId">智能体 ID</param>
    /// <param name="force">是否强制移除（即使有未提交更改）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理结果</returns>
    Task<WorktreeCleanupResult> RemoveAgentWorktreeAsync(
        string agentId,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取智能体的 Worktree 会话
    /// </summary>
    /// <param name="agentId">智能体 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>会话信息，如果不存在则返回 null</returns>
    Task<AgentWorktreeSession?> GetSessionAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查智能体是否有活动的 Worktree
    /// </summary>
    /// <param name="agentId">智能体 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否有活动 worktree</returns>
    Task<bool> HasActiveWorktreeAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有活动的 Worktree 会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>会话列表</returns>
    Task<IReadOnlyList<AgentWorktreeSession>> GetAllSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理过期的 Worktree
    /// </summary>
    /// <param name="options">Worktree 选项（用于判断过期）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的 worktree 数量</returns>
    Task<int> CleanupStaleWorktreesAsync(
        WorktreeOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查 Worktree 是否有未提交的更改
    /// </summary>
    /// <param name="worktreePath">Worktree 路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否有未提交更改</returns>
    Task<bool> HasUncommittedChangesAsync(
        string worktreePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查 Worktree 是否有未推送的提交
    /// </summary>
    /// <param name="worktreePath">Worktree 路径</param>
    /// <param name="baseCommitSha">基础提交 SHA</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否有未推送提交</returns>
    Task<bool> HasUnpushedCommitsAsync(
        string worktreePath,
        string? baseCommitSha = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找 Git 仓库根目录
    /// </summary>
    /// <param name="startPath">起始路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Git 根目录，如果不是 git 仓库则返回 null</returns>
    Task<string?> FindGitRootAsync(string startPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出所有 Worktree
    /// </summary>
    /// <param name="gitRootPath">Git 根目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Worktree 路径列表</returns>
    Task<IReadOnlyList<string>> ListWorktreesAsync(
        string? gitRootPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保留 Worktree 但清除会话 — 对齐 TS keepWorktree
    /// Worktree 目录和分支保留，但会话从内存和持久化中移除
    /// </summary>
    /// <param name="agentId">智能体 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task KeepWorktreeAsync(string agentId, CancellationToken cancellationToken = default);
}
