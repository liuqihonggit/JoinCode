namespace Core.Agents.Coordinator;

/// <summary>
/// Agent Worktree 管理器 - 负责 Worktree 的创建和清理
/// 对齐 TS: worktree isolation — 创建/清理时触发 WorktreeCreate/WorktreeRemove hook
/// </summary>
[Register(typeof(IAgentWorktreeManager), ServiceLifetime.Singleton)]
public sealed partial class AgentWorktreeManager : ServiceEntity, IAgentWorktreeManager
{
    private readonly IAgentWorktreeService? _worktreeService;
    private readonly IHookOrchestrator? _hookOrchestrator;
    private readonly ILogger? _logger;
    private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, AgentWorktreeSession> _worktreeSessions;
    private readonly ConcurrentDictionary<string, WorktreeLifecycleGuard> _lifecycleGuards;
    private readonly bool _enableWorktreeIsolation;
    private readonly IFileOperationService? _fileOperationService;
    private readonly IGitCommandRunner? _gitRunner;

    public event EventHandler<WorktreeEventArgs>? WorktreeCreated;
    public event EventHandler<WorktreeEventArgs>? WorktreeCleaned;

    public AgentWorktreeManager(
        IAgentWorktreeService? worktreeService = null,
        IHookOrchestrator? hookOrchestrator = null,
        ILogger? logger = null,
        bool enableWorktreeIsolation = false,
        IClockService? clock = null,
        IFileOperationService? fileOperationService = null,
        IGitCommandRunner? gitRunner = null)
    {
        _worktreeService = worktreeService;
        _hookOrchestrator = hookOrchestrator;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _enableWorktreeIsolation = enableWorktreeIsolation && worktreeService != null;
        _worktreeSessions = new ConcurrentDictionary<string, AgentWorktreeSession>();
        _lifecycleGuards = new ConcurrentDictionary<string, WorktreeLifecycleGuard>();
        _fileOperationService = fileOperationService;
        _gitRunner = gitRunner;
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
                RegisterLifecycleGuard(agentId, worktreeResult.Session.WorktreePath, worktreeResult.Session.GitRootPath, worktreeResult.Session.BranchName);
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

    /// <summary>
    /// per-agent Worktree 创建 — 不依赖全局隔离开关，显式请求时直接创建。
    /// 存储 session + 注册 guard，确保清理路径能找到 worktree。
    /// </summary>
    public async Task<AgentWorktreeSession?> CreateWorktreeForAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (_worktreeService is null)
        {
            return null;
        }

        try
        {
            var worktreeResult = await _worktreeService.CreateAgentWorktreeAsync(agentId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (worktreeResult.Success && worktreeResult.Session is not null)
            {
                _worktreeSessions[agentId] = worktreeResult.Session;
                RegisterLifecycleGuard(agentId, worktreeResult.Session.WorktreePath, worktreeResult.Session.GitRootPath, worktreeResult.Session.BranchName);
                _logger?.LogInformation(
                    AgentCoordinatorConstants.LogMessages.CreateWorktree,
                    AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId, worktreeResult.Session.WorktreePath);
                FireWorktreeCreated(agentId, worktreeResult.Session.WorktreePath, worktreeResult.Session.BranchName);
                return worktreeResult.Session;
            }

            _logger?.LogWarning(
                AgentCoordinatorConstants.LogMessages.CreateWorktreeFailed,
                AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId, worktreeResult.ErrorMessage);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, AgentCoordinatorConstants.LogMessages.CreateWorktreeError, AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId);
            return null;
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
        if (_worktreeService == null)
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
                var removed = await RemoveWorktreeViaGuardAsync(agentId, cancellationToken).ConfigureAwait(false);
                if (removed)
                {
                    _logger?.LogInformation(AgentCoordinatorConstants.LogMessages.CleanupWorktree, AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId);
                    FireWorktreeCleaned(agentId, removedSession.WorktreePath, removedSession.BranchName);
                    return WorktreeCleanupDetail.SuccessfullyRemoved;
                }

                _logger?.LogWarning("Failed to remove unchanged worktree for agent {AgentId}", agentId);
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

    /// <summary>
    /// 为 worktree 注册生命周期守卫 — 构造时锁定路径，Dispose 时用同一路径删除，从不二次计算。
    /// </summary>
    private void RegisterLifecycleGuard(string agentId, string worktreePath, string mainPath, string? branchName)
    {
        if (_fileOperationService is null) return;
        try
        {
            var guard = new WorktreeLifecycleGuard(worktreePath, mainPath, _fileOperationService, _gitRunner, branchName, _logger);
            _lifecycleGuards[agentId] = guard;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "创建 WorktreeLifecycleGuard 失败: {AgentId}", agentId);
        }
    }

    /// <summary>
    /// 通过 guard 释放 worktree — 优先用构造时锁定的路径，guard 不存在时回退到 worktreeService。
    /// </summary>
    private async Task<bool> RemoveWorktreeViaGuardAsync(string agentId, CancellationToken cancellationToken)
    {
        if (_lifecycleGuards.TryRemove(agentId, out var guard))
        {
            var result = await guard.ReleaseAsync(force: true, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                _logger?.LogWarning("Guard 释放 worktree 失败: {Reason} {Error}", result.Reason, result.ErrorMessage);
            }
            return result.Success;
        }

        if (_worktreeService is not null)
        {
            var cleanupResult = await _worktreeService.RemoveAgentWorktreeAsync(agentId, force: true, cancellationToken).ConfigureAwait(false);
            if (!cleanupResult.Success)
            {
                _logger?.LogWarning("WorktreeService 移除失败: {Error}", cleanupResult.ErrorMessage);
            }
            return cleanupResult.Success;
        }

        return false;
    }

    /// <summary>
    /// 异步释放 — 遍历 guard 字典逐个 DisposeAsync，用构造时锁定的路径删除，从不二次计算。
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        foreach (var kvp in _lifecycleGuards)
        {
            try
            {
                await kvp.Value.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { _logger?.LogDebug(ex, "DisposeAsync guard 清理 worktree {AgentId} 失败", kvp.Key); }
        }
        Dispose();
    }

    protected override void OnDispose()
    {
        _worktreeSessions.Clear();
        _lifecycleGuards.Clear();
    }
}
