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
    public List<string>? InitialContext { get; init; }
    public string? TeamName { get; init; }
    public string? TeamId { get; init; }
    public string? ParentSessionId { get; init; }
    public string? Color { get; init; }
    public bool PlanModeRequired { get; init; }
    public bool ContinuousMode { get; init; }
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
    private readonly SemaphoreSlim _teammateLock = new(1, 1);
    private readonly MiddlewarePipeline<TeammateExecutionContext>? _executePipeline;

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
        IClockService? clock = null)
    {
        _agentLifecycleManager = agentLifecycleManager;
        _messageBroker = messageBroker;
        _logger = logger;
        _telemetryService = telemetryService;
        _mailboxPoller = mailboxPoller;
        _planModeManager = planModeManager;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;

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

            var options = new SubAgentOptions
            {
                Role = definition.Role != default ? definition.Role : AgentRole.Executor,
                Variant = definition.Variant,
                AdditionalInstructions = definition.AdditionalInstructions,
                MaxIterations = definition.MaxIterations,
                ContentReplacementState = _subAgentContextAccessor.Current?.ContentReplacementState?.Clone(),
                SessionId = _subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId,
            };

            var agent = await _agentLifecycleManager.SpawnSubAgentAsync(definition.Task, options, ct).ConfigureAwait(false);

            if (definition.InitialContext is { Count: > 0 })
            {
                foreach (var ctx in definition.InitialContext)
                {
                    ((AgentBase)agent).AddContext(ctx);
                }
            }

            var sessionId = definition.ParentSessionId ?? _subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
            _messageBroker.RegisterAgent(definition.TeammateId, sessionId);

            StartMailboxPollingIfNeeded(definition.TeammateId);

            var lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var teammateContext = new TeammateContext
            {
                AgentId = definition.TeammateId,
                AgentName = definition.TeammateId,
                TeamName = definition.TeamName ?? "default",
                TeamId = definition.TeamId,
                Color = definition.Color,
                PlanModeRequired = definition.PlanModeRequired,
                ParentSessionId = definition.ParentSessionId ?? sessionId,
                IsInProcess = true
            };

            var state = new TeammateState
            {
                Agent = agent,
                LifecycleCts = lifecycleCts,
                Context = teammateContext,
                IsIdle = false,
                Task = definition.Task
            };

            await _teammateLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _activeTeammates[definition.TeammateId] = state;
            }
            finally
            {
                _teammateLock.Release();
            }

            _pendingMessages[definition.TeammateId] = Channel.CreateUnbounded<CoordinatorMessage>();

            if (definition.ContinuousMode)
            {
                RunTeammateLoopBackground(definition, state, lifecycleCts.Token);

                var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
                return AgentTaskResult.Success(definition.TaskId, definition.TeammateId, "Teammate started in continuous mode", elapsed);
            }

            if (definition.PlanModeRequired && _planModeManager != null && !_planModeManager.IsInPlanMode)
            {
                try
                {
                    _logger?.LogInformation("Teammate {TeammateId} requires plan mode, entering automatically", definition.TeammateId);

                    var planResult = await _planModeManager.EnterPlanModeAsync(
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

            var result = await _agentLifecycleManager.ExecuteAsync(agent, ct).ConfigureAwait(false);
            var elapsed2 = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;

            await CleanupTeammateAsync(definition.TeammateId, state).ConfigureAwait(false);

            RecordTeammateMetrics("execute", result.IsSuccess);
            return result.IsSuccess
                ? AgentTaskResult.Success(definition.TaskId, definition.TeammateId, result.Output ?? string.Empty, elapsed2)
                : AgentTaskResult.Failure(definition.TaskId, definition.TeammateId, result.Error ?? "Teammate execution failed", elapsed2);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await TryCleanupTeammateAsync(definition.TeammateId).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            var elapsed = (long)(_clock.GetUtcNow() - startTime).TotalMilliseconds;
            _logger?.LogError(ex, L.T(StringKey.InProcessTeammateFailedLog, definition.TeammateId));
            await TryCleanupTeammateAsync(definition.TeammateId).ConfigureAwait(false);
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
        await _teammateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _activeTeammates.Keys;
        }
        finally
        {
            _teammateLock.Release();
        }
    }

    /// <summary>
    /// 返回所有活跃 teammate 的状态快照 — 供 GUI 渲染子会话树（含 ParentSessionId/Task/IsIdle 等）。
    /// </summary>
    public async Task<IEnumerable<TeammateStateSnapshot>> GetActiveTeammateSnapshotsAsync(CancellationToken ct = default)
    {
        await _teammateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _activeTeammates.Select(kv => new TeammateStateSnapshot(
                kv.Key,
                kv.Value.Context.ParentSessionId,
                kv.Value.Task,
                kv.Value.IsIdle,
                kv.Value.TurnCount,
                kv.Value.LastResult)).ToList();
        }
        finally
        {
            _teammateLock.Release();
        }
    }

    public async Task StopTeammateAsync(string teammateId, CancellationToken ct = default)
    {
        await _teammateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_activeTeammates.TryRemove(teammateId, out var state))
            {
                await state.LifecycleCts.CancelAsync().ConfigureAwait(false);
                await CleanupTeammateAsync(teammateId, state).ConfigureAwait(false);
            }
        }
        finally
        {
            _teammateLock.Release();
        }
    }

    public async Task TerminateTeammateAsync(string teammateId, string? reason = null, CancellationToken ct = default)
    {
        TeammateState? state;

        await _teammateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_activeTeammates.TryGetValue(teammateId, out state))
            {
                return;
            }
        }
        finally
        {
            _teammateLock.Release();
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
        await _teammateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _activeTeammates.TryGetValue(teammateId, out var state) && state.IsIdle;
        }
        finally
        {
            _teammateLock.Release();
        }
    }

    /// <summary>
    /// 中断 teammate 当前 per-turn work — 只 cancel <see cref="TeammateState.CurrentWorkCts"/>，
    /// 不 cancel lifecycle，teammate 进 idle 等待 next prompt（对齐 ClaudeCode inProcessRunner ESC 行为）。
    /// 若 teammate 不存在或当前无活跃 work，返回 false。
    /// </summary>
    public async Task<bool> InterruptTeammateAsync(string teammateId, CancellationToken ct = default)
    {
        await _teammateLock.WaitAsync(ct).ConfigureAwait(false);
        CancellationTokenSource? workCts;
        try
        {
            if (!_activeTeammates.TryGetValue(teammateId, out var state))
            {
                return false;
            }
            workCts = state.CurrentWorkCts;
        }
        finally
        {
            _teammateLock.Release();
        }

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
        await _teammateLock.WaitAsync(lifecycleCt).ConfigureAwait(false);
        try
        {
            if (_activeTeammates.TryGetValue(teammateId, out var state))
            {
                state.CurrentWorkCts = workCts;
            }
        }
        finally
        {
            _teammateLock.Release();
        }
    }

    /// <summary>
    /// 循环体 finally 调用 — 清空 state.CurrentWorkCts，避免 Interrupt 取到已 dispose 的旧 cts。
    /// 用 CancellationToken.None 保证清理不被取消。
    /// </summary>
    private async Task ClearCurrentWorkCtsAsync(string teammateId)
    {
        await _teammateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_activeTeammates.TryGetValue(teammateId, out var state))
            {
                state.CurrentWorkCts = null;
            }
        }
        finally
        {
            _teammateLock.Release();
        }
    }

    /// <summary>
    /// 后台启动 teammate 循环 — 观察未处理异常，避免静默死亡；退出时通知 coordinator
    /// </summary>
    private void RunTeammateLoopBackground(InProcessTeammateDefinition definition, TeammateState state, CancellationToken lifecycleCt)
    {
        _ = SafeRunLoopAsync();

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

        using (state.Context.EnterScope())
        using (subAgentContext.EnterScopeWithCwd(null))
        {
            var shouldExit = false;

            if (definition.PlanModeRequired && _planModeManager != null && !_planModeManager.IsInPlanMode)
            {
                try
                {
                    _logger?.LogInformation("Teammate {TeammateId} requires plan mode, entering automatically", definition.TeammateId);

                    var planResult = await _planModeManager.EnterPlanModeAsync(
                        description: $"Teammate {definition.TeammateId}: {definition.Task}",
                        cancellationToken: lifecycleCt).ConfigureAwait(false);

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

            while (!lifecycleCt.IsCancellationRequested && !shouldExit)
            {
                CancellationTokenSource? workCts = null;
                try
                {
                    workCts = CancellationTokenSource.CreateLinkedTokenSource(lifecycleCt);
                    await SetCurrentWorkCtsAsync(definition.TeammateId, workCts, lifecycleCt).ConfigureAwait(false);

                    var result = await _agentLifecycleManager.ExecuteAsync(state.Agent, workCts.Token).ConfigureAwait(false);

                    state.TurnCount++;
                    state.LastResult = result.Output;
                    RecordTeammateMetrics("turn_complete", result.IsSuccess);

                    _logger?.LogDebug("Teammate {TeammateId} checkpoint: turn={TurnCount} success={Success} outputLen={OutputLen}",
                        definition.TeammateId, state.TurnCount, result.IsSuccess, result.Output?.Length ?? 0);

                    // 正常完成 — 退出循环（对齐 forked agent 单次执行语义；Interrupt 后才进 idle 等 next prompt）
                    shouldExit = true;
                }
                catch (OperationCanceledException) when (lifecycleCt.IsCancellationRequested)
                {
                    shouldExit = true;
                }
                catch (OperationCanceledException) when (!lifecycleCt.IsCancellationRequested)
                {
                    // Interrupt — workCts 被 cancel 但 lifecycle 未取消，进 idle 等 next prompt
                    // 对齐 ClaudeCode inProcessRunner ESC：不通知 coordinator（不自动唤醒 mainAgent），仅等用户 next prompt
                    state.TurnCount++;
                    state.IsIdle = true;
                    RecordTeammateMetrics("turn_interrupted", true);

                    _logger?.LogInformation("Teammate {TeammateId} interrupted at turn={TurnCount}, entering idle to wait for next prompt",
                        definition.TeammateId, state.TurnCount);

                    var waitResult = await WaitForNextPromptOrShutdownAsync(
                        definition.TeammateId, lifecycleCt).ConfigureAwait(false);

                    state.IsIdle = false;

                    switch (waitResult)
                    {
                        case TeammateWaitResult.ShutdownRequest:
                            _logger?.LogInformation("Teammate {TeammateId} received shutdown request after interrupt", definition.TeammateId);
                            shouldExit = true;
                            break;
                        case TeammateWaitResult.NewMessage:
                            _logger?.LogDebug("Teammate {TeammateId} received new message after interrupt, resuming work", definition.TeammateId);
                            break;
                        case TeammateWaitResult.Aborted:
                            shouldExit = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Teammate {TeammateId} loop iteration failed", definition.TeammateId);
                    state.IsIdle = true;
                    RecordTeammateMetrics("turn_error", false);

                    _logger?.LogWarning("Teammate {TeammateId} checkpoint: turn={TurnCount} failed, will retry after delay", definition.TeammateId, state.TurnCount);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), lifecycleCt).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        shouldExit = true;
                    }
                }
                finally
                {
                    await ClearCurrentWorkCtsAsync(definition.TeammateId).ConfigureAwait(false);
                    workCts?.Dispose();
                }
            }
        }

        await TryCleanupTeammateAsync(definition.TeammateId).ConfigureAwait(false);

        _logger?.LogInformation("Teammate {TeammateId} loop exited after {TurnCount} turns",
            definition.TeammateId, state.TurnCount);
    }

    private async Task<TeammateWaitResult> WaitForNextPromptOrShutdownAsync(
        string teammateId, CancellationToken lifecycleCt)
    {
        if (!_pendingMessages.TryGetValue(teammateId, out var channel))
        {
            try
            {
                await Task.Delay(Timeout.Infinite, lifecycleCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            return TeammateWaitResult.Aborted;
        }

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

        try
        {
            await _agentLifecycleManager.DisposeAgentAsync(state.Agent.ObjectId.UniqueId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "清理 Teammate {TeammateId} 的 Agent 资源失败", teammateId);
        }

        state.LifecycleCts.Dispose();
    }

    private async Task TryCleanupTeammateAsync(string teammateId)
    {
        try
        {
            await _teammateLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (_activeTeammates.TryRemove(teammateId, out var state))
                {
                    await CleanupTeammateAsync(teammateId, state).ConfigureAwait(false);
                }
            }
            finally
            {
                _teammateLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.CleanupTeammateAttemptFailedLog, teammateId));
        }
    }

    private void RecordTeammateMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("scheduling.teammate.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "In-process teammate execution count");

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
}

