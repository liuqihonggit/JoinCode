namespace Core.Agents.Coordinator;

/// <summary>
/// Agent Worktree 管理器 - 负责 Worktree 的创建和清理
/// 对齐 TS: worktree isolation — 创建/清理时触发 WorktreeCreate/WorktreeRemove hook
/// </summary>
[Register(typeof(IAgentWorktreeManager), ServiceLifetime.Singleton)]
public sealed partial class AgentWorktreeManager : ServiceEntity, IAgentWorktreeManager {
    private readonly IAgentWorktreeService? _worktreeService;
    private readonly IHookOrchestrator? _hookOrchestrator;
    private readonly ILogger? _logger;
    private readonly IClockService _clock;
    private static readonly ConcurrentDictionary<string, AgentWorktreeSession> s_worktreeSessions = new();
    private readonly ConcurrentDictionary<string, WorktreeLifecycleGuard> _lifecycleGuards;
    private readonly bool _enableWorktreeIsolation;
    private readonly IFileOperationService? _fileOperationService;
    private readonly IGitCommandRunner? _gitRunner;
    private readonly IFileSystem? _fileSystem;

    /// <summary>Worktree 创建完成事件，参数携带 Agent ID、Worktree 路径与分支名</summary>
    public event EventHandler<WorktreeEventArgs>? WorktreeCreated;
    /// <summary>Worktree 清理完成事件，参数携带 Agent ID、Worktree 路径与分支名</summary>
    public event EventHandler<WorktreeEventArgs>? WorktreeCleaned;

    /// <summary>
    /// 构造 Agent Worktree 管理器实例
    /// </summary>
    /// <param name="worktreeService">可选 Worktree 服务，缺省时不启用隔离</param>
    /// <param name="hookOrchestrator">可选 Hook 编排器，用于触发 WorktreeCreate/WorktreeRemove 事件</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="enableWorktreeIsolation">是否启用全局 Worktree 隔离</param>
    /// <param name="clock">可选时钟服务，缺省时使用系统时钟</param>
    /// <param name="fileOperationService">可选文件操作服务，用于构造生命周期守卫</param>
    /// <param name="gitRunner">可选 Git 命令执行器，用于守卫内的分支清理</param>
    /// <param name="workflowConfig">可选工作流配置，从中读取 EnableWorktreeIsolation 全局开关</param>
    /// <param name="fileSystem">可选文件系统抽象，用于跨进程清理时定位 git 工作区根目录</param>
    public AgentWorktreeManager(
        IAgentWorktreeService? worktreeService = null,
        IHookOrchestrator? hookOrchestrator = null,
        ILogger? logger = null,
        bool enableWorktreeIsolation = false,
        IClockService? clock = null,
        IFileOperationService? fileOperationService = null,
        IGitCommandRunner? gitRunner = null,
        WorkflowConfig? workflowConfig = null,
        IFileSystem? fileSystem = null) {
        _worktreeService = worktreeService;
        _hookOrchestrator = hookOrchestrator;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        var configEnabled = workflowConfig?.EnableWorktreeIsolation ?? enableWorktreeIsolation;
        _enableWorktreeIsolation = configEnabled && worktreeService != null;
        _lifecycleGuards = new ConcurrentDictionary<string, WorktreeLifecycleGuard>();
        _fileOperationService = fileOperationService;
        _gitRunner = gitRunner;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// 为 Agent 创建 Worktree
    /// </summary>
    public async Task<bool> CreateWorktreeAsync(string agentId, CancellationToken cancellationToken = default) {
        if (!_enableWorktreeIsolation || _worktreeService == null) {
            return false;
        }

        try {
            if (_hookOrchestrator is not null) {
                var hookPath = await TryCreateWorktreeViaHookAsync(agentId, cancellationToken).ConfigureAwait(false);
                if (hookPath is not null) {
                    var session = new AgentWorktreeSession {
                        AgentId = agentId,
                        OriginalCwd = Environment.CurrentDirectory,
                        WorktreePath = hookPath,
                        BranchName = $"hook-{agentId}",
                        GitRootPath = hookPath,
                        CreatedAt = _clock.GetUtcNow(),
                        Existed = false,
                        HookBased = true
                    };
                    s_worktreeSessions[agentId] = session;
                    _logger?.LogInformation("Hook-based worktree created for agent {AgentId} at: {Path}", agentId, hookPath);
                    FireWorktreeCreated(agentId, hookPath, session.BranchName);
                    return true;
                }
            }

            var worktreeResult = await _worktreeService.CreateAgentWorktreeAsync(agentId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (worktreeResult.Success && worktreeResult.Session != null) {
                s_worktreeSessions[agentId] = worktreeResult.Session;
                RegisterLifecycleGuard(agentId, worktreeResult.Session.WorktreePath, worktreeResult.Session.GitRootPath, worktreeResult.Session.BranchName);
                _logger?.LogInformation(
                AgentCoordinatorConstants.LogMessages.CreateWorktree,
                AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId, worktreeResult.Session.WorktreePath);

                FireWorktreeCreated(agentId, worktreeResult.Session.WorktreePath, worktreeResult.Session.BranchName);
                return true;
            } else {
                _logger?.LogWarning(
                AgentCoordinatorConstants.LogMessages.CreateWorktreeFailed,
                AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId, worktreeResult.ErrorMessage);
                return false;
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, AgentCoordinatorConstants.LogMessages.CreateWorktreeError, AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId);
            return false;
        }
    }

    /// <summary>
    /// per-agent Worktree 创建 — 不依赖全局隔离开关，显式请求时直接创建。
    /// 存储 session + 注册 guard，确保清理路径能找到 worktree。
    /// </summary>
    public async Task<AgentWorktreeSession?> CreateWorktreeForAgentAsync(string agentId, CancellationToken cancellationToken = default) {
        if (_worktreeService is null) {
            return null;
        }

        try {
            var worktreeResult = await _worktreeService.CreateAgentWorktreeAsync(agentId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (worktreeResult.Success && worktreeResult.Session is not null) {
                s_worktreeSessions[agentId] = worktreeResult.Session;
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
        } catch (Exception ex) {
            _logger?.LogError(ex, AgentCoordinatorConstants.LogMessages.CreateWorktreeError, AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId);
            return null;
        }
    }

    private async Task<string?> TryCreateWorktreeViaHookAsync(string agentId, CancellationToken cancellationToken) {
        var hookOrchestrator = _hookOrchestrator ?? throw new InvalidOperationException("Hook orchestrator not available.");
        try {
            var payload = new Dictionary<string, System.Text.Json.JsonElement> {
                ["agent_id"] = System.Text.Json.JsonSerializer.SerializeToElement(agentId, AgentsJsonContext.Default.String),
                ["action"] = System.Text.Json.JsonSerializer.SerializeToElement("create", AgentsJsonContext.Default.String)
            };

            await foreach (var result in hookOrchestrator.ExecuteHooksAsync(
                HookEvent.WorktreeCreate, payload, cancellationToken: cancellationToken).ConfigureAwait(false)) {
                if (result.Outcome == HookOutcome.Success && result.Message is not null) {
                    using var doc = System.Text.Json.JsonDocument.Parse(result.Message);
                    if (doc.RootElement.TryGetProperty("worktree_path", out var pathElem)) {
                        var path = pathElem.GetString();
                        if (!string.IsNullOrEmpty(path)) return path;
                    }
                    if (doc.RootElement.TryGetProperty("worktreePath", out var pathElem2)) {
                        var path = pathElem2.GetString();
                        if (!string.IsNullOrEmpty(path)) return path;
                    }
                }
            }
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "WorktreeCreate hook execution failed for agent {AgentId}", agentId);
        }

        return null;
    }

    /// <summary>
    /// Worktree 清理状态机 — 所有差异在本函数体内。
    /// 状态流: 定位 session → hook-based 判断 → 模式决策(变更检查) → 执行删除/保留。
    /// OnTaskComplete: 无变更删除,有变更保留(任务自然完成路径)。
    /// ForceRemove: 强制删除(worktree_remove 工具)。
    /// agent_stop 不调用此方法(保留 worktree 供检查)。
    /// </summary>
    public async Task<WorktreeCleanupDetail> CleanupWorktreeAsync(
        string agentId,
        WorktreeCleanupMode mode = WorktreeCleanupMode.OnTaskComplete,
        CancellationToken cancellationToken = default) {
        if (_worktreeService == null) {
            return WorktreeCleanupDetail.NotIsolated;
        }

        // 状态1: 定位 session(内存优先,跨进程从磁盘重建)
        s_worktreeSessions.TryRemove(agentId, out var session);
        session ??= await ReconstructSessionFromDiskAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (session is null) {
            return WorktreeCleanupDetail.NoSession;
        }

        try {
            // 状态2: hook-based 总是保留
            if (session.HookBased) {
                return KeptDetail(session, "hook-based");
            }

            // 状态3: 模式驱动决策 — 所有差异在此 switch
            var shouldRemove = mode switch {
                WorktreeCleanupMode.ForceRemove => true,
                WorktreeCleanupMode.OnTaskComplete => !await HasWorktreeChangesAsync(session, cancellationToken).ConfigureAwait(false),
                _ => false
            };

            if (!shouldRemove) {
                return KeptDetail(session, "has_changes");
            }

            // 状态4: 执行删除(唯一删除路径)
            var removed = await RemoveWorktreeViaGuardAsync(agentId, session, cancellationToken).ConfigureAwait(false);
            if (!removed) {
                return KeptDetail(session, "remove_failed");
            }

            _logger?.LogInformation("worktree 已移除: {WorktreePath}", session.WorktreePath);
            FireWorktreeCleaned(agentId, session.WorktreePath, session.BranchName ?? string.Empty);
            return WorktreeCleanupDetail.SuccessfullyRemoved;
        } catch (Exception ex) {
            _logger?.LogError(ex, AgentCoordinatorConstants.LogMessages.CleanupWorktreeError, AgentCoordinatorConstants.LogMessages.AgentWorktreeManagerPrefix, agentId);
            return KeptDetail(session, "cleanup_error");
        }
    }

    /// <summary>ForceRemove 模式便捷包装</summary>
    public Task<WorktreeCleanupDetail> ForceRemoveWorktreeAsync(string agentId, CancellationToken cancellationToken = default)
        => CleanupWorktreeAsync(agentId, WorktreeCleanupMode.ForceRemove, cancellationToken);

    private static WorktreeCleanupDetail KeptDetail(AgentWorktreeSession session, string reason) => new() {
        Kept = true,
        WorktreePath = session.WorktreePath,
        BranchName = session.BranchName,
        Reason = reason
    };

    /// <summary>
    /// 跨进程重建 session — 当 mcp_call 启动新进程时,内存 s_worktreeSessions 不共享。
    /// 用确定性路径(agentId → GenerateWorktreePath + GenerateBranchName)从磁盘反查 worktree,
    /// 若路径存在则构造 synthetic session 继续清理;不存在则返回 null。
    /// </summary>
    private async Task<AgentWorktreeSession?> ReconstructSessionFromDiskAsync(string agentId, CancellationToken cancellationToken) {
        if (_fileSystem is null) {
            return null;
        }

        var gitRoot = GitWorkspaceResolver.FindGitWorkspaceDir(null, _fileSystem);
        if (string.IsNullOrEmpty(gitRoot)) {
            return null;
        }

        var worktreePath = AgentWorktreeSession.GenerateWorktreePath(gitRoot, agentId);
        if (!_fileSystem.DirectoryExists(worktreePath)) {
            return null;
        }

        var branchName = AgentWorktreeSession.GenerateBranchName(agentId);
        _logger?.LogInformation("跨进程重建 worktree session: agent={AgentId}, path={Path}, branch={Branch}", agentId, worktreePath, branchName);

        return new AgentWorktreeSession {
            AgentId = agentId,
            OriginalCwd = Environment.CurrentDirectory,
            WorktreePath = worktreePath,
            BranchName = branchName,
            GitRootPath = gitRoot,
            CreatedAt = _clock.GetUtcNow(),
            Existed = true,
            HookBased = false
        };
    }

    /// <summary>
    /// 检查 worktree 是否有变更 — 对齐 TS hasWorktreeChanges
    /// 比较当前 HEAD 与 baseCommitSha，同时检查未提交更改
    /// </summary>
    private async Task<bool> HasWorktreeChangesAsync(AgentWorktreeSession session, CancellationToken cancellationToken) {
        var worktreeService = _worktreeService ?? throw new InvalidOperationException("Worktree service not available.");
        if (!string.IsNullOrEmpty(session.BaseCommitSha)) {
            var hasUnpushed = await worktreeService.HasUnpushedCommitsAsync(
                session.WorktreePath, session.BaseCommitSha, cancellationToken).ConfigureAwait(false);
            if (hasUnpushed) {
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
    public Task<AgentWorktreeSession?> GetWorktreeSessionAsync(string agentId, CancellationToken cancellationToken = default) {
        return Task.FromResult(s_worktreeSessions.GetValueOrDefault(agentId));
    }

    /// <summary>
    /// 获取所有Worktree会话
    /// </summary>
    public Task<IReadOnlyDictionary<string, AgentWorktreeSession>> GetAllWorktreeSessionsAsync(CancellationToken cancellationToken = default) {
        return Task.FromResult<IReadOnlyDictionary<string, AgentWorktreeSession>>(
            s_worktreeSessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    /// <summary>
    /// 检查是否启用了 Worktree 隔离
    /// </summary>
    public bool IsWorktreeIsolationEnabled => _enableWorktreeIsolation;

    private void FireWorktreeCreated(string agentId, string worktreePath, string branchName) {
        try {
            WorktreeCreated?.Invoke(this, new WorktreeEventArgs {
                AgentId = agentId,
                WorktreePath = worktreePath,
                BranchName = branchName
            });
        } catch (Exception ex) {
            _logger?.LogError(ex, "Failed to fire WorktreeCreated event for {AgentId}", agentId);
        }

        FireWorktreeHookAsync(HookEvent.WorktreeCreate, agentId, worktreePath, branchName);
    }

    private void FireWorktreeCleaned(string agentId, string worktreePath, string branchName) {
        try {
            WorktreeCleaned?.Invoke(this, new WorktreeEventArgs {
                AgentId = agentId,
                WorktreePath = worktreePath,
                BranchName = branchName
            });
        } catch (Exception ex) {
            _logger?.LogError(ex, "Failed to fire WorktreeCleaned event for {AgentId}", agentId);
        }

        FireWorktreeHookAsync(HookEvent.WorktreeRemove, agentId, worktreePath, branchName);
    }

    /// <summary>
    /// 触发 WorktreeCreate/WorktreeRemove hook — 对齐 TS executeWorktreeCreateHook/executeWorktreeRemoveHook
    /// </summary>
    private void FireWorktreeHookAsync(HookEvent hookEvent, string agentId, string worktreePath, string branchName) {
        if (_hookOrchestrator is null) return;

        _ = Task.Run(async () => {
            try {
                var payload = new Dictionary<string, System.Text.Json.JsonElement> {
                    ["agent_id"] = System.Text.Json.JsonSerializer.SerializeToElement(agentId, AgentsJsonContext.Default.String),
                    ["worktree_path"] = System.Text.Json.JsonSerializer.SerializeToElement(worktreePath, AgentsJsonContext.Default.String),
                    ["branch_name"] = System.Text.Json.JsonSerializer.SerializeToElement(branchName, AgentsJsonContext.Default.String)
                };

                await foreach (var _ in _hookOrchestrator.ExecuteHooksAsync(hookEvent, payload).ConfigureAwait(false)) {
                    // 消费所有结果，但不阻塞
                }
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "Failed to execute {HookEvent} hook for agent {AgentId}", hookEvent.ToValue(), agentId);
            }
        });
    }

    /// <summary>
    /// 为 worktree 注册生命周期守卫 — 构造时锁定路径，Dispose 时用同一路径删除，从不二次计算。
    /// </summary>
    private void RegisterLifecycleGuard(string agentId, string worktreePath, string mainPath, string? branchName) {
        if (_fileOperationService is null) return;
        try {
            var guard = new WorktreeLifecycleGuard(worktreePath, mainPath, _fileOperationService, _gitRunner, branchName, _logger);
            _lifecycleGuards[agentId] = guard;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "创建 WorktreeLifecycleGuard 失败: {AgentId}", agentId);
        }
    }

    /// <summary>
    /// 通过 guard 释放 worktree — 优先用构造时锁定的路径，guard 不存在时回退到 worktreeService，
    /// service 也失败时(跨进程 session 不共享)用 _gitRunner 直接执行 git worktree remove + branch -D。
    /// </summary>
    private async Task<bool> RemoveWorktreeViaGuardAsync(string agentId, AgentWorktreeSession session, CancellationToken cancellationToken) {
        if (_lifecycleGuards.TryRemove(agentId, out var guard)) {
            var result = await guard.ReleaseAsync(force: true, cancellationToken).ConfigureAwait(false);
            if (!result.Success) {
                _logger?.LogWarning("Guard 释放 worktree 失败: {Reason} {Error}", result.Reason, result.ErrorMessage);
            }
            return result.Success;
        }

        if (_worktreeService is not null) {
            var cleanupResult = await _worktreeService.RemoveAgentWorktreeAsync(agentId, force: true, cancellationToken).ConfigureAwait(false);
            if (cleanupResult.Success) {
                return true;
            }
            _logger?.LogWarning("WorktreeService 移除失败: {Error}", cleanupResult.ErrorMessage);
        }

        return await RemoveWorktreeViaGitRunnerAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 用 _gitRunner 直接执行 git worktree remove --force + git branch -D — 跨进程清理的最终兜底。
    /// </summary>
    private async Task<bool> RemoveWorktreeViaGitRunnerAsync(AgentWorktreeSession session, CancellationToken cancellationToken) {
        if (_gitRunner is null) {
            return false;
        }

        var removeResult = await _gitRunner.ExecuteAsync(
            $"worktree remove --force \"{session.WorktreePath}\"",
            session.GitRootPath,
            cancellationToken).ConfigureAwait(false);

        if (!removeResult.Success) {
            _logger?.LogWarning("git worktree remove 失败: {Error}", removeResult.Error);
            return false;
        }

        await _gitRunner.ExecuteAsync(
            $"branch -D {session.BranchName}",
            session.GitRootPath,
            cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("跨进程直接清理 worktree 成功: {Path}", session.WorktreePath);
        return true;
    }

    /// <summary>
    /// 异步释放 — 仅清空 guard 字典,不自动删除 worktree。
    /// worktree 生命周期由显式 worktree_remove 指令控制,不由进程退出自动清理。
    /// </summary>
    public override ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>释放资源 — 仅清理实例级 lifecycleGuards,不清空 static s_worktreeSessions(跨实例共享)</summary>
    public override void Dispose() {
        _lifecycleGuards.Clear();
        base.Dispose();
    }
}