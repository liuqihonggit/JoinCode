
namespace Core.Hooks;

/// <summary>
/// 钩子编排器内部接口（扩展 Abstractions.IHookOrchestrator，增加管理方法）
/// </summary>
public interface IHookOrchestratorInternal : IHookOrchestrator
{
    /// <summary>
    /// 注册钩子执行器
    /// </summary>
    void RegisterExecutor(IHookExecutor executor);

    /// <summary>
    /// 注册配置提供者
    /// </summary>
    void RegisterConfigurationProvider(HookSource source, IHookConfigurationProvider provider);
}

/// <summary>
/// 钩子编排器实现
/// </summary>
[Register]
public sealed partial class HookOrchestrator : IHookOrchestratorInternal
{
    private readonly IHookConfigurationManager _configurationManager;
    private readonly IHookExecutorFactory _executorFactory;
    private readonly ISessionHookManagerInternal _sessionHookManager;
    private readonly IHookEventBroadcaster _eventBroadcaster;
    private readonly IAsyncHookRegistry _asyncHookRegistry;
    private readonly IHookConditionEvaluator _conditionEvaluator;
    [Inject] private readonly ILogger<HookOrchestrator>? _logger;

    public HookOrchestrator(
        IHookConfigurationManager configurationManager,
        IHookExecutorFactory executorFactory,
        ISessionHookManagerInternal sessionHookManager,
        IHookEventBroadcaster eventBroadcaster,
        IAsyncHookRegistry asyncHookRegistry,
        IHookConditionEvaluator conditionEvaluator,
        ILogger<HookOrchestrator>? logger = null)
    {
        _configurationManager = configurationManager;
        _executorFactory = executorFactory;
        _sessionHookManager = sessionHookManager;
        _eventBroadcaster = eventBroadcaster;
        _asyncHookRegistry = asyncHookRegistry;
        _conditionEvaluator = conditionEvaluator;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<HookResult> ExecuteHooksAsync(
        HookInput input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var hookId = Guid.NewGuid().ToString("N");

        _logger?.LogDebug(
            "Executing hooks for event {Event}, matcher={Matcher}",
            input.Event,
            input.Matcher);

        _eventBroadcaster.BroadcastStarted(hookId, $"hook-{input.Event}", input.Event);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var hasResult = false;

        await foreach (var result in ExecuteHooksInternalAsync(input, cancellationToken))
        {
            hasResult = true;
            yield return result;

            if (result.PreventContinuation ||
                result.Outcome == HookOutcome.Blocking)
            {
                break;
            }
        }

        stopwatch.Stop();

        _eventBroadcaster.BroadcastResponse(new BroadcastContext
        {
            HookId = hookId,
            HookName = $"hook-{input.Event}",
            HookEvent = input.Event,
            Output = hasResult ? AsyncHookProcessStatusConstants.Completed : null,
            Stdout = null,
            Stderr = null,
            ExitCode = hasResult ? 0 : null,
            Outcome = HookExecutionOutcome.Success,
            Duration = stopwatch.Elapsed
        });
    }

    /// <inheritdoc />
    public IAsyncEnumerable<HookResult> ExecuteHooksAsync(
        HookEvent hookEvent,
        Dictionary<string, JsonElement> payload,
        string? matcher = null,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var input = new HookInput
        {
            Event = hookEvent,
            Matcher = matcher,
            SessionId = sessionId,
            Payload = payload
        };

        return ExecuteHooksAsync(input, cancellationToken);
    }

    private async IAsyncEnumerable<HookResult> ExecuteHooksInternalAsync(
        HookInput input,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var configHooks = await _configurationManager.GetHooksForEventAsync(
            input.Event,
            input.Matcher,
            cancellationToken).ConfigureAwait(false);

        var sessionHooks = input.SessionId != null
            ? await _sessionHookManager.GetSessionHooksAsync(
                input.SessionId,
                input.Event,
                cancellationToken).ConfigureAwait(false)
            : new List<SourcedHookConfig>();

        var allHooks = configHooks
            .Concat(sessionHooks)
            .OrderBy(h => h.Source.GetPriority())
            .ToList();

        var functionHooks = input.SessionId != null
            ? await _sessionHookManager.GetSessionFunctionHooksAsync(
                input.SessionId,
                input.Event,
                cancellationToken).ConfigureAwait(false)
            : new List<FunctionHook>();

        _logger?.LogDebug(
            "Found {ConfigCount} config hooks, {SessionCount} session hooks, {FunctionCount} function hooks for event {Event}",
            configHooks.Count,
            sessionHooks.Count,
            functionHooks.Count,
            input.Event);

        foreach (var hookConfig in allHooks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            var result = await ExecuteSingleHookAsync(hookConfig.Command, input, cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                yield return result;

                if (result.PreventContinuation ||
                    result.Outcome == HookOutcome.Blocking)
                {
                    yield break;
                }
            }
        }

        foreach (var functionHook in functionHooks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            var result = await ExecuteSingleHookAsync(functionHook, input, cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                yield return result;

                if (functionHook.Once == true && input.SessionId != null)
                {
                    await _sessionHookManager.RemoveFunctionHookAsync(
                        input.SessionId,
                        input.Event,
                        functionHook.Id,
                        cancellationToken).ConfigureAwait(false);
                }

                if (result.PreventContinuation ||
                    result.Outcome == HookOutcome.Blocking)
                {
                    yield break;
                }
            }
        }
    }

    private async Task<HookResult?> ExecuteSingleHookAsync(
        HookCommand hook,
        HookInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await EvaluateConditionAsync(hook.If, input, cancellationToken).ConfigureAwait(false))
            {
                _logger?.LogDebug(
                    "Skipping hook {HookType} due to condition: {Condition}",
                    hook.Type,
                    hook.If);
                return null;
            }

            var executor = _executorFactory.GetExecutor(hook.Type);

            _logger?.LogDebug(
                "Executing {HookType} hook: {HookDisplay}",
                hook.Type,
                hook.GetDisplayText());

            var result = await executor.ExecuteAsync(hook, input, cancellationToken).ConfigureAwait(false);

            _logger?.LogDebug(
                "Hook {HookType} completed with outcome {Outcome}",
                hook.Type,
                result.Outcome);

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to execute hook {HookType}",
                hook.Type);

            return HookResult.NonBlockingError(
                error: ex.Message,
                message: $"Hook execution failed: {ex.Message}");
        }
    }

    private async Task<bool> EvaluateConditionAsync(
        string? condition,
        HookInput input,
        CancellationToken cancellationToken)
    {
        return await _conditionEvaluator.EvaluateAsync(condition, input, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void RegisterExecutor(IHookExecutor executor)
    {
        _executorFactory.RegisterExecutor(executor);
    }

    /// <inheritdoc />
    public void RegisterConfigurationProvider(HookSource source, IHookConfigurationProvider provider)
    {
        (_configurationManager as HookConfigurationManager)?.RegisterProvider(source, provider);
    }
}
