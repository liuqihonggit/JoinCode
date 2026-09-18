
namespace Core.Scheduling.Tasks;

/// <summary>
/// 工作流任务执行器接口 — 提供工作流的执行、状态查询与取消能力。
/// </summary>
public interface IWorkflowTaskExecutor
{
    /// <summary>
    /// 异步执行指定工作流定义。
    /// </summary>
    /// <param name="definition">工作流定义,包含步骤列表与执行模式。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>工作流执行结果,包含各步骤产物与最终状态。</returns>
    Task<WorkflowResult> ExecuteWorkflowAsync(WorkflowDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// 异步查询指定工作流的当前状态。
    /// </summary>
    /// <param name="workflowId">工作流唯一标识。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>工作流状态快照,包含各步骤状态与完成进度。</returns>
    Task<WorkflowStatus> GetWorkflowStatusAsync(string workflowId, CancellationToken ct = default);

    /// <summary>
    /// 异步取消指定工作流的执行。
    /// </summary>
    /// <param name="workflowId">工作流唯一标识。</param>
    /// <param name="ct">取消令牌。</param>
    Task CancelWorkflowAsync(string workflowId, CancellationToken ct = default);
}

/// <summary>
/// 工作流定义 — 描述一个工作流的标识、步骤集合、执行模式与变量。
/// </summary>
public sealed partial class WorkflowDefinition
{
    /// <summary>工作流唯一标识。</summary>
    public required string WorkflowId { get; init; }
    /// <summary>工作流步骤列表,按声明顺序或依赖关系执行。</summary>
    public required List<WorkflowStep> Steps { get; init; }
    /// <summary>执行模式 — 顺序、并行或 DAG 拓扑排序,默认 Sequential。</summary>
    public WorkflowExecutionMode ExecutionMode { get; init; } = WorkflowExecutionMode.Sequential;
    /// <summary>工作流级变量字典,供步骤间共享数据。</summary>
    public Dictionary<string, string> Variables { get; init; } = [];
}

/// <summary>
/// 工作流步骤定义 — 描述单个步骤的类型、参数、依赖与失败处理策略。
/// </summary>
public sealed partial class WorkflowStep
{
    /// <summary>步骤唯一标识。</summary>
    public required string StepId { get; init; }
    /// <summary>步骤显示名称。</summary>
    public required string Name { get; init; }
    /// <summary>步骤描述,可选。</summary>
    public string? Description { get; init; }
    /// <summary>依赖的步骤标识列表,DAG 模式下用于拓扑排序。</summary>
    public List<string> DependsOn { get; init; } = [];
    /// <summary>步骤参数字典,键为参数名,值为 JSON 元素。</summary>
    public Dictionary<string, JsonElement> Parameters { get; init; } = [];
    /// <summary>步骤类型 — 工具调用、Agent 任务、子工作流或条件判断。</summary>
    public WorkflowStepType StepType { get; init; }
    /// <summary>工具名称,StepType 为 ToolCall 时必填。</summary>
    public string? ToolName { get; init; }
    /// <summary>Agent 类型标识,StepType 为 AgentTask 时使用。</summary>
    public string? AgentType { get; init; }
    /// <summary>Agent 角色,默认 Executor。</summary>
    public AgentRole Role { get; init; } = AgentRole.Executor;
    /// <summary>执行器变体,可选,用于细分 Agent 执行策略。</summary>
    public ExecutorVariant? Variant { get; init; }
    /// <summary>步骤失败时的处理策略,默认 Stop。</summary>
    public WorkflowStepOnFailure OnFailure { get; init; } = WorkflowStepOnFailure.Stop;
    /// <summary>最大重试次数,OnFailure 为 Retry 时生效,默认 3。</summary>
    public int? MaxRetries { get; init; }
}

/// <summary>
/// 工作流执行模式枚举。
/// </summary>
public enum WorkflowExecutionMode
{
    /// <summary>顺序执行 — 按声明顺序依次执行步骤。</summary>
    [EnumValue("sequential")] Sequential,
    /// <summary>并行执行 — 所有步骤同时执行。</summary>
    [EnumValue("parallel")] Parallel,
    /// <summary>DAG 执行 — 按依赖关系拓扑分层并行执行。</summary>
    [EnumValue("dag")] Dag
}

/// <summary>
/// 工作流步骤类型枚举。
/// </summary>
public enum WorkflowStepType
{
    /// <summary>工具调用步骤。</summary>
    [EnumValue("toolCall")]
    ToolCall,
    /// <summary>Agent 任务步骤。</summary>
    [EnumValue("agentTask")]
    AgentTask,
    /// <summary>子工作流步骤。</summary>
    [EnumValue("subWorkflow")]
    SubWorkflow,
    /// <summary>条件判断步骤。</summary>
    [EnumValue("conditional")]
    [EnumValue("conditional")]
    Conditional,
}

/// <summary>
/// 工作流步骤失败处理策略枚举。
/// </summary>
public enum WorkflowStepOnFailure
{
    /// <summary>停止 — 失败即终止整个工作流。</summary>
    [EnumValue("stop")]
    Stop,
    /// <summary>跳过 — 标记为 Skipped 并继续后续步骤。</summary>
    [EnumValue("skip")]
    Skip,
    /// <summary>重试 — 按指数退避重试,达到上限后停止。</summary>
    [EnumValue("retry")]
    Retry,
    /// <summary>继续 — 忽略失败,继续执行后续步骤。</summary>
    [EnumValue("continue")]
    Continue,
}

/// <summary>
/// 工作流执行结果 — 包含最终状态、各步骤产物与耗时。
/// </summary>
public sealed partial class WorkflowResult
{
    /// <summary>工作流唯一标识。</summary>
    public required string WorkflowId { get; init; }
    /// <summary>工作流最终执行状态。</summary>
    public required TaskExecutionStatus Status { get; init; }
    /// <summary>各步骤结果字典,键为步骤标识,值为 JSON 元素。</summary>
    public Dictionary<string, JsonElement> StepResults { get; init; } = new();
    /// <summary>错误信息,失败时填充。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>工作流总耗时。</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// 工作流状态快照 — 描述工作流当前执行进度与各步骤状态。
/// </summary>
public sealed partial class WorkflowStatus
{
    /// <summary>工作流唯一标识。</summary>
    public required string WorkflowId { get; init; }
    /// <summary>工作流当前执行状态。</summary>
    public required TaskExecutionStatus State { get; init; }
    /// <summary>各步骤状态字典,键为步骤标识。</summary>
    public Dictionary<string, StepStatus> StepStatuses { get; init; } = new();
    /// <summary>已完成(含跳过)的步骤数。</summary>
    public int CompletedSteps { get; init; }
    /// <summary>总步骤数。</summary>
    public int TotalSteps { get; init; }
}

/// <summary>
/// 工作流步骤状态枚举。
/// </summary>
public enum StepState
{
    /// <summary>待执行。</summary>
    [EnumValue("pending")] Pending,
    /// <summary>执行中。</summary>
    [EnumValue("running")] Running,
    /// <summary>已完成。</summary>
    [EnumValue("completed")] Completed,
    /// <summary>执行失败。</summary>
    [EnumValue("failed")] Failed,
    /// <summary>已跳过。</summary>
    [EnumValue("skipped")] Skipped
}

/// <summary>
/// 工作流步骤状态记录 — 描述单个步骤的执行结果与错误信息。
/// </summary>
public sealed partial class StepStatus
{
    /// <summary>步骤唯一标识。</summary>
    public required string StepId { get; init; }
    /// <summary>步骤当前状态。</summary>
    public required StepState State { get; init; }
    /// <summary>步骤执行结果,以 JSON 元素表示。</summary>
    public JsonElement Result { get; init; }
    /// <summary>错误消息,失败时填充。</summary>
    public string? Error { get; init; }
    /// <summary>错误代码,失败时填充。</summary>
    public string? ErrorCode { get; init; }
    /// <summary>错误详情(含堆栈),失败时填充。</summary>
    public string? ErrorDetail { get; init; }
    /// <summary>步骤执行耗时,可选。</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// 工作流任务执行器 — 支持顺序、并行、DAG 三种执行模式,提供断点续跑、失败重试与进度上报能力。
/// 通过 Actor 化的 ConcurrentDictionary 管理活跃工作流,AsyncLock 保护取消操作。
/// </summary>
[Register(typeof(IWorkflowTaskExecutor), ServiceLifetime.Singleton)]
public sealed partial class WorkflowTaskExecutor : ServiceEntity, IWorkflowTaskExecutor
{
    private readonly IToolExecutionGateway _toolExecutionGateway;
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    private readonly ILogger<WorkflowTaskExecutor>? _logger;
    private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly IWorkflowStateStore? _stateStore;
    private readonly IWorkflowProgressSink? _progressSink;
    private readonly ConcurrentDictionary<string, WorkflowRunState> _activeWorkflows = new();
    private readonly AsyncLock _stateLock = new();

    /// <summary>
    /// 构造工作流任务执行器。
    /// </summary>
    /// <param name="toolExecutionGateway">工具执行网关,ToolCall 步骤通过它调用工具。</param>
    /// <param name="agentLifecycleManager">Agent 生命周期管理器,AgentTask 步骤通过它 spawn/执行 Agent。</param>
    /// <param name="logger">日志记录器,可选。</param>
    /// <param name="telemetryService">遥测服务,可选,用于记录执行指标。</param>
    /// <param name="subAgentContextAccessor">子 Agent 上下文访问器,可选,默认使用 SubAgentContextAccessor。</param>
    /// <param name="clock">时钟服务,可选,默认使用系统时钟,用于测试时间控制。</param>
    /// <param name="stateStore">工作流状态存储,可选,提供断点续跑能力。</param>
    /// <param name="progressSink">进度上报接收器,可选,用于通知步骤启动/完成/失败/跳过/重试事件。</param>
    public WorkflowTaskExecutor(
        IToolExecutionGateway toolExecutionGateway,
        IAgentLifecycleManager agentLifecycleManager,
        ILogger<WorkflowTaskExecutor>? logger = null,
        ITelemetryService? telemetryService = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null,
        IWorkflowStateStore? stateStore = null,
        IWorkflowProgressSink? progressSink = null)
    {
        _toolExecutionGateway = toolExecutionGateway;
        _agentLifecycleManager = agentLifecycleManager;
        _logger = logger;
        _telemetryService = telemetryService;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
        _stateStore = stateStore;
        _progressSink = progressSink;
    }

    /// <inheritdoc/>
    public async Task<WorkflowResult> ExecuteWorkflowAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();
        var runState = new WorkflowRunState(definition);
        await RestoreFromSnapshotAsync(runState, ct).ConfigureAwait(false);
        _activeWorkflows[definition.WorkflowId] = runState;

        try
        {
            var result = definition.ExecutionMode switch
            {
                WorkflowExecutionMode.Sequential => await ExecuteSequentialAsync(runState, ct).ConfigureAwait(false),
                WorkflowExecutionMode.Parallel => await ExecuteParallelAsync(runState, ct).ConfigureAwait(false),
                WorkflowExecutionMode.Dag => await ExecuteDagAsync(runState, ct).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(definition.ExecutionMode))
            };

            var duration = _clock.GetUtcNow() - startTime;
        _telemetryService?.RecordCount("workflow.execution.count", new Dictionary<string, string> { ["mode"] = definition.ExecutionMode.ToString(), ["success"] = result.Status == TaskExecutionStatus.Completed ? true.ToString() : false.ToString() }, "count", "Workflow execution count");
            return new WorkflowResult
            {
                WorkflowId = result.WorkflowId,
                Status = result.Status,
                StepResults = result.StepResults,
                ErrorMessage = result.ErrorMessage,
                Duration = duration
            };
        }
        finally
        {
            _activeWorkflows.TryRemove(definition.WorkflowId, out _);
        }
    }

    /// <inheritdoc/>
    public Task<WorkflowStatus> GetWorkflowStatusAsync(string workflowId, CancellationToken ct = default)
    {
        if (_activeWorkflows.TryGetValue(workflowId, out var runState))
        {
            return Task.FromResult(runState.ToStatus());
        }

        return Task.FromResult<WorkflowStatus>(new WorkflowStatus
        {
            WorkflowId = workflowId,
            State = TaskExecutionStatus.Failed,
            CompletedSteps = 0,
            TotalSteps = 0
        });
    }

    /// <inheritdoc/>
    public async Task CancelWorkflowAsync(string workflowId, CancellationToken ct = default)
    {
        if (_activeWorkflows.TryGetValue(workflowId, out var runState))
        {
            using var guard = await _stateLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_stateLock.Name}' 等待超时");

            runState.Cts.Cancel();
            runState.State = TaskExecutionStatus.Cancelled;
        
        }
    }

    private async Task<WorkflowResult> ExecuteSequentialAsync(WorkflowRunState runState, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, runState.Cts.Token);

        foreach (var step in runState.Definition.Steps)
        {
            linkedCts.Token.ThrowIfCancellationRequested();

            if (runState.StepStatuses.TryGetValue(step.StepId, out var existingStatus) && existingStatus.State is StepState.Completed or StepState.Skipped)
            {
                continue;
            }

            var stepResult = await ExecuteStepWithFailureHandlingAsync(step, runState, linkedCts.Token).ConfigureAwait(false);
            runState.StepStatuses[step.StepId] = stepResult;
            _logger?.LogDebug("Workflow checkpoint: step {StepId} -> {State}, completed {Completed}/{Total}",
                step.StepId, stepResult.State, runState.StepStatuses.Count, runState.Definition.Steps.Count);

            if (stepResult.State == StepState.Failed)
            {
                var action = step.OnFailure;
                if (action is WorkflowStepOnFailure.Stop or WorkflowStepOnFailure.Retry)
                {
                    await SaveSnapshotIfAvailableAsync(runState, linkedCts.Token).ConfigureAwait(false);
                    _logger?.LogWarning("Workflow stopped at step {StepId} due to failure. Completed: {Completed}/{Total}",
                        step.StepId, runState.StepStatuses.Count, runState.Definition.Steps.Count);
                    return BuildResult(runState, TaskExecutionStatus.Failed, stepResult.Error);
                }
                if (action == WorkflowStepOnFailure.Skip)
                {
                    runState.StepStatuses[step.StepId] = new StepStatus { StepId = stepResult.StepId, State = StepState.Skipped, Error = stepResult.Error, ErrorCode = stepResult.ErrorCode, ErrorDetail = stepResult.ErrorDetail, Result = stepResult.Result, Duration = stepResult.Duration };
                }
            }

            await SaveSnapshotIfAvailableAsync(runState, linkedCts.Token).ConfigureAwait(false);
        }

        return BuildResult(runState, TaskExecutionStatus.Completed);
    }

    private async Task<WorkflowResult> ExecuteParallelAsync(WorkflowRunState runState, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, runState.Cts.Token);

        var pendingSteps = runState.Definition.Steps
            .Where(step => !runState.StepStatuses.TryGetValue(step.StepId, out var st) || st.State is not (StepState.Completed or StepState.Skipped))
            .ToList();

        if (pendingSteps.Count == 0)
        {
            await SaveSnapshotIfAvailableAsync(runState, linkedCts.Token).ConfigureAwait(false);
            return BuildResult(runState, TaskExecutionStatus.Completed);
        }

        var tasks = pendingSteps.Select(step => ExecuteStepWithFailureHandlingAsync(step, runState, linkedCts.Token)).ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedCts.Token.IsCancellationRequested)
        {
            return BuildResult(runState, TaskExecutionStatus.Cancelled);
        }

        await SaveSnapshotIfAvailableAsync(runState, linkedCts.Token).ConfigureAwait(false);

        var hasFailure = runState.StepStatuses.Values.Any(s => s.State == StepState.Failed);
        return BuildResult(runState, hasFailure ? TaskExecutionStatus.Failed : TaskExecutionStatus.Completed);
    }

    private async Task<WorkflowResult> ExecuteDagAsync(WorkflowRunState runState, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, runState.Cts.Token);

        var dag = BuildWorkflowDag(runState);
        if (dag.HasCycle())
        {
            return BuildResult(runState, TaskExecutionStatus.Failed, "Circular dependency detected");
        }

        var levels = dag.TopologicalSortByLevels();
        var completed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kvp in runState.StepStatuses)
        {
            if (kvp.Value.State is StepState.Completed or StepState.Skipped)
            {
                completed.Add(kvp.Key);
            }
        }

        foreach (var level in levels)
        {
            linkedCts.Token.ThrowIfCancellationRequested();

            var ready = level
                .Where(n => !completed.Contains(n.Payload.StepId))
                .Where(n => n.Payload.DependsOn is null || n.Payload.DependsOn.All(d => completed.Contains(d)))
                .Select(n => n.Payload)
                .ToList();

            if (ready.Count == 0)
            {
                var failedDeps = level
                    .Where(n => n.Payload.DependsOn is not null && n.Payload.DependsOn.Any(d => runState.StepStatuses.TryGetValue(d, out var st) && st.State == StepState.Failed))
                    .Select(n => n.Payload)
                    .ToList();

                foreach (var fd in failedDeps)
                {
                    runState.StepStatuses[fd.StepId] = new StepStatus { StepId = fd.StepId, State = StepState.Skipped, Error = "Dependency failed" };
                    completed.Add(fd.StepId);
                    _progressSink?.OnStepSkipped(runState.Definition.WorkflowId, fd.StepId, "Dependency failed");
                }

                await SaveSnapshotIfAvailableAsync(runState, linkedCts.Token).ConfigureAwait(false);
                continue;
            }

            var tasks = ready.Select(step => ExecuteStepWithFailureHandlingAsync(step, runState, linkedCts.Token)).ToArray();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCts.Token.IsCancellationRequested)
            {
                return BuildResult(runState, TaskExecutionStatus.Cancelled);
            }

            foreach (var step in ready)
            {
                completed.Add(step.StepId);
            }

            await SaveSnapshotIfAvailableAsync(runState, linkedCts.Token).ConfigureAwait(false);
        }

        var hasFailure = runState.StepStatuses.Values.Any(s => s.State == StepState.Failed);
        return BuildResult(runState, hasFailure ? TaskExecutionStatus.Failed : TaskExecutionStatus.Completed);
    }

    private async Task SaveSnapshotIfAvailableAsync(WorkflowRunState runState, CancellationToken ct)
    {
        if (_stateStore is null) return;
        var snapshot = runState.ToSnapshot(_clock.GetUtcNow());
        await _stateStore.SaveSnapshotAsync(runState.Definition.WorkflowId, snapshot, ct).ConfigureAwait(false);
    }

    private async Task RestoreFromSnapshotAsync(WorkflowRunState runState, CancellationToken ct)
    {
        if (_stateStore is null) return;

        var snapshot = await _stateStore.LoadSnapshotAsync(runState.Definition.WorkflowId, ct).ConfigureAwait(false);
        if (snapshot is null) return;

        var validStepIds = runState.Definition.Steps.Select(s => s.StepId).ToHashSet(StringComparer.Ordinal);
        if (!snapshot.StepStates.Keys.All(k => validStepIds.Contains(k)))
        {
            _logger?.LogWarning("快照与 definition 不一致，丢弃快照从头执行: {WorkflowId}", runState.Definition.WorkflowId);
            return;
        }

        foreach (var kvp in snapshot.StepStates)
        {
            runState.StepStatuses[kvp.Key] = new StepStatus { StepId = kvp.Key, State = kvp.Value };
        }

        var restoredCount = snapshot.StepStates.Count(kvp => kvp.Value is StepState.Completed or StepState.Skipped);
        _logger?.LogInformation("从快照恢复 workflow: {WorkflowId}, 已完成步骤: {RestoredCount}", runState.Definition.WorkflowId, restoredCount);
    }

    private static Dag<WorkflowStep> BuildWorkflowDag(WorkflowRunState runState)
    {
        var dag = new Dag<WorkflowStep>();

        foreach (var step in runState.Definition.Steps)
        {
            dag.AddNode(new DagNode<WorkflowStep> { Id = step.StepId, Payload = step });
        }

        foreach (var step in runState.Definition.Steps)
        {
            if (step.DependsOn is null) continue;
            foreach (var depId in step.DependsOn)
            {
                dag.AddEdge(new DagEdge { FromId = depId, ToId = step.StepId, Label = "DEPENDS_ON" });
            }
        }

        return dag;
    }

    private async Task<StepStatus> ExecuteStepWithFailureHandlingAsync(WorkflowStep step, WorkflowRunState runState, CancellationToken ct)
    {
        _progressSink?.OnStepStarted(runState.Definition.WorkflowId, step.StepId, step.Name);
        var stepStart = _clock.GetUtcNow();

        try
        {
            var result = await ExecuteStepAsync(step, runState, ct).ConfigureAwait(false);

            if (result.State == StepState.Failed && step.OnFailure == WorkflowStepOnFailure.Retry)
            {
                var maxRetries = step.MaxRetries ?? 3;
                for (var i = 0; i < maxRetries; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    _progressSink?.OnStepRetried(runState.Definition.WorkflowId, step.StepId, i + 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * (i + 1)), ct).ConfigureAwait(false);
                    result = await ExecuteStepAsync(step, runState, ct).ConfigureAwait(false);
                    if (result.State != StepState.Failed) break;
                }
            }

            runState.StepStatuses[step.StepId] = result;

            if (result.State == StepState.Completed)
            {
                _progressSink?.OnStepCompleted(runState.Definition.WorkflowId, step.StepId, _clock.GetUtcNow() - stepStart);
            }
            else if (result.State == StepState.Failed)
            {
                _progressSink?.OnStepFailed(runState.Definition.WorkflowId, step.StepId, result.Error ?? "Unknown", result.ErrorCode);
            }

            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var failedStatus = new StepStatus
            {
                StepId = step.StepId,
                State = StepState.Failed,
                Result = JsonElementHelper.FromString(string.Empty),
                Error = ex.Message,
                ErrorCode = ex is WorkflowException wfEx ? wfEx.ErrorCode : null,
                ErrorDetail = ex.ToString(),
                Duration = TimeSpan.Zero
            };
            runState.StepStatuses[step.StepId] = failedStatus;
            _progressSink?.OnStepFailed(runState.Definition.WorkflowId, step.StepId, ex.Message, ex is WorkflowException wfEx2 ? wfEx2.ErrorCode : null);
            return failedStatus;
        }
    }

    private async Task<StepStatus> ExecuteStepAsync(WorkflowStep step, WorkflowRunState runState, CancellationToken ct)
    {
        var stepStart = _clock.GetUtcNow();

        try
        {
            JsonElement stepResult = step.StepType switch
            {
                WorkflowStepType.ToolCall => JsonElementHelper.FromString(await ExecuteToolCallStepAsync(step, ct).ConfigureAwait(false)),
                WorkflowStepType.AgentTask => JsonElementHelper.FromString(await ExecuteAgentTaskStepAsync(step, ct).ConfigureAwait(false)),
                WorkflowStepType.SubWorkflow => JsonElementHelper.FromString(await ExecuteSubWorkflowStepAsync(step, runState, ct).ConfigureAwait(false)),
                WorkflowStepType.Conditional => JsonElementHelper.FromString(EvaluateConditionalStep(step, runState)),
                _ => throw new ArgumentOutOfRangeException(nameof(step.StepType))
            };

            return new StepStatus
            {
                StepId = step.StepId,
                State = StepState.Completed,
                Result = stepResult,
                Duration = _clock.GetUtcNow() - stepStart
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StepStatus
            {
                StepId = step.StepId,
                State = StepState.Failed,
                Result = JsonElementHelper.FromString(string.Empty),
                Error = ex.Message,
                ErrorCode = ex is WorkflowException wfEx ? wfEx.ErrorCode : null,
                ErrorDetail = ex.ToString(),
                Duration = _clock.GetUtcNow() - stepStart
            };
        }
    }

    private async Task<string> ExecuteToolCallStepAsync(WorkflowStep step, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(step.ToolName))
        {
            throw new InvalidOperationException($"Step {step.StepId}: ToolName is required for ToolCall step type");
        }

        var args = step.Parameters ?? new Dictionary<string, JsonElement>();
        var result = await _toolExecutionGateway.ExecuteAsync(step.ToolName, args, ct).ConfigureAwait(false);
        return string.Join("\n", result.Content.Select(c => c.Text ?? string.Empty));
    }

    private async Task<string> ExecuteAgentTaskStepAsync(WorkflowStep step, CancellationToken ct)
    {
        var description = step.Description ?? step.Name;
        var options = new SubAgentOptions
        {
            AdditionalInstructions = step.Variant.HasValue ? $"Agent type: {step.Variant.Value.ToValue()}" : (step.Role != default ? $"Agent role: {step.Role.ToValue()}" : null),
            ContentReplacementState = _subAgentContextAccessor.Current?.ContentReplacementState?.Clone(),
            SessionId = _subAgentContextAccessor.Current?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId,
        };

        var agent = await _agentLifecycleManager.SpawnSubAgentAsync(description, options, ct).ConfigureAwait(false);
        var result = await _agentLifecycleManager.ExecuteAsync(agent, ct).ConfigureAwait(false);
        await _agentLifecycleManager.DisposeAgentAsync(agent.ObjectId.UniqueId, ct).ConfigureAwait(false);

        return result.Output ?? string.Empty;
    }

    private async Task<string> ExecuteSubWorkflowStepAsync(WorkflowStep step, WorkflowRunState parentState, CancellationToken ct)
    {
        var subDefinition = new WorkflowDefinition
        {
            WorkflowId = $"{parentState.Definition.WorkflowId}:{step.StepId}",
            Steps = step.Parameters?.GetValueOrDefault("steps") is JsonElement stepsEl && stepsEl.ValueKind == JsonValueKind.Array
                ? RelaxedJsonSerializer.Deserialize(stepsEl, SchedulingTasksJsonContext.Default.ListWorkflowStep) ?? []
                : [],
            ExecutionMode = step.Parameters?.GetValueOrDefault("executionMode") is JsonElement modeEl && modeEl.ValueKind == JsonValueKind.String
                ? WorkflowExecutionModeExtensions.FromValue(modeEl.GetString()) ?? WorkflowExecutionMode.Sequential : WorkflowExecutionMode.Sequential,
            Variables = (step.Parameters?
                .Where(kvp => kvp.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetString() ?? string.Empty)) ?? []
        };

        var subResult = await ExecuteWorkflowAsync(subDefinition, ct).ConfigureAwait(false);
        return subResult.Status == TaskExecutionStatus.Completed ? "Sub-workflow completed" : $"Sub-workflow {subResult.Status}: {subResult.ErrorMessage}";
    }

    private static string EvaluateConditionalStep(WorkflowStep step, WorkflowRunState runState)
    {
        if (step.Parameters is null) return "No condition specified";

        var conditionMet = step.Parameters.TryGetValue("condition", out var conditionEl) && conditionEl.ValueKind == JsonValueKind.True;
        var branchKey = conditionMet ? "onTrue" : "onFalse";

        if (step.Parameters.TryGetValue(branchKey, out var branchEl) && branchEl.ValueKind == JsonValueKind.String)
        {
            return branchEl.GetString() ?? string.Empty;
        }

        return conditionMet.ToString().ToLowerInvariant();
    }

    private static WorkflowResult BuildResult(WorkflowRunState runState, TaskExecutionStatus state, string? error = null)
    {
        var stepResults = runState.StepStatuses
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Result);

        return new WorkflowResult
        {
            WorkflowId = runState.Definition.WorkflowId,
            Status = state,
            StepResults = stepResults,
            ErrorMessage = error
        };
    }

    /// <summary>释放资源时回调，释放状态锁。</summary>
    public override void Dispose()
    {
        _stateLock.Dispose();
        base.Dispose();
    }

}

internal sealed class WorkflowRunState
{
    public WorkflowDefinition Definition { get; }
    public TaskExecutionStatus State { get; set; } = TaskExecutionStatus.Pending;
    public Dictionary<string, StepStatus> StepStatuses { get; } = new();
    public CancellationTokenSource Cts { get; } = new();

    public WorkflowRunState(WorkflowDefinition definition)
    {
        Definition = definition;
    }

    public WorkflowStatus ToStatus()
    {
        var completedCount = StepStatuses.Values.Count(s => s.State is StepState.Completed or StepState.Skipped);
        return new WorkflowStatus
        {
            WorkflowId = Definition.WorkflowId,
            State = State,
            StepStatuses = new Dictionary<string, StepStatus>(StepStatuses),
            CompletedSteps = completedCount,
            TotalSteps = Definition.Steps.Count
        };
    }

    public WorkflowSnapshot ToSnapshot(DateTimeOffset now)
    {
        return new WorkflowSnapshot
        {
            WorkflowId = Definition.WorkflowId,
            StepStates = StepStatuses.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.State),
            SkipReasons = StepStatuses
                .Where(kvp => kvp.Value.State == StepState.Skipped)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Error ?? "Skipped"),
            LastUpdated = now
        };
    }
}
