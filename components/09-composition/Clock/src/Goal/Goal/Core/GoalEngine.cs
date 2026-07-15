
namespace Core.Goal;

// IGoalEngine 接口已移至 JoinCode.Abstractions.Interfaces.Scheduling

[Register]
public sealed partial class GoalEngine : IGoalEngine, IAsyncDisposable
{
    private readonly IChatClient _kernel;
    private readonly IGoalEvaluator _evaluator;
    private readonly IGoalHeartbeat _heartbeat;
    private readonly SemaphoreSlim _stateLock;
    [Inject] private readonly ILogger<GoalEngine>? _logger;
    [Inject] private readonly IClockService _clock;
    private readonly IToolPermissionManager? _permissionManager;
    private readonly MiddlewarePipeline<GoalLifecycleContext>? _lifecyclePipeline;
    private GoalState? _state;
    private CancellationTokenSource? _engineCts;
    private Task? _engineLoop;
    private int _goalCounter;
    private PermissionMode? _savedPermissionMode;
    private readonly MessageList _chatHistory;
    private TaskCompletionSource? _completionTcs;

    public GoalState? CurrentState => _state;
    public bool IsRunning => _state?.Status == GoalStatus.Pursuing;

    /// <summary>
    /// 等待目标引擎循环退出（完成、预算耗尽、暂停、清除等）。
    /// </summary>
    public Task WaitForCompletionAsync(CancellationToken ct = default)
    {
        return _completionTcs?.Task ?? Task.CompletedTask;
    }

    public GoalEngine(
        IChatClient kernel,
        IGoalEvaluator evaluator,
        ILogger<GoalEngine>? logger = null,
        IToolPermissionManager? permissionManager = null,
        IEnumerable<IGoalLifecycleMiddleware>? lifecycleMiddlewares = null,
        IGoalHeartbeat? heartbeat = null,
        IClockService? clock = null)
    {
        _kernel = kernel;
        _evaluator = evaluator;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _permissionManager = permissionManager;
        _stateLock = new SemaphoreSlim(1, 1);
        _chatHistory = new MessageList();
        _heartbeat = heartbeat ?? throw new ArgumentNullException(nameof(heartbeat));
        _heartbeat.RegisterCallback(OnHeartbeatAsync);

        if (lifecycleMiddlewares is not null)
        {
            _lifecyclePipeline = new MiddlewarePipeline<GoalLifecycleContext>(lifecycleMiddlewares);
        }
    }

    public async Task<GoalState> StartAsync(
        string objective,
        List<string>? constraints = null,
        int? tokenBudget = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);

        if (_lifecyclePipeline is not null)
        {
            return await StartViaPipelineAsync(objective, constraints, tokenBudget, cancellationToken).ConfigureAwait(false);
        }

        return await StartDirectAsync(objective, constraints, tokenBudget, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GoalState> StartViaPipelineAsync(
        string objective,
        List<string>? constraints,
        int? tokenBudget,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != null && _state.Status == GoalStatus.Pursuing)
            {
                throw new InvalidOperationException(L.T(StringKey.GoalEngineAlreadyRunning));
            }

            var goalId = GenerateGoalId();
            _state = new GoalState
            {
                GoalId = goalId,
                Objective = objective,
                Status = GoalStatus.Pursuing,
                Constraints = constraints ?? [],
                TokenBudget = tokenBudget
            };

            _chatHistory.Clear();
            _chatHistory.AddUserMessage(objective);
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.Start,
            Objective = objective,
            Constraints = constraints,
            TokenBudget = tokenBudget,
            CancellationToken = cancellationToken,
            State = _state,
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
            SavedPermissionMode = _savedPermissionMode,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is null)
        {
            return _state;
        }

        await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

        _savedPermissionMode = ctx.SavedPermissionMode;

        if (ctx.ShouldStartEngineLoop)
        {
            _logger?.LogInformation(L.T(StringKey.GoalEngineStarting),
                _state.GoalId, objective, tokenBudget?.ToString() ?? L.T(StringKey.GoalEngineBudgetUnlimited));

            _engineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _completionTcs = new();
            _engineLoop = Task.Run(() => RunGoalLoopAsync(_engineCts.Token));
        }

        return _state;
    }

    private async Task<GoalState> StartDirectAsync(
        string objective,
        List<string>? constraints,
        int? tokenBudget,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != null && _state.Status == GoalStatus.Pursuing)
            {
                throw new InvalidOperationException(L.T(StringKey.GoalEngineAlreadyRunning));
            }

            var goalId = GenerateGoalId();
            _state = new GoalState
            {
                GoalId = goalId,
                Objective = objective,
                Status = GoalStatus.Pursuing,
                Constraints = constraints ?? [],
                TokenBudget = tokenBudget
            };

            _chatHistory.Clear();
            _chatHistory.AddUserMessage(objective);
        }
        finally
        {
            _stateLock.Release();
        }

        await SwitchToGoalPermissionModeAsync(cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation(L.T(StringKey.GoalEngineStarting),
            _state.GoalId, objective, tokenBudget?.ToString() ?? L.T(StringKey.GoalEngineBudgetUnlimited));

        _engineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _completionTcs = new();
        _engineLoop = Task.Run(() => RunGoalLoopAsync(_engineCts.Token));

        return _state;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_lifecyclePipeline is not null)
        {
            await PauseViaPipelineAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await PauseDirectAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PauseViaPipelineAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Paused;
            _state.PausedAt = _clock.GetUtcNow();
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.Pause,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Paused },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        if (ctx.ShouldResetHeartbeat)
        {
            await _heartbeat.ResetAsync().ConfigureAwait(false);
        }

        _logger?.LogInformation(L.T(StringKey.GoalEnginePaused), _state?.GoalId);
    }

    private async Task PauseDirectAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Paused;
            _state.PausedAt = _clock.GetUtcNow();
        }
        finally
        {
            _stateLock.Release();
        }

        await _heartbeat.ResetAsync().ConfigureAwait(false);
        _logger?.LogInformation(L.T(StringKey.GoalEnginePaused), _state?.GoalId);
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_lifecyclePipeline is not null)
        {
            await ResumeViaPipelineAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await ResumeDirectAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ResumeViaPipelineAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Paused) return;

            _state.Status = GoalStatus.Pursuing;
            _state.PausedAt = null;
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.Resume,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Pursuing },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        if (ctx.ShouldStartEngineLoop)
        {
            var continuationPrompt = ContinuationPromptBuilder.BuildContinuationPrompt(
                _state?.Objective ?? throw new InvalidOperationException("GoalState is not initialized."),
                _state.Constraints,
                _state.TokensUsed,
                _state.TokenBudget,
                _state.LastEvaluation?.Reason ?? L.T(StringKey.GoalEngineUserResumeReason));

            _chatHistory.AddSystemMessage(continuationPrompt);

            _engineCts?.Cancel();
            _engineCts?.Dispose();
            _engineCts = new CancellationTokenSource();
            _completionTcs = new();
            _engineLoop = Task.Run(() => RunGoalLoopAsync(_engineCts.Token));
        }

        _logger?.LogInformation(L.T(StringKey.GoalEngineResumed), _state?.GoalId);
    }

    private async Task ResumeDirectAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Paused) return;

            _state.Status = GoalStatus.Pursuing;
            _state.PausedAt = null;
        }
        finally
        {
            _stateLock.Release();
        }

        var continuationPrompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            _state?.Objective ?? throw new InvalidOperationException("GoalState is not initialized."),
            _state.Constraints,
            _state.TokensUsed,
            _state.TokenBudget,
            _state.LastEvaluation?.Reason ?? L.T(StringKey.GoalEngineUserResumeReason));

        _chatHistory.AddSystemMessage(continuationPrompt);

        _engineCts?.Cancel();
        _engineCts?.Dispose();
        _engineCts = new CancellationTokenSource();
        _completionTcs = new();
        _engineLoop = Task.Run(() => RunGoalLoopAsync(_engineCts.Token));
        _logger?.LogInformation(L.T(StringKey.GoalEngineResumed), _state?.GoalId);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_lifecyclePipeline is not null)
        {
            await ClearViaPipelineAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await ClearDirectAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ClearViaPipelineAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null) return;

            _state.Status = GoalStatus.Unmet;
            _state.AchievedAt = _clock.GetUtcNow();
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.Clear,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Unmet },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
            SavedPermissionMode = _savedPermissionMode,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        _savedPermissionMode = ctx.SavedPermissionMode;

        if (ctx.ShouldCancelEngineLoop)
        {
            _engineCts?.Cancel();
        }

        if (ctx.ShouldResetHeartbeat)
        {
            await _heartbeat.ResetAsync().ConfigureAwait(false);
        }

        _logger?.LogInformation(L.T(StringKey.GoalEngineCleared), _state?.GoalId);
    }

    private async Task ClearDirectAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null) return;

            _state.Status = GoalStatus.Unmet;
            _state.AchievedAt = _clock.GetUtcNow();
        }
        finally
        {
            _stateLock.Release();
        }

        _engineCts?.Cancel();
        await _heartbeat.ResetAsync().ConfigureAwait(false);
        await RestorePermissionModeAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation(L.T(StringKey.GoalEngineCleared), _state?.GoalId);
    }

    public async Task MarkCompletedAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_lifecyclePipeline is not null)
        {
            await MarkCompletedViaPipelineAsync(reason, cancellationToken).ConfigureAwait(false);
            return;
        }

        await MarkCompletedDirectAsync(reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkCompletedViaPipelineAsync(string reason, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Achieved;
            _state.AchievedAt = _clock.GetUtcNow();
            _state.LastEvaluation = GoalEvaluationResult.Completed(reason);
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.MarkCompleted,
            Reason = reason,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Achieved },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
            SavedPermissionMode = _savedPermissionMode,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        _savedPermissionMode = ctx.SavedPermissionMode;

        if (ctx.ShouldCancelEngineLoop)
        {
            _engineCts?.Cancel();
        }

        if (ctx.ShouldResetHeartbeat)
        {
            await _heartbeat.ResetAsync().ConfigureAwait(false);
        }

        if (ctx.ShouldSignalCompletion)
        {
            _completionTcs?.TrySetResult();
        }

        _logger?.LogInformation(L.T(StringKey.GoalEngineCompletedByModel), _state?.GoalId, reason);
    }

    private async Task MarkCompletedDirectAsync(string reason, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Achieved;
            _state.AchievedAt = _clock.GetUtcNow();
            _state.LastEvaluation = GoalEvaluationResult.Completed(reason);
        }
        finally
        {
            _stateLock.Release();
        }

        _engineCts?.Cancel();
        await _heartbeat.ResetAsync().ConfigureAwait(false);
        await RestorePermissionModeAsync(cancellationToken).ConfigureAwait(false);
        _completionTcs?.TrySetResult();
        _logger?.LogInformation(L.T(StringKey.GoalEngineCompletedByModel), _state?.GoalId, reason);
    }

    public async Task MarkUnmetAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_lifecyclePipeline is not null)
        {
            await MarkUnmetViaPipelineAsync(reason, cancellationToken).ConfigureAwait(false);
            return;
        }

        await MarkUnmetDirectAsync(reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkUnmetViaPipelineAsync(string reason, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Unmet;
            _state.AchievedAt = _clock.GetUtcNow();
            _state.LastEvaluation = GoalEvaluationResult.NotCompleted(reason);
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.MarkUnmet,
            Reason = reason,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Unmet },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
            SavedPermissionMode = _savedPermissionMode,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        _savedPermissionMode = ctx.SavedPermissionMode;

        if (ctx.ShouldCancelEngineLoop)
        {
            _engineCts?.Cancel();
        }

        if (ctx.ShouldResetHeartbeat)
        {
            await _heartbeat.ResetAsync().ConfigureAwait(false);
        }

        if (ctx.ShouldSignalCompletion)
        {
            _completionTcs?.TrySetResult();
        }

        _logger?.LogInformation(L.T(StringKey.GoalEngineUnmetByModel), _state?.GoalId, reason);
    }

    private async Task MarkUnmetDirectAsync(string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Unmet;
            _state.AchievedAt = _clock.GetUtcNow();
            _state.LastEvaluation = GoalEvaluationResult.NotCompleted(reason);
        }
        finally
        {
            _stateLock.Release();
        }

        _engineCts?.Cancel();
        await _heartbeat.ResetAsync().ConfigureAwait(false);
        await RestorePermissionModeAsync(cancellationToken).ConfigureAwait(false);
        _completionTcs?.TrySetResult();
        _logger?.LogInformation(L.T(StringKey.GoalEngineUnmetByModel), _state?.GoalId, reason);
    }

    private async Task RunGoalLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (_state?.Status != GoalStatus.Pursuing) break;
                }
                finally
                {
                    _stateLock.Release();
                }

                if (_state is { TokenBudget: { } budget } && _state.TokensUsed >= budget)
                {
                    await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        _state.Status = GoalStatus.BudgetLimited;
                    }
                    finally
                    {
                        _stateLock.Release();
                    }

                    var budgetPrompt = ContinuationPromptBuilder.BuildBudgetLimitPrompt(
                        _state.Objective,
                        _state.TokensUsed,
                        _state.TokenBudget.Value,
                        (int)_state.Elapsed.TotalSeconds);
                    _chatHistory.AddSystemMessage(budgetPrompt);

                    _logger?.LogInformation(L.T(StringKey.GoalEngineBudgetExhausted),
                        _state.GoalId, _state.TokensUsed, _state.TokenBudget.Value);
                    break;
                }

                var turnResult = await ExecuteAgentTurnAsync(ct).ConfigureAwait(false);

                await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    _state.TokensUsed += turnResult.TokensUsed;
                    _state.TurnsCompleted++;
                }
                finally
                {
                    _stateLock.Release();
                }

                var evaluation = await _evaluator.EvaluateAsync(
                    _state.Objective,
                    _state.Constraints,
                    turnResult.RecentOutput,
                    ct).ConfigureAwait(false);

                await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    _state.LastEvaluation = evaluation;
                }
                finally
                {
                    _stateLock.Release();
                }

                if (evaluation.IsCompleted)
                {
                    await _stateLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        _state.Status = GoalStatus.Achieved;
                        _state.AchievedAt = _clock.GetUtcNow();
                    }
                    finally
                    {
                        _stateLock.Release();
                    }

                    await _heartbeat.ResetAsync().ConfigureAwait(false);
                    await RestorePermissionModeAsync(ct).ConfigureAwait(false);
                    _logger?.LogInformation(L.T(StringKey.GoalEngineCompleted),
                        _state.GoalId, _state.TurnsCompleted, _state.TokensUsed);
                    break;
                }

                var continuationPrompt = ContinuationPromptBuilder.BuildContinuationPrompt(
                    _state.Objective,
                    _state.Constraints,
                    _state.TokensUsed,
                    _state.TokenBudget,
                    evaluation.Reason);
                _chatHistory.AddSystemMessage(continuationPrompt);

                _logger?.LogDebug(L.T(StringKey.GoalEngineContinuing),
                    _state.GoalId, evaluation.Reason);
            }
        }
        finally
        {
            _completionTcs?.TrySetResult();
        }
    }

    private async Task<GoalTurnResult> ExecuteAgentTurnAsync(CancellationToken ct)
    {
        var chatService = _kernel.GetChatCompletionService();

        var executionSettings = new ChatOptions
        {
            Temperature = 0.7f,
            MaxTokens = 8000,
            ToolChoice = ToolChoice.AutoInvoke
        };

        await _heartbeat.StartActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(false);
        try
        {
            var results = await chatService.GetApiMessageContentsAsync(
                _chatHistory,
                executionSettings,
                _kernel,
                ct).ConfigureAwait(false);

            var content = results.Count > 0 ? results[0].Content ?? string.Empty : string.Empty;
            var tokensUsed = results.Count > 0 && results[0].TokenUsage is { TotalTokens: var tt }
                ? tt
                : 0;

            if (!string.IsNullOrEmpty(content))
            {
                _chatHistory.AddAssistantMessage(content);
            }

            return new GoalTurnResult(content, tokensUsed);
        }
        finally
        {
            await _heartbeat.StopActivityAsync(SessionActivityReason.ApiCall).ConfigureAwait(false);
        }
    }

    private string GenerateGoalId()
    {
        var counter = Interlocked.Increment(ref _goalCounter);
        return $"goal_{counter:D4}_{_clock.GetUtcNow():yyyyMMddHHmmss}";
    }

    private ValueTask OnHeartbeatAsync(CancellationToken cancellationToken)
    {
        _logger?.LogDebug(L.T(StringKey.GoalEngineHeartbeatTriggered),
            _state?.GoalId, _state?.TurnsCompleted);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _engineCts?.Cancel();
        await _heartbeat.ResetAsync().ConfigureAwait(false);

        if (_engineLoop != null)
        {
            try
            {
#pragma warning disable VSTHRD003
                await _engineLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_savedPermissionMode.HasValue)
        {
            await RestorePermissionModeAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await _heartbeat.DisposeAsync().ConfigureAwait(false);
        _stateLock.Dispose();
        _engineCts?.Dispose();
        _completionTcs?.TrySetCanceled();
    }

    private async Task SwitchToGoalPermissionModeAsync(CancellationToken cancellationToken)
    {
        if (_permissionManager == null) return;

        try
        {
            _savedPermissionMode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);
            await _permissionManager.SetPermissionModeAsync(PermissionMode.Auto, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(L.T(StringKey.PermissionModeSwitched), _savedPermissionMode);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.PermissionModeSwitchFailed));
            _savedPermissionMode = null;
        }
    }

    private async Task RestorePermissionModeAsync(CancellationToken cancellationToken)
    {
        if (_permissionManager == null || !_savedPermissionMode.HasValue) return;

        try
        {
            await _permissionManager.SetPermissionModeAsync(_savedPermissionMode.Value, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(L.T(StringKey.PermissionModeRestored), _savedPermissionMode.Value);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.PermissionModeRestoreFailed));
        }
        finally
        {
            _savedPermissionMode = null;
        }
    }
}

internal sealed record GoalTurnResult(string RecentOutput, int TokensUsed);
