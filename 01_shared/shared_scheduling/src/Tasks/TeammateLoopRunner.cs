namespace Core.Scheduling.Tasks;

/// <summary>
/// Teammate 运行时回调接口 — TeammateLoopRunner 通过此接口访问主类 Actor 的状态管理方法
/// </summary>
internal interface ITeammateRuntime
{
    /// <summary>设置当前 per-turn workCts（供 Interrupt 读取）</summary>
    Task SetCurrentWorkCtsAsync(string teammateId, CancellationTokenSource workCts, CancellationToken ct);

    /// <summary>清空当前 per-turn workCts</summary>
    Task ClearCurrentWorkCtsAsync(string teammateId);

    /// <summary>尝试清理 teammate（从活跃表移除 + 资源清理）</summary>
    Task TryCleanupTeammateAsync(string teammateId);

    /// <summary>获取 teammate 的待处理消息通道</summary>
    Channel<CoordinatorMessage>? GetPendingChannel(string teammateId);

    /// <summary>记录遥测指标</summary>
    void RecordTeammateMetrics(string operation, bool isSuccess);

    /// <summary>触发 TeammateCompleted 事件</summary>
    void OnTeammateCompleted(TeammateCompletedEventArgs args);
}

/// <summary>
/// Teammate 循环执行器 — 封装 teammate 后台循环逻辑（run loop + single turn + wait for prompt + notify idle）
/// 从 InProcessTeammateTaskExecutor 提取，主类 Actor 创建此执行器并委托循环逻辑
/// </summary>
internal sealed class TeammateLoopRunner
{
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    private readonly IMailbox _messageBroker;
    private readonly ILogger? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IPlanModeManager? _planModeManager;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly ITeammateRuntime _runtime;

    public TeammateLoopRunner(
        IAgentLifecycleManager agentLifecycleManager,
        IMailbox messageBroker,
        ILogger? logger,
        ITelemetryService? telemetryService,
        IPlanModeManager? planModeManager,
        ISubAgentContextAccessor subAgentContextAccessor,
        ITeammateRuntime runtime)
    {
        _agentLifecycleManager = agentLifecycleManager;
        _messageBroker = messageBroker;
        _logger = logger;
        _telemetryService = telemetryService;
        _planModeManager = planModeManager;
        _subAgentContextAccessor = subAgentContextAccessor;
        _runtime = runtime;
    }

    /// <summary>
    /// 后台启动 teammate 循环 — 观察未处理异常，避免静默死亡；退出时通知 coordinator
    /// </summary>
    public void RunTeammateLoopBackground(InProcessTeammateDefinition definition, TeammateState state, CancellationToken lifecycleCt)
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
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Teammate {TeammateId} 后台循环异常退出", definition.TeammateId);
                await NotifyIdleAsync(definition.TeammateId, state, $"后台循环异常: {ex.Message}").ConfigureAwait(false);
                await _runtime.TryCleanupTeammateAsync(definition.TeammateId).ConfigureAwait(false);
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
                await using (var work = new TeammateWorkScope(_runtime, definition.TeammateId, lifecycleCt))
                {
                    await work.EnterAsync(lifecycleCt).ConfigureAwait(false);
                    (shouldExit, completedNormally) = await ExecuteSingleTurnAsync(work, definition, state, lifecycleCt, completedNormally).ConfigureAwait(false);
                }
            }
        }

        await _runtime.TryCleanupTeammateAsync(definition.TeammateId).ConfigureAwait(false);

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
            _runtime.RecordTeammateMetrics("turn_complete", result.IsSuccess);

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
            _runtime.RecordTeammateMetrics("turn_interrupted", true);

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
            _runtime.RecordTeammateMetrics("turn_error", false);

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
            _runtime.OnTeammateCompleted(new TeammateCompletedEventArgs
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
        var channel = _runtime.GetPendingChannel(teammateId);
        if (channel is null)
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

    /// <summary>
    /// Teammate 单轮工作作用域 — 封装 RunTeammateLoopAsync 单轮的"前-后"配对(workCts+状态注册/反注册)
    /// 构造时创建 workCts,EnterAsync 注册到 state 供 Interrupt 读取,DisposeAsync 反注册+释放 workCts
    /// 用 await using var work = new TeammateWorkScope(...) 管理生命周期,消除散落的 try-finally 配对
    /// </summary>
    private sealed class TeammateWorkScope : IAsyncDisposable
    {
        private readonly ITeammateRuntime _runtime;
        private readonly string _teammateId;
        private readonly CancellationTokenSource _workCts;
        private int _disposed;

        /// <summary>单轮工作取消令牌 — 传给 ExecuteAsync,Interrupt 时 cancel</summary>
        public CancellationToken Token => _workCts.Token;

        public TeammateWorkScope(ITeammateRuntime runtime, string teammateId, CancellationToken lifecycleCt)
        {
            _runtime = runtime;
            _teammateId = teammateId;
            _workCts = CancellationTokenSource.CreateLinkedTokenSource(lifecycleCt);
        }

        /// <summary>注册 workCts 到 state,供 InterruptTeammateAsync 读取并 cancel</summary>
        public Task EnterAsync(CancellationToken lifecycleCt) => _runtime.SetCurrentWorkCtsAsync(_teammateId, _workCts, lifecycleCt);

        public async ValueTask DisposeAsync()
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            await _runtime.ClearCurrentWorkCtsAsync(_teammateId).ConfigureAwait(false);
            _workCts.Dispose();
        }
    }
}
