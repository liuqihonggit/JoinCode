namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 释放管道上下文 — 承载释放过程中各中间件共享的状态与结果
/// </summary>
public sealed class AgentDisposeContext : PipelineContextBase
{
    /// <summary>目标 Agent 标识（必填）</summary>
    public required string AgentId { get; init; }
    /// <summary>取消令牌，用于协作式取消释放流程</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>Worktree 清理结果，由清理中间件写入</summary>
    public WorktreeCleanupDetail? WorktreeCleanupResult { get; set; }
    /// <summary>已取消的 Shell 任务数量，由 Shell 任务清理中间件写入</summary>
    public int CancelledShellTaskCount { get; set; }
    /// <summary>是否已释放生命周期管理器，由生命周期释放中间件写入</summary>
    public bool LifecycleDisposed { get; set; }
    /// <summary>是否已移除执行上下文，由执行上下文清理中间件写入</summary>
    public bool ExecutionContextRemoved { get; set; }
    /// <summary>是否已移除启动时间记录，由启动时间清理中间件写入</summary>
    public bool StartTimeRemoved { get; set; }
}
