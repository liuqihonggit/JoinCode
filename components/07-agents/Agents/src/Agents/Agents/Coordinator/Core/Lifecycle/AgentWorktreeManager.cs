namespace Core.Agents.Coordinator;

/// <summary>
/// Agent Worktree 管理器 - 负责 Worktree 的创建和清理
/// 对齐 TS: worktree isolation — 创建/清理时触发 WorktreeCreate/WorktreeRemove hook
/// </summary>
[Register]
public sealed partial class AgentWorktreeManager : IAgentWorktreeManager
{
    private readonly IAgentWorktreeService? _worktreeService;
    private readonly IHookOrchestrator? _hookOrchestrator;
    private readonly ILogger? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, AgentWorktreeSession> _worktreeSessions;
    private readonly bool _enableWorktreeIsolation;

    public event EventHandler<WorktreeEventArgs>? WorktreeCreated;
    public event EventHandler<WorktreeEventArgs>? WorktreeCleaned;

    public AgentWorktreeManager(
        IAgentWorktreeService? worktreeService = null,
        IHookOrchestrator? hookOrchestrator = null,
        ILogger? logger = null,
        bool enableWorktreeIsolation = false,
        IClockService? clock = null)
    {
        _worktreeService = worktreeService;
        _hookOrchestrator = hookOrchestrator;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _enableWorktreeIsolation = enableWorktreeIsolation && worktreeService != null;
        _worktreeSessions = new ConcurrentDictionary<string, AgentWorktreeSession>();
    }

    /// <summary>
    /// 为 Agent 创建 Worktree
    /// </summary>
    public async Task<bool> CreateWorktreeAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (!_enableWorktreeIsolation || _worktreeService == null)
        {
            return false;
        }

        try
        {
            if (_hookOrchestrator is not null)
            {
                var hookPath = await TryCreateWorktreeViaHookAsync(agentId, cancellationToken).ConfigureAwait(false);
                if (hookPath is not null)
                {
                    var session = new AgentWorktreeSession
                    {
                        AgentId = agentId,
                        OriginalCwd = Environment.CurrentDirectory,
                        WorktreePath = hookPath,
                        BranchName = $"hook-{agentId}",
                        GitRootPath = hookPath,
                        CreatedAt = _clock.GetUtcNow(),
                        Existed = false,
                        HookBased = true
                    };
                    _worktreeSessions[agentId] = session;
                    _logger?.LogInformation("Hook-based worktree created for agent {AgentId} at: {Path}", agentId, hookPath);
                    FireWorktreeCreated(agentId, hookPath, session.BranchName);
                    return true;
                }
            }

            var worktreeResult = await _worktreeService.CreateAgentWorktreeAsync(agentId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (worktreeResult.Success && worktreeResult.Session != null)
            {
                _worktreeSessions[agentId] = worktreeResult.Session;
                _logger?.LogInformation(
                AgentCoordinatorConstants.LogMessages.CreateWorktree,
                AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId, worktreeResult.Session.WorktreePath);

                FireWorktreeCreated(agentId, worktreeResult.Session.WorktreePath, worktreeResult.Session.BranchName);
                return true;
            }
            else
            {
                _logger?.LogWarning(
                AgentCoordinatorConstants.LogMessages.CreateWorktreeFailed,
                AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId, worktreeResult.ErrorMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, AgentCoordinatorConstants.LogMessages.CreateWorktreeError, AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId);
            return false;
        }
    }

    private async Task<string?> TryCreateWorktreeViaHookAsync(string agentId, CancellationToken cancellationToken)
    {
        var hookOrchestrator = _hookOrchestrator ?? throw new InvalidOperationException("Hook orchestrator not available.");
        try
        {
            var payload = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["agent_id"] = System.Text.Json.JsonSerializer.SerializeToElement(agentId, AgentsJsonContext.Default.String),
                ["action"] = System.Text.Json.JsonSerializer.SerializeToElement("create", AgentsJsonContext.Default.String)
            };

            await foreach (var result in hookOrchestrator.ExecuteHooksAsync(
                HookEvent.WorktreeCreate, payload, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (result.Outcome == HookOutcome.Success && result.Message is not null)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(result.Message);
                    if (doc.RootElement.TryGetProperty("worktree_path", out var pathElem))
                    {
                        var path = pathElem.GetString();
                        if (!string.IsNullOrEmpty(path)) return path;
                    }
                    if (doc.RootElement.TryGetProperty("worktreePath", out var pathElem2))
                    {
                        var path = pathElem2.GetString();
                        if (!string.IsNullOrEmpty(path)) return path;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "WorktreeCreate hook execution failed for agent {AgentId}", agentId);
        }

        return null;
    }

    /// <summary>
    /// 清理 Agent 的 Worktree — 对齐 TS cleanupWorktreeIfNeeded
    /// 无变更时自动删除，有变更时保留 worktree 并返回路径/分支信息
    /// HookBased worktree 始终保留（无法检测 VCS 变更）
    /// </summary>
    public async Task<WorktreeCleanupDetail> CleanupWorktreeAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (!_enableWorktreeIsolation || _worktreeService == null)
        {
            return WorktreeCleanupDetail.NotIsolated;
        }

        if (!_worktreeSessions.TryRemove(agentId, out var removedSession))
        {
            return WorktreeCleanupDetail.NoSession;
        }

        try
        {
            if (removedSession.HookBased)
            {
                _logger?.LogInformation("Hook-based agent worktree kept at: {WorktreePath}", removedSession.WorktreePath);
                FireWorktreeCleaned(agentId, removedSession.WorktreePath, removedSession.BranchName);
                return new WorktreeCleanupDetail
                {
                    Kept = true,
                    WorktreePath = removedSession.WorktreePath,
                    BranchName = removedSession.BranchName,
                    Reason = "hook-based"
                };
            }

            var hasChanges = await HasWorktreeChangesAsync(removedSession, cancellationToken).ConfigureAwait(false);

            if (!hasChanges)
            {
                var cleanupResult = await _worktreeService.RemoveAgentWorktreeAsync(agentId, force: true, cancellationToken).ConfigureAwait(false);
                if (cleanupResult.Success)
                {
                    _logger?.LogInformation(AgentCoordinatorConstants.LogMessages.CleanupWorktree, AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId);
                    FireWorktreeCleaned(agentId, removedSession.WorktreePath, removedSession.BranchName);
                    return WorktreeCleanupDetail.SuccessfullyRemoved;
                }

                _logger?.LogWarning("Failed to remove unchanged worktree for agent {AgentId}: {Error}", agentId, cleanupResult.ErrorMessage);
                return new WorktreeCleanupDetail
                {
                    Kept = true,
                    WorktreePath = removedSession.WorktreePath,
                    BranchName = removedSession.BranchName,
                    Reason = "remove_failed"
                };
            }

            _logger?.LogInformation("Agent worktree has changes, keeping: {WorktreePath}", removedSession.WorktreePath);
            FireWorktreeCleaned(agentId, removedSession.WorktreePath, removedSession.BranchName);
            return new WorktreeCleanupDetail
            {
                Kept = true,
                WorktreePath = removedSession.WorktreePath,
                BranchName = removedSession.BranchName,
                Reason = "has_changes"
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, AgentCoordinatorConstants.LogMessages.CleanupWorktreeError, AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId);
            return new WorktreeCleanupDetail
            {
                Kept = true,
                WorktreePath = removedSession.WorktreePath,
                BranchName = removedSession.BranchName,
                Reason = "cleanup_error"
            };
        }
    }

    /// <summary>
    /// 检查 worktree 是否有变更 — 对齐 TS hasWorktreeChanges
    /// 比较当前 HEAD 与 baseCommitSha，同时检查未提交更改
    /// </summary>
    private async Task<bool> HasWorktreeChangesAsync(AgentWorktreeSession session, CancellationToken cancellationToken)
    {
        var worktreeService = _worktreeService ?? throw new InvalidOperationException("Worktree service not available.");
        if (!string.IsNullOrEmpty(session.BaseCommitSha))
        {
            var hasUnpushed = await worktreeService.HasUnpushedCommitsAsync(
                session.WorktreePath, session.BaseCommitSha, cancellationToken).ConfigureAwait(false);
            if (hasUnpushed)
            {
                return true;
            }
        }

        var hasUncommitted = await worktreeService.HasUncommittedChangesAsync(
            session.WorktreePath, cancellationToken).ConfigureAwait(false);
        return hasUncommitted;
    }

    /// <summary>
    /// 获取Agent的Worktree会话
    /// </summary>
    public Task<AgentWorktreeSession?> GetWorktreeSessionAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_worktreeSessions.GetValueOrDefault(agentId));
    }

    /// <summary>
    /// 获取所有Worktree会话
    /// </summary>
    public Task<IReadOnlyDictionary<string, AgentWorktreeSession>> GetAllWorktreeSessionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, AgentWorktreeSession>>(
            _worktreeSessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    /// <summary>
    /// 检查是否启用了 Worktree 隔离
    /// </summary>
    public bool IsWorktreeIsolationEnabled => _enableWorktreeIsolation;

    private void FireWorktreeCreated(string agentId, string worktreePath, string branchName)
    {
        try
        {
            WorktreeCreated?.Invoke(this, new WorktreeEventArgs
            {
                AgentId = agentId,
                WorktreePath = worktreePath,
                BranchName = branchName
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to fire WorktreeCreated event for {AgentId}", agentId);
        }

        FireWorktreeHookAsync(HookEvent.WorktreeCreate, agentId, worktreePath, branchName);
    }

    private void FireWorktreeCleaned(string agentId, string worktreePath, string branchName)
    {
        try
        {
            WorktreeCleaned?.Invoke(this, new WorktreeEventArgs
            {
                AgentId = agentId,
                WorktreePath = worktreePath,
                BranchName = branchName
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to fire WorktreeCleaned event for {AgentId}", agentId);
        }

        FireWorktreeHookAsync(HookEvent.WorktreeRemove, agentId, worktreePath, branchName);
    }

    /// <summary>
    /// 触发 WorktreeCreate/WorktreeRemove hook — 对齐 TS executeWorktreeCreateHook/executeWorktreeRemoveHook
    /// </summary>
    private void FireWorktreeHookAsync(HookEvent hookEvent, string agentId, string worktreePath, string branchName)
    {
        if (_hookOrchestrator is null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["agent_id"] = System.Text.Json.JsonSerializer.SerializeToElement(agentId, AgentsJsonContext.Default.String),
                    ["worktree_path"] = System.Text.Json.JsonSerializer.SerializeToElement(worktreePath, AgentsJsonContext.Default.String),
                    ["branch_name"] = System.Text.Json.JsonSerializer.SerializeToElement(branchName, AgentsJsonContext.Default.String)
                };

                await foreach (var _ in _hookOrchestrator.ExecuteHooksAsync(hookEvent, payload).ConfigureAwait(false))
                {
                    // 消费所有结果，但不阻塞
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to execute {HookEvent} hook for agent {AgentId}", hookEvent.ToValue(), agentId);
            }
        });
    }
}
