namespace Core.Scheduling.Tasks;

public interface IInProcessTeammateTaskExecutor
{
    Task<AgentTaskResult> ExecuteTeammateAsync(InProcessTeammateDefinition definition, CancellationToken ct = default);
    Task<bool> SendMessageToTeammateAsync(string teammateId, CoordinatorMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetActiveTeammatesAsync(CancellationToken ct = default);
    Task StopTeammateAsync(string teammateId, CancellationToken ct = default);
    Task TerminateTeammateAsync(string teammateId, string? reason = null, CancellationToken ct = default);
    Task<bool> IsTeammateIdleAsync(string teammateId, CancellationToken ct = default);
}

public sealed partial class InProcessTeammateDefinition
{
    public required string TaskId { get; init; }
    public required string TeammateId { get; init; }
    public required string Task { get; init; }
    public string? SystemPrompt { get; init; }
    public string? AgentType { get; init; }
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
    public required ISubAgent Agent { get; init; }
    public required CancellationTokenSource LifecycleCts { get; init; }
    public required TeammateContext Context { get; init; }
    public bool IsIdle { get; set; }
    public string? LastResult { get; set; }
    public int TurnCount { get; set; }
}

[Register]
public sealed partial class InProcessTeammateTaskExecutor : IInProcessTeammateTaskExecutor
{
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    private readonly IAgentMessageBroker _messageBroker;
    [Inject] private readonly ILogger<InProcessTeammateTaskExecutor>? _logger;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
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
        IAgentMessageBroker messageBroker,
        ILogger<InProcessTeammateTaskExecutor>? logger = null,
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

        if (executeMiddlewares is not null)
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
                AgentType = definition.AgentType,
                AdditionalInstructions = definition.AdditionalInstructions,
                MaxIterations = definition.MaxIterations,
                ContentReplacementState = _subAgentContextAccessor.Current?.ContentReplacementState?.Clone(),
                SessionId = _subAgentContextAccessor.Current?.SessionId ?? "default",
            };

            var agent = await _agentLifecycleManager.SpawnSubAgentAsync(definition.Task, options, ct).ConfigureAwait(false);

            if (definition.InitialContext is { Count: > 0 })
            {
                foreach (var ctx in definition.InitialContext)
                {
                    agent.AddContext(ctx);
                }
            }

            var sessionId = definition.ParentSessionId ?? _subAgentContextAccessor.Current?.SessionId ?? "default";
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
                IsIdle = false
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
                _ = RunTeammateLoopAsync(definition, state, lifecycleCts.Token);

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

        return await _messageBroker.SendMessageAsync(teammateId, message, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetActiveTeammatesAsync(CancellationToken ct = default)
    {
        await _teammateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _activeTeammates.Keys.ToList();
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

    private async Task RunTeammateLoopAsync(
        InProcessTeammateDefinition definition,
        TeammateState state,
        CancellationToken lifecycleCt)
    {
        var subAgentContext = new SubAgentContext
        {
            AgentId = state.Context.AgentId,
            AgentType = "teammate",
            Task = definition.Task,
            ParentAgentId = _subAgentContextAccessor.Current?.AgentId,
            SessionId = definition.ParentSessionId ?? _subAgentContextAccessor.Current?.SessionId ?? "default",
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
                try
                {
                    using var workCts = CancellationTokenSource.CreateLinkedTokenSource(lifecycleCt);

                    var result = await _agentLifecycleManager.ExecuteAsync(state.Agent, workCts.Token).ConfigureAwait(false);

                    state.TurnCount++;
                    state.IsIdle = true;
                    state.LastResult = result.Output;

                    await NotifyIdleAsync(definition.TeammateId, state, result.Output).ConfigureAwait(false);

                    RecordTeammateMetrics("turn_complete", result.IsSuccess);

                    var waitResult = await WaitForNextPromptOrShutdownAsync(
                        definition.TeammateId, lifecycleCt).ConfigureAwait(false);

                    state.IsIdle = false;

                    switch (waitResult)
                    {
                        case TeammateWaitResult.ShutdownRequest:
                            _logger?.LogInformation("Teammate {TeammateId} received shutdown request", definition.TeammateId);
                            shouldExit = true;
                            break;
                        case TeammateWaitResult.NewMessage:
                            _logger?.LogDebug("Teammate {TeammateId} received new message, continuing loop", definition.TeammateId);
                            break;
                        case TeammateWaitResult.Aborted:
                            shouldExit = true;
                            break;
                    }
                }
                catch (OperationCanceledException) when (lifecycleCt.IsCancellationRequested)
                {
                    shouldExit = true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Teammate {TeammateId} loop iteration failed", definition.TeammateId);
                    state.IsIdle = true;
                    RecordTeammateMetrics("turn_error", false);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), lifecycleCt).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        shouldExit = true;
                    }
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

            await _messageBroker.SendMessageAsync("coordinator", message).ConfigureAwait(false);

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
            await _agentLifecycleManager.DisposeAgentAsync(state.Agent.Id, CancellationToken.None).ConfigureAwait(false);
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
}

