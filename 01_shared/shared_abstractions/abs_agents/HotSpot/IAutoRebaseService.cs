namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 自动 rebase 同步服务 — SubagentStop 时系统自动执行 git fetch + rebase，取代软通知模式
/// <para>
/// 设计理由：软通知模式（"请 git pull --rebase"文本提示）有5个问题：
/// 1. LLM 可能忽略提示 2. 执行错误命令 3. 多 Worker 同时 rebase 冲突
/// 4. 无确定性保证 5. 归因偏差（LLM 认为 rebase 问题不是自己引入的）
/// 自动 rebase 模式优势：确定性执行 + 时机可控 + 冲突可检测 + 可追溯 + 减少归因偏差
/// </para>
/// </summary>
public interface IAutoRebaseService
{
    /// <summary>
    /// 自动 rebase 同步主干 — SubagentStop 时调用
    /// <para>流程：git fetch → 检测有无新提交 → (有则) stash 脏工作区 → git rebase → 冲突则 abort + 邮箱通知</para>
    /// </summary>
    /// <param name="request">rebase 同步请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>rebase 同步结果（含最终状态、冲突文件列表等）</returns>
    Task<AutoRebaseResult> RebaseSyncAsync(AutoRebaseRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// rebase 同步状态机状态 — 显式枚举驱动状态转换（参考 ADR 0018 状态机设计风格）
/// </summary>
public enum RebaseSyncState : byte
{
    /// <summary>初始空闲状态</summary>
    Idle,
    /// <summary>正在 fetch 远程</summary>
    Fetching,
    /// <summary>正在检查主干有无新提交</summary>
    CheckingUpstream,
    /// <summary>正在暂存未提交修改</summary>
    StashingDirty,
    /// <summary>正在 rebase</summary>
    Rebasing,
    /// <summary>检测到 rebase 冲突</summary>
    ConflictDetected,
    /// <summary>正在 abort rebase</summary>
    Aborting,
    /// <summary>rebase 完成（成功或冲突已 abort + 通知）</summary>
    Completed,
    /// <summary>跳过（主干无新提交）</summary>
    Skipped,
    /// <summary>失败（fetch/stash/abort 等错误）</summary>
    Failed,
}

/// <summary>
/// 自动 rebase 同步请求
/// </summary>
public sealed record AutoRebaseRequest
{
    /// <summary>worktree 路径（rebase 在此目录执行）</summary>
    public required string WorktreePath { get; init; }

    /// <summary>Worker Agent ID（用于邮箱通知发件人/收件人）</summary>
    public required string AgentId { get; init; }

    /// <summary>上游分支（默认 origin/main）</summary>
    public string UpstreamBranch { get; init; } = "origin/main";

    /// <summary>队长 Agent ID（用于邮箱通知收件人，null 则不通知）</summary>
    public string? CaptainId { get; init; }
}

/// <summary>
/// 自动 rebase 同步结果
/// </summary>
public sealed record AutoRebaseResult
{
    /// <summary>最终状态机状态</summary>
    public required RebaseSyncState FinalState { get; init; }

    /// <summary>是否成功（Completed 或 Skipped 都算成功）</summary>
    public required bool Success { get; init; }

    /// <summary>结果消息（错误信息或跳过原因）</summary>
    public string? Message { get; init; }

    /// <summary>冲突文件列表（HadConflicts=true 时填充）</summary>
    public IReadOnlyList<string> ConflictFiles { get; init; } = [];

    /// <summary>是否因无新提交而跳过</summary>
    public bool WasSkipped { get; init; }

    /// <summary>是否有冲突（冲突已 abort，worktree 回到 rebase 前状态）</summary>
    public bool HadConflicts { get; init; }
}
