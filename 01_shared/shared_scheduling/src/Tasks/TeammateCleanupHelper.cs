namespace Core.Scheduling.Tasks;

/// <summary>
/// Teammate 清理助手 — 封装 teammate 资源清理逻辑（worktree/agent/mailbox/polling）
/// 从 InProcessTeammateTaskExecutor 提取，主类 Actor Consumer 调用此助手执行清理
/// </summary>
internal sealed class TeammateCleanupHelper
{
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    private readonly IMailbox _messageBroker;
    private readonly IMailboxPoller? _mailboxPoller;
    private readonly IAgentWorktreeManager? _worktreeManager;
    private readonly ILogger? _logger;

    public TeammateCleanupHelper(
        IAgentLifecycleManager agentLifecycleManager,
        IMailbox messageBroker,
        IMailboxPoller? mailboxPoller,
        IAgentWorktreeManager? worktreeManager,
        ILogger? logger)
    {
        _agentLifecycleManager = agentLifecycleManager;
        _messageBroker = messageBroker;
        _mailboxPoller = mailboxPoller;
        _worktreeManager = worktreeManager;
        _logger = logger;
    }

    /// <summary>
    /// 清理 teammate 资源 — mailbox polling + broker 注销 + worktree + agent + lifecycleCts
    /// 注意：_pendingMessages.TryRemove 由 Actor Consumer 执行，不在此处
    /// </summary>
    public async Task CleanupTeammateAsync(string teammateId, TeammateState state)
    {
        StopMailboxPollingIfNeeded(teammateId);
        _messageBroker.UnregisterAgent(teammateId);
        await CleanupWorktreeSafelyAsync(teammateId, state).ConfigureAwait(false);
        await DisposeAgentSafelyAsync(teammateId, state).ConfigureAwait(false);
        state.LifecycleCts.Dispose();
    }

    /// <summary>
    /// 安全清理 worktree — 有变更保留并记录 reason,无变更移除,失败仅警告
    /// </summary>
    private async Task CleanupWorktreeSafelyAsync(string teammateId, TeammateState state)
    {
        if (_worktreeManager is null) return;
        try
        {
            var cleanupDetail = await _worktreeManager.CleanupWorktreeAsync(state.Agent.ObjectId.UniqueId, CancellationToken.None).ConfigureAwait(false);
            if (cleanupDetail.Kept)
            {
                _logger?.LogInformation("Teammate {TeammateId} worktree kept: {Path} (reason: {Reason})",
                    teammateId, cleanupDetail.WorktreePath, cleanupDetail.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Teammate {TeammateId} worktree cleanup failed", teammateId);
        }
    }

    /// <summary>
    /// 安全释放 Agent 资源 — 失败仅警告
    /// </summary>
    private async Task DisposeAgentSafelyAsync(string teammateId, TeammateState state)
    {
        try
        {
            await _agentLifecycleManager.DisposeAgentAsync(state.Agent.ObjectId.UniqueId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "清理 Teammate {TeammateId} 的 Agent 资源失败", teammateId);
        }
    }

    public void StopMailboxPollingIfNeeded(string teammateId)
    {
        if (_mailboxPoller == null) return;
        var sessionId = _messageBroker.GetSessionId(teammateId);
        if (sessionId is null) return;
        try
        {
            _mailboxPoller.StopPolling(teammateId, sessionId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to stop mailbox polling for teammate {TeammateId}", teammateId);
        }
    }

    public void StartMailboxPollingIfNeeded(string teammateId)
    {
        if (_mailboxPoller == null) return;
        var sessionId = _messageBroker.GetSessionId(teammateId);
        if (sessionId is null) return;
        try
        {
            _mailboxPoller.StartPolling(teammateId, sessionId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to start mailbox polling for teammate {TeammateId}", teammateId);
        }
    }
}
