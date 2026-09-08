namespace Core.Scheduling.Tasks;

public interface IInProcessTeammateTaskExecutor
{
    Task<AgentTaskResult> ExecuteTeammateAsync(InProcessTeammateDefinition definition, CancellationToken ct = default);
    Task<bool> SendMessageToTeammateAsync(string teammateId, CoordinatorMessage message, CancellationToken ct = default);
    Task<IEnumerable<string>> GetActiveTeammatesAsync(CancellationToken ct = default);
    Task<IEnumerable<TeammateStateSnapshot>> GetActiveTeammateSnapshotsAsync(CancellationToken ct = default);
    Task StopTeammateAsync(string teammateId, CancellationToken ct = default);
    Task TerminateTeammateAsync(string teammateId, string? reason = null, CancellationToken ct = default);
    Task<bool> IsTeammateIdleAsync(string teammateId, CancellationToken ct = default);
    Task<bool> InterruptTeammateAsync(string teammateId, CancellationToken ct = default);
    event EventHandler<TeammateCompletedEventArgs>? TeammateCompleted;
}

/// <summary>
/// teammate 状态快照 — 供 GUI 渲染子会话树（含 ParentSessionId/Task/IsIdle 等，弥补 GetActiveTeammatesAsync 只返回 ID 的不足）。
/// </summary>
public sealed record TeammateStateSnapshot(
    string TeammateId,
    string? ParentSessionId,
    string Task,
    bool IsIdle,
    int TurnCount,
    string? LastResult);

/// <summary>
/// teammate 完成事件参数 — teammate 正常完成或被终止时触发，供 GUI 移除子会话卡片/补足结果。
/// </summary>
public sealed class TeammateCompletedEventArgs : EventArgs
{
    public required string TeammateId { get; init; }
    public required string Task { get; init; }
    public string? Output { get; init; }
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public int TurnCount { get; init; }
}

public sealed partial class InProcessTeammateDefinition
{
    public required string TaskId { get; init; }
    public required string TeammateId { get; init; }
    public required string Task { get; init; }
    public string? SystemPrompt { get; init; }
    public string? AgentType { get; init; }
    public AgentRole Role { get; init; } = AgentRole.Executor;
    public ExecutorVariant? Variant { get; init; }
    public string? AdditionalInstructions { get; init; }
    public int MaxIterations { get; init; } = 50;
    public List<string> InitialContext { get; init; } = [];
    public string? TeamName { get; init; }
    public string? TeamId { get; init; }
    public string? ParentSessionId { get; init; }
    public string? Color { get; init; }
    public bool PlanModeRequired { get; init; }
    public bool ContinuousMode { get; init; }
    /// <summary>隔离模式 — Worktree 时 teammate 在独立工作树中执行（供 mainAgent 接手分析 diff）</summary>
    public AgentIsolationMode IsolationMode { get; init; } = AgentIsolationMode.None;
}

public sealed class TeammateState
{
    public required IAgent Agent { get; init; }
    public required CancellationTokenSource LifecycleCts { get; init; }
    public required TeammateContext Context { get; init; }
    public bool IsIdle { get; set; }
    public string? LastResult { get; set; }
    public int TurnCount { get; set; }
    /// <summary>
    /// 任务描述 — 来自 <see cref="InProcessTeammateDefinition.Task"/>，供 snapshot 暴露给 GUI 渲染子会话标题。
    /// </summary>
    public string Task { get; init; } = string.Empty;
    /// <summary>
    /// 当前 per-turn work 的 CTS — Interrupt 时只 cancel 此 CTS 中断当前 work，不杀 lifecycle。
    /// 由循环体在 work 开始前设置、结束后清空；InterruptTeammateAsync 读取并 cancel。
    /// 受 <see cref="InProcessTeammateTaskExecutor._teammateLock"/> 保护。
    /// </summary>
    public CancellationTokenSource? CurrentWorkCts { get; set; }
}

[Register(typeof(IInProcessTeammateTaskExecutor), ServiceLifetime.Singleton)]
public sealed partial class InProcessTeammateTaskExecutor : ServiceEntity, IInProcessTeammateTaskExecutor
{
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    private readonly IMailbox _messageBroker;
    private readonly ILogger<InProcessTeammateTaskExecutor>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly IMailboxPoller? _mailboxPoller;
    private readonly IPlanModeManager? _planModeManager;
    private readonly ConcurrentDictionary<string, TeammateState> _activeTeammates = new();
    private readonly ConcurrentDictionary<string, Channel<CoordinatorMessage>> _pendingMessages = new();
    private readonly AsyncLock _teammateLock = new();
    private readonly MiddlewarePipeline<TeammateExecutionContext>? _executePipeline;
    private readonly IAgentWorktreeService? _worktreeService;
    private readonly IAgentWorktreeManager? _worktreeManager;

    public event EventHandler<TeammateCompletedEventArgs>? TeammateCompleted;

    public InProcessTeammateTaskExecutor(
        IAgentLifecycleManager agentLifecycleManager,
        IMailbox messageBroker,
        ILogger<InProcessTeammateTaskExecutor>? logger = null,
        ILoggerFactory? loggerFactory = null,
        ITelemetryService? telemetryService = null,
        IMailboxPoller? mailboxPoller = null,
        IPlanModeManager? planModeManager = null,
        IEnumerable<ITeammateExecutionMiddleware>? executeMiddlewares = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null,
        IAgentWorktreeService? worktreeService = null,
        IAgentWorktreeManager? worktreeManager = null)
    {
        _agentLifecycleManager = agentLifecycleManager;
        _messageBroker = messageBroker;
        _logger = logger;
        _telemetryService = telemetryService;
        _mailboxPoller = mailboxPoller;
        _planModeManager = planModeManager;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
        _worktreeService = worktreeService;
        _worktreeManager = worktreeManager;

        if (executeMiddlewares is not null && loggerFactory is not null)
        {
            _executePipeline = new PipelineBuilder<TeammateExecutionContext>()
                .WithLoggingScope(loggerFactory)
                .UseRange(executeMiddlewares)
                .Build();
        }
        else if (executeMiddlewares is not null)
        {
            _executePipeline = new MiddlewarePipeline<TeammateExecutionContext>(executeMiddlewares);
        }
    }

    public async Task<AgentTaskResult> ExecuteTeammateAsync(InProcessTeammateDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (_executePipeline is not null)
        {
            return await ExecuteTeammateViaPipelineAsync(definition, ct).ConfigureAwait(false);
        }

        return await ExecuteTeammateDirectAsync(definition, ct).ConfigureAwait(false);
    }

    private async Task<AgentTaskResult> ExecuteTeammateViaPipelineAsync(InProcessTeammateDefinition definition, CancellationToken ct)
    {
        var ctx = new TeammateExecutionContext
        {
            Definition = definition,
            CancellationToken = ct,
            RunLoopAsync = RunTeammateLoopAsync,
            TryCleanupAsync = TryCleanupTeammateAsync,
            CleanupAsync = (teammateId, state) => CleanupTeammateAsync(teammateId, state),
            ActiveTeammates = _activeTeammates,
            PendingMessages = _pendingMessages,
            TeammateLock = _teammateLock,
        };

        var pipeline = _executePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, ct).ConfigureAwait(false);
        }

        return ctx.Result ?? AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, "Pipeline produced no result", 0);
    }

    private async Task<AgentTaskResult> ExecuteTeammateDirectAsync(InProcessTeammateDefinition definition, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();

        try
        {
            _logger?.LogInformation(L.T(StringKey.InProcessTeammateStartLog),
                definition.TeammateId, definition.Task, definition.ContinuousMode);

            await using var scope = new TeammateDirectScope(this, definition, ct);
            await scope.InitAsync().ConfigureAwait(false);
            var state = scope.State;

            if (definition.ContinuousMode)
            {
                RunTeammateLoopBackground(definition, state, scope.LifecycleToken);
                scope.Detach();

                var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
                return AgentTaskResult.Success(definition.TaskId, definition.TeammateId, "Teammate started in continuous mode", elapsed);
            }

            if (definition.PlanModeRequired && _planModeManager != null && !_planModeManager.IsInPlanMode)
            {
                await TryEnterPlanModeIfNeededAsync(definition, ct).ConfigureAwait(false);
            }

            var result = await _agentLifecycleManager.ExecuteAsync(state.Agent, ct).ConfigureAwait(false);
            var elapsed2 = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;

            RecordTeammateMetrics("execute", result.IsSuccess);
            return result.IsSuccess
                ? AgentTaskResult.Success(definition.TaskId, definition.TeammateId, result.Output ?? string.Empty, elapsed2)
                : AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, result.Error ?? "Teammate execution failed", elapsed2);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.InProcessTeammateFailedLog, definition.TeammateId));
            RecordTeammateMetrics("execute", false);
            return AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, ex.Message, elapsed);
        }
    }

    public async Task<bool> SendMessageToTeammateAsync(string teammateId, CoordinatorMessage message, CancellationToken ct = default)
    {
        if (_pendingMessages.TryGetValue(teammateId, out var channel))
        {
            await channel.Writer.WriteAsync(message, ct).ConfigureAwait(false);
        }

        return await _messageBroker.SendAsync(teammateId, message, ct).ConfigureAwait(false);
    }

    public async Task<IEnumerable<string>> GetActiveTeammatesAsync(CancellationToken ct = default)
    {
        using var guard = await _teammateLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_teammateLock.Name}' 等待超时");

        return _activeTeammates.Keys;
    
    }

    /// <summary>
    /// 返回所有活跃 teammate 的状态快照 — 供 GUI 渲染子会话树（含 ParentSessionId/Task/IsIdle 等）。
    /// </summary>
    public async Task<IEnumerable<TeammateStateSnapshot>> GetActiveTeammateSnapshotsAsync(CancellationToken ct = default)
    {
        using var guard = await _teammateLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_teammateLock.Name}' 等待超时");

        return _activeTeammates.Select(kv => new TeammateStateSnapshot(
            kv.Key,
            kv.Value.Context.ParentSessionId,
            kv.Value.Task,
            kv.Value.IsIdle,
            kv.Value.TurnCount,
            kv.Value.LastResult)).ToList();
    
    }

    public async Task StopTeammateAsync(string teammateId, CancellationToken ct = default)
    {
        using var guard = await _teammateLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_teammateLock.Name}' 等待超时");

        if (_activeTeammates.TryRemove(teammateId, out var state))
        {
            await state.LifecycleCts.CancelAsync().ConfigureAwait(false);
            await CleanupTeammateAsync(teammateId, state).ConfigureAwait(false);
        }
    
    }

    public async Task TerminateTeammateAsync(string teammateId, string? reason = null, CancellationToken ct = default)
    {
        TeammateState? state;

        using var guard = await _teammateLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_teammateLock.Name}' 等待超时");

        if (!_activeTeammates.TryGetValue(teammateId, out state))
        {
            return;
        }
    

        var shutdownMsg = new CoordinatorMessage
        {
            FromAgentId = "coordinator",
            ToAgentId = teammateId,
            MessageType = TeammateMessageType.ShutdownRequest.ToValue(),
            Content = reason ?? "Teammate shutdown requested"
        };

        await SendMessageToTeammateAsync(teammateId, shutdownMsg, ct).ConfigureAwait(false);

        _logger?.LogInformation("Shutdown request sent to Teammate {TeammateId}: {Reason}", teammateId, reason);
    }

    public async Task<bool> IsTeammateIdleAsync(string teammateId, CancellationToken ct = default)
    {
        using var guard = await _teammateLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_teammateLock.Name}' 等待超时");

        return _activeTeammates.TryGetValue(teammateId, out var state) && state.IsIdle;
    
    }

    /// <summary>
    /// 中断 teammate 当前 per-turn work — 只 cancel <see cref="TeammateState.CurrentWorkCts"/>，
    /// 不 cancel lifecycle，teammate 进 idle 等待 next prompt（对齐 TS 原版 inProcessRunner ESC 行为）。
    /// 若 teammate 不存在或当前无活跃 work，返回 false。
    /// </summary>
    public async Task<bool> InterruptTeammateAsync(string teammateId, CancellationToken ct = default)
    {
        using var guard = await _teammateLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_teammateLock.Name}' 等待超时");
        CancellationTokenSource? workCts;
        if (!_activeTeammates.TryGetValue(teammateId, out var state))
        {
            return false;
        }
        workCts = state.CurrentWorkCts;

        if (workCts is null || workCts.IsCancellationRequested)
        {
            return false;
        }

        await workCts.CancelAsync().ConfigureAwait(false);
        _logger?.LogInformation("Teammate {TeammateId} 当前 work 已中断（interrupt），进入 idle 等待 next prompt", teammateId);
        return true;
    }

    /// <summary>
    /// 循环体调用 — 将当前 per-turn workCts 暴露到 state，供 InterruptTeammateAsync 读取并 cancel。
    /// </summary>
    private async Task SetCurrentWorkCtsAsync(string teammateId, CancellationTokenSource workCts, CancellationToken lifecycleCt)
    {
        using var guard = await _teammateLock.TryLockAsync(lifecycleCt).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_teammateLock.Name}' 等待超时");
        if (_activeTeammates.TryGetValue(teammateId, out var state))
        {
            state.CurrentWorkCts = workCts;
        }
    }

    /// <summary>
    /// 循环体 finally 调用 — 清空 state.CurrentWorkCts，避免 Interrupt 取到已 dispose 的旧 cts。
    /// 用 CancellationToken.None 保证清理不被取消。
    /// </summary>
    private async Task ClearCurrentWorkCtsAsync(string teammateId)
    {
        using var guard = await _teammateLock.TryLockAsync(CancellationToken.None).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_teammateLock.Name}' 等待超时");

        if (_activeTeammates.TryGetValue(teammateId, out var state))
        {
            state.CurrentWorkCts = null;
        }
    
    }

    /// <summary>
    /// 后台启动 teammate 循环 — 观察未处理异常，避免静默死亡；退出时通知 coordinator
    /// </summary>
    private void RunTeammateLoopBackground(InProcessTeammateDefinition definition, TeammateState state, CancellationToken lifecycleCt)
    {
        _ = Task.Run(SafeRunLoopAsync);

        async Task SafeRunLoopAsync()
        {
            try
            {
                await RunTeammateLoopAsync(definition, state, lifecycleCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifecycleCt.IsCancellationRequested)
            {
                // 主动停止 — 预期行为
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Teammate {TeammateId} 后台循环异常退出", definition.TeammateId);
                await NotifyIdleAsync(definition.TeammateId, state, $"后台循环异常: {ex.Message}").ConfigureAwait(false);
                await TryCleanupTeammateAsync(definition.TeammateId).ConfigureAwait(false);
                    }
                }
            }

    private async Task RunTeammateLoopAsync(
        InProcessTeammateDefinition definition,
        TeammateState state,
        CancellationToken lifecycleCt)
    {
        var subAgentContext = new SubAgentContext
        {
            AgentId = state.Context.AgentId,
            Role = AgentRole.Executor,
            Variant = ExecutorVariant.Teammate,
            Task = definition.Task,
            ParentAgentId = _subAgentContextAccessor.Current?.AgentId,
            SessionId = definition.ParentSessionId ?? _subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId,
            TeamId = state.Context.TeamId,
            SubagentName = state.Context.AgentName,
            IsBuiltIn = true,
            DisplayName = state.Context.AgentName
        };

        var completedNormally = false;

        using (state.Context.EnterScope())
        using (subAgentContext.EnterScopeWithCwd(null))
        {
            var shouldExit = false;

            if (definition.PlanModeRequired && _planModeManager != null && !_planModeManager.IsInPlanMode)
            {
                await TryEnterPlanModeIfNeededAsync(definition, lifecycleCt).ConfigureAwait(false);
            }

            while (!lifecycleCt.IsCancellationRequested && !shouldExit)
            {
                await using (var work = new TeammateWorkScope(this, definition.TeammateId, lifecycleCt))
                {
                    await work.EnterAsync(lifecycleCt).ConfigureAwait(false);
                    (shouldExit, completedNormally) = await ExecuteSingleTurnAsync(work, definition, state, lifecycleCt, completedNormally).ConfigureAwait(false);
                }
            }
        }

        await TryCleanupTeammateAsync(definition.TeammateId).ConfigureAwait(false);

        _logger?.LogInformation("Teammate {TeammateId} loop exited after {TurnCount} turns",
            definition.TeammateId, state.TurnCount);

        InvokeTeammateCompletedSafely(definition, state, completedNormally);
    }

    /// <summary>
    /// 执行单轮 teammate work — 正常完成/Interrupt/异常,返回 (ShouldExit, CompletedNormally)
    /// </summary>
    private async Task<(bool ShouldExit, bool CompletedNormally)> ExecuteSingleTurnAsync(
        TeammateWorkScope work, InProcessTeammateDefinition definition, TeammateState state,
        CancellationToken lifecycleCt, bool completedNormally)
    {
        try
        {
            var result = await _agentLifecycleManager.ExecuteAsync(state.Agent, work.Token).ConfigureAwait(false);

            state.TurnCount++;
            state.LastResult = result.Output;
            RecordTeammateMetrics("turn_complete", result.IsSuccess);

            _logger?.LogDebug("Teammate {TeammateId} checkpoint: turn={TurnCount} success={Success} outputLen={OutputLen}",
                definition.TeammateId, state.TurnCount, result.IsSuccess, result.Output?.Length ?? 0);

            completedNormally = true;
            return (true, completedNormally);
        }
        catch (OperationCanceledException) when (lifecycleCt.IsCancellationRequested)
        {
            return (true, completedNormally);
        }
        catch (OperationCanceledException) when (!lifecycleCt.IsCancellationRequested)
        {
            state.TurnCount++;
            state.IsIdle = true;
            RecordTeammateMetrics("turn_interrupted", true);

            _logger?.LogInformation("Teammate {TeammateId} interrupted at turn={TurnCount}, entering idle to wait for next prompt",
                definition.TeammateId, state.TurnCount);

            var waitResult = await WaitForNextPromptOrShutdownAsync(
                definition.TeammateId, lifecycleCt).ConfigureAwait(false);

            state.IsIdle = false;

            return waitResult switch
            {
                TeammateWaitResult.ShutdownRequest => (true, completedNormally),
                TeammateWaitResult.NewMessage => (false, completedNormally),
                _ => (true, completedNormally),
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Teammate {TeammateId} loop iteration failed", definition.TeammateId);
            state.IsIdle = true;
            RecordTeammateMetrics("turn_error", false);

            _logger?.LogWarning("Teammate {TeammateId} checkpoint: turn={TurnCount} failed, will retry after delay", definition.TeammateId, state.TurnCount);

            var shouldExit = await WaitForRetryOrCancelAsync(lifecycleCt).ConfigureAwait(false);
            return (shouldExit, completedNormally);
        }
    }

    /// <summary>
    /// 等待重试延迟 — 取消时返回 true(应退出),否则 false(重试)
    /// </summary>
    private static async Task<bool> WaitForRetryOrCancelAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    /// <summary>
    /// 安全触发 TeammateCompleted 事件 — handler 异常仅警告
    /// </summary>
    private void InvokeTeammateCompletedSafely(InProcessTeammateDefinition definition, TeammateState state, bool completedNormally)
    {
        try
        {
            TeammateCompleted?.Invoke(this, new TeammateCompletedEventArgs
            {
                TeammateId = definition.TeammateId,
                Task = definition.Task,
                Output = state.LastResult,
                IsSuccess = completedNormally,
                TurnCount = state.TurnCount
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Teammate {TeammateId} TeammateCompleted event handler threw", definition.TeammateId);
        }
    }

    private async Task<TeammateWaitResult> WaitForNextPromptOrShutdownAsync(
        string teammateId, CancellationToken lifecycleCt)
    {
        if (!_pendingMessages.TryGetValue(teammateId, out var channel))
        {
            await WaitForCancellationAsync(lifecycleCt).ConfigureAwait(false);
            return TeammateWaitResult.Aborted;
        }

        return await ReadNextMessageAsync().ConfigureAwait(false);

        static async Task WaitForCancellationAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        async Task<TeammateWaitResult> ReadNextMessageAsync()
        {
            try
            {
                await foreach (var message in channel.Reader.ReadAllAsync(lifecycleCt).ConfigureAwait(false))
                {
                    if (message.MessageType == TeammateMessageType.ShutdownRequest.ToValue())
                    {
                        return TeammateWaitResult.ShutdownRequest;
                    }

                    return TeammateWaitResult.NewMessage;
                }
            }
            catch (OperationCanceledException)
            {
                return TeammateWaitResult.Aborted;
            }

            return TeammateWaitResult.Aborted;
        }
    }

    private async Task NotifyIdleAsync(string teammateId, TeammateState state, string? lastResult)
    {
        try
        {
            var idleNotification = new TeammateIdleNotification
            {
                AgentId = teammateId,
                TeamName = state.Context.TeamName,
                LastResult = lastResult
            };

            var content = JsonSerializer.Serialize(idleNotification, TeammateMessageJsonContext.Default.TeammateIdleNotification);

            var message = new CoordinatorMessage
            {
                FromAgentId = teammateId,
                ToAgentId = "coordinator",
                MessageType = TeammateMessageType.IdleNotification.ToValue(),
                Content = content
            };

            await _messageBroker.SendAsync("coordinator", message).ConfigureAwait(false);

            _logger?.LogDebug("Teammate {TeammateId} sent idle notification", teammateId);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send idle notification for Teammate {TeammateId}", teammateId);
        }
    }

    private async Task CleanupTeammateAsync(string teammateId, TeammateState state)
    {
        StopMailboxPollingIfNeeded(teammateId);
        _messageBroker.UnregisterAgent(teammateId);

        _pendingMessages.TryRemove(teammateId, out var channel);
        channel?.Writer.Complete();

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

    private async Task TryCleanupTeammateAsync(string teammateId)
    {
        try
        {
            using var guard = await _teammateLock.TryLockAsync(CancellationToken.None).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_teammateLock.Name}' 等待超时");

            if (_activeTeammates.TryRemove(teammateId, out var state))
            {
                await CleanupTeammateAsync(teammateId, state).ConfigureAwait(false);
            }
        
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.CleanupTeammateAttemptFailedLog, teammateId));
        }
    }

    private void RecordTeammateMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("scheduling.teammate.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "In-process teammate execution count");

    /// <summary>
    /// 尝试进入 plan mode — PlanModeRequired 且未已在 plan mode 时自动进入,失败仅警告不抛异常
    /// </summary>
    private async Task TryEnterPlanModeIfNeededAsync(InProcessTeammateDefinition definition, CancellationToken ct)
    {
        try
        {
            _logger?.LogInformation("Teammate {TeammateId} requires plan mode, entering automatically", definition.TeammateId);

            var planResult = await _planModeManager!.EnterPlanModeAsync(
                description: $"Teammate {definition.TeammateId}: {definition.Task}",
                cancellationToken: ct).ConfigureAwait(false);

            if (!planResult.Success)
            {
                _logger?.LogWarning("Teammate {TeammateId} failed to enter plan mode: {Error}", definition.TeammateId, planResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Teammate {TeammateId} failed to enter plan mode", definition.TeammateId);
        }
    }

    private void StartMailboxPollingIfNeeded(string teammateId)
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

    private void StopMailboxPollingIfNeeded(string teammateId)
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

    protected override void OnDispose() => _teammateLock.Dispose();

    /// <summary>
    /// Teammate 单轮工作作用域 — 封装 RunTeammateLoopAsync 单轮的"前-后"配对(workCts+状态注册/反注册)
    /// 构造时创建 workCts,EnterAsync 注册到 state 供 Interrupt 读取,DisposeAsync 反注册+释放 workCts
    /// 用 await using var work = new TeammateWorkScope(...) 管理生命周期,消除散落的 try-finally 配对
    /// </summary>
    private sealed class TeammateWorkScope : IAsyncDisposable
    {
        private readonly InProcessTeammateTaskExecutor _owner;
        private readonly string _teammateId;
        private readonly CancellationTokenSource _workCts;
        private int _disposed;

        /// <summary>单轮工作取消令牌 — 传给 ExecuteAsync,Interrupt 时 cancel</summary>
        public CancellationToken Token => _workCts.Token;

        public TeammateWorkScope(InProcessTeammateTaskExecutor owner, string teammateId, CancellationToken lifecycleCt)
        {
            _owner = owner;
            _teammateId = teammateId;
            _workCts = CancellationTokenSource.CreateLinkedTokenSource(lifecycleCt);
        }

        /// <summary>注册 workCts 到 state,供 InterruptTeammateAsync 读取并 cancel</summary>
        public Task EnterAsync(CancellationToken lifecycleCt) => _owner.SetCurrentWorkCtsAsync(_teammateId, _workCts, lifecycleCt);

        public async ValueTask DisposeAsync()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            await _owner.ClearCurrentWorkCtsAsync(_teammateId).ConfigureAwait(false);
            _workCts.Dispose();
        }
    }

    /// <summary>
    /// Teammate 直接执行作用域 — 封装 ExecuteTeammateDirectAsync 的资源获取/释放
    /// <para>InitAsync:spawn agent + worktree 隔离 + 注册 broker/polling + 创建 lifecycleCts + 构造 state + 获取锁 + 注册到 _activeTeammates/_pendingMessages</para>
    /// <para>Detach:ContinuousMode 时调用,后台循环接管清理,DisposeAsync 仅释放锁</para>
    /// <para>DisposeAsync:释放锁 + CleanupTeammateAsync(或半成品清理),修复原资源泄漏 bug(原代码 agent spawn 后、state 注册前抛异常时 agent/broker 泄漏)</para>
    /// </summary>
    private sealed class TeammateDirectScope : IAsyncDisposable
    {
        private readonly InProcessTeammateTaskExecutor _owner;
        private readonly InProcessTeammateDefinition _definition;
        private readonly CancellationToken _externalCt;
        private readonly string _teammateId;

        private IAgent? _agent;
        private bool _brokerRegistered;
        private CancellationTokenSource? _lifecycleCts;
        private TeammateState? _state;
        private IDisposable? _lockGuard;
        private bool _detached;
        private int _disposed;

        /// <summary>teammate 状态 — InitAsync 后可用</summary>
        public TeammateState State => _state ?? throw new InvalidOperationException("Scope not initialized");

        /// <summary>lifecycle 取消令牌 — 传给 RunTeammateLoopBackground</summary>
        public CancellationToken LifecycleToken => _lifecycleCts?.Token ?? CancellationToken.None;

        public TeammateDirectScope(InProcessTeammateTaskExecutor owner, InProcessTeammateDefinition definition, CancellationToken externalCt)
        {
            _owner = owner;
            _definition = definition;
            _externalCt = externalCt;
            _teammateId = definition.TeammateId;
        }

        /// <summary>
        /// 异步初始化 — 获取所有资源(agent/worktree/broker/polling/lifecycleCts/state/lock/注册)
        /// 抛异常时 DisposeAsync 清理半成品
        /// </summary>
        public async Task InitAsync()
        {
            var options = new SubAgentOptions
            {
                Role = _definition.Role != default ? _definition.Role : AgentRole.Executor,
                Variant = _definition.Variant,
                AdditionalInstructions = _definition.AdditionalInstructions,
                MaxIterations = _definition.MaxIterations,
                ContentReplacementState = _owner._subAgentContextAccessor.Current?.ContentReplacementState?.Clone(),
                SessionId = _owner._subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId,
            };

            _agent = await _owner._agentLifecycleManager.SpawnSubAgentAsync(_definition.Task, options, _externalCt).ConfigureAwait(false);

            if (_definition.IsolationMode == AgentIsolationMode.Worktree && _owner._worktreeService is not null)
            {
                try
                {
                    var wtResult = await _owner._worktreeService.CreateAgentWorktreeAsync(_agent.ObjectId.UniqueId, cancellationToken: _externalCt).ConfigureAwait(false);
                    if (wtResult.Success && wtResult.Session is not null)
                    {
                        ((AgentBase)_agent).Options.WorktreePath = wtResult.Session.WorktreePath;
                        ((AgentBase)_agent).Options.WorktreeBranch = wtResult.Session.BranchName;
                        if (((AgentBase)_agent).Context is not null)
                        {
                            ((AgentBase)_agent).Context!.WorktreePath = wtResult.Session.WorktreePath;
                        }
                        _owner._logger?.LogInformation("Teammate {TeammateId} worktree created: {Path}", _teammateId, wtResult.Session.WorktreePath);
                    }
                    else
                    {
                        _owner._logger?.LogWarning("Teammate {TeammateId} worktree creation failed: {Error}, degrading to normal mode", _teammateId, wtResult.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _owner._logger?.LogWarning(ex, "Teammate {TeammateId} worktree creation exception, degrading to normal mode", _teammateId);
                }
            }

            if (_definition.InitialContext is { Count: > 0 })
            {
                foreach (var ctx in _definition.InitialContext)
                {
                    ((AgentBase)_agent).AddContext(ctx);
                }
            }

            var sessionId = _definition.ParentSessionId ?? _owner._subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
            _owner._messageBroker.RegisterAgent(_teammateId, sessionId);
            _brokerRegistered = true;
            _owner.StartMailboxPollingIfNeeded(_teammateId);

            _lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(_externalCt);

            var teammateContext = new TeammateContext
            {
                AgentId = _teammateId,
                AgentName = _teammateId,
                TeamName = _definition.TeamName ?? "default",
                TeamId = _definition.TeamId,
                Color = _definition.Color,
                PlanModeRequired = _definition.PlanModeRequired,
                ParentSessionId = _definition.ParentSessionId ?? sessionId,
                IsInProcess = true
            };

            _state = new TeammateState
            {
                Agent = _agent,
                LifecycleCts = _lifecycleCts,
                Context = teammateContext,
                IsIdle = false,
                Task = _definition.Task
            };

            _lockGuard = await _owner._teammateLock.TryLockAsync(_externalCt).ConfigureAwait(false)
                ?? throw new System.TimeoutException($"锁 '{_owner._teammateLock.Name}' 等待超时");

            _owner._activeTeammates[_teammateId] = _state;
            _owner._pendingMessages[_teammateId] = Channel.CreateUnbounded<CoordinatorMessage>();
        }

        /// <summary>
        /// 分离作用域 — ContinuousMode 时调用,后台循环接管清理,DisposeAsync 仅释放锁
        /// </summary>
        public void Detach() => _detached = true;

        public async ValueTask DisposeAsync()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

            _lockGuard?.Dispose();

            if (_detached) return;

            _owner._activeTeammates.TryRemove(_teammateId, out _);

            if (_state is not null)
            {
                await _owner.CleanupTeammateAsync(_teammateId, _state).ConfigureAwait(false);
                return;
            }

            _owner._pendingMessages.TryRemove(_teammateId, out var channel);
            channel?.Writer.Complete();
            _lifecycleCts?.Dispose();
            if (_brokerRegistered)
            {
                _owner.StopMailboxPollingIfNeeded(_teammateId);
                _owner._messageBroker.UnregisterAgent(_teammateId);
            }
            if (_agent is not null)
            {
                try
                {
                    await _owner._agentLifecycleManager.DisposeAgentAsync(_agent.ObjectId.UniqueId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _owner._logger?.LogWarning(ex, "清理 Teammate {TeammateId} 半成品 Agent 资源失败", _teammateId);
                }
            }
        }
    }
}

