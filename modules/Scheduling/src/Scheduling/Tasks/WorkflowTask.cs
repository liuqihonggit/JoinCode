
namespace Core.Scheduling.Tasks;

public interface IWorkflowTaskExecutor
{
    Task<WorkflowResult> ExecuteWorkflowAsync(WorkflowDefinition definition, CancellationToken ct = default);
    Task<WorkflowStatus> GetWorkflowStatusAsync(string workflowId, CancellationToken ct = default);
    Task CancelWorkflowAsync(string workflowId, CancellationToken ct = default);
}

public sealed partial class WorkflowDefinition
{
    public required string WorkflowId { get; init; }
    public required List<WorkflowStep> Steps { get; init; }
    public WorkflowExecutionMode ExecutionMode { get; init; } = WorkflowExecutionMode.Sequential;
    public Dictionary<string, string>? Variables { get; init; }
}

public sealed partial class WorkflowStep
{
    public required string StepId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public List<string>? DependsOn { get; init; }
    public Dictionary<string, JsonElement>? Parameters { get; init; }
    public WorkflowStepType StepType { get; init; }
    public string? ToolName { get; init; }
    public string? AgentType { get; init; }
    public WorkflowStepOnFailure OnFailure { get; init; } = WorkflowStepOnFailure.Stop;
}

public enum WorkflowExecutionMode
{
    [EnumValue("sequential")] Sequential,
    [EnumValue("parallel")] Parallel,
    [EnumValue("dag")] Dag
}
public enum WorkflowStepType { ToolCall, AgentTask, SubWorkflow, Conditional }
public enum WorkflowStepOnFailure { Stop, Skip, Retry, Continue }

public sealed partial class WorkflowResult
{
    public required string WorkflowId { get; init; }
    public required TaskExecutionStatus Status { get; init; }
    public Dictionary<string, JsonElement> StepResults { get; init; } = new();
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}

public sealed partial class WorkflowStatus
{
    public required string WorkflowId { get; init; }
    public required TaskExecutionStatus State { get; init; }
    public Dictionary<string, StepStatus> StepStatuses { get; init; } = new();
    public int CompletedSteps { get; init; }
    public int TotalSteps { get; init; }
}

public enum StepState { [EnumValue("pending")] Pending, [EnumValue("running")] Running, [EnumValue("completed")] Completed, [EnumValue("failed")] Failed, [EnumValue("skipped")] Skipped }

public sealed partial class StepStatus
{
    public required string StepId { get; init; }
    public required StepState State { get; init; }
    public JsonElement Result { get; init; }
    public string? Error { get; init; }
    public TimeSpan? Duration { get; init; }
}

[Register]
public sealed partial class WorkflowTaskExecutor : IWorkflowTaskExecutor
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IAgentLifecycleManager _agentLifecycleManager;
    [Inject] private readonly ILogger<WorkflowTaskExecutor>? _logger;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly ConcurrentDictionary<string, WorkflowRunState> _activeWorkflows = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    public WorkflowTaskExecutor(
        IToolRegistry toolRegistry,
        IAgentLifecycleManager agentLifecycleManager,
        ILogger<WorkflowTaskExecutor>? logger = null,
        ITelemetryService? telemetryService = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        IClockService? clock = null)
    {
        _toolRegistry = toolRegistry;
        _agentLifecycleManager = agentLifecycleManager;
        _logger = logger;
        _telemetryService = telemetryService;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task<WorkflowResult> ExecuteWorkflowAsync(WorkflowDefinition definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var startTime = _clock.GetUtcNow();
        var runState = new WorkflowRunState(definition);
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

    public async Task CancelWorkflowAsync(string workflowId, CancellationToken ct = default)
    {
        if (_activeWorkflows.TryGetValue(workflowId, out var runState))
        {
            await _stateLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                runState.Cts.Cancel();
                runState.State = TaskExecutionStatus.Cancelled;
            }
            finally
            {
                _stateLock.Release();
            }
        }
    }

    private async Task<WorkflowResult> ExecuteSequentialAsync(WorkflowRunState runState, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, runState.Cts.Token);

        foreach (var step in runState.Definition.Steps)
        {
            linkedCts.Token.ThrowIfCancellationRequested();

            var stepResult = await ExecuteStepAsync(step, runState, linkedCts.Token).ConfigureAwait(false);
            runState.StepStatuses[step.StepId] = stepResult;

            if (stepResult.State == StepState.Failed)
            {
                var action = step.OnFailure;
                if (action == WorkflowStepOnFailure.Stop)
                {
                    return BuildResult(runState, TaskExecutionStatus.Failed, stepResult.Error);
                }
                if (action == WorkflowStepOnFailure.Skip)
                {
                    runState.StepStatuses[step.StepId] = new StepStatus { StepId = stepResult.StepId, State = StepState.Skipped, Error = stepResult.Error, Result = stepResult.Result, Duration = stepResult.Duration };
                }
            }
        }

        return BuildResult(runState, TaskExecutionStatus.Completed);
    }

    private async Task<WorkflowResult> ExecuteParallelAsync(WorkflowRunState runState, CancellationToken ct)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, runState.Cts.Token);

        var tasks = runState.Definition.Steps.Select(step => ExecuteStepWithFailureHandlingAsync(step, runState, linkedCts.Token)).ToArray();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedCts.Token.IsCancellationRequested)
        {
            return BuildResult(runState, TaskExecutionStatus.Cancelled);
        }

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

        foreach (var level in levels)
        {
            linkedCts.Token.ThrowIfCancellationRequested();

            var ready = level
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
                }

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
        }

        var hasFailure = runState.StepStatuses.Values.Any(s => s.State == StepState.Failed);
        return BuildResult(runState, hasFailure ? TaskExecutionStatus.Failed : TaskExecutionStatus.Completed);
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
        var result = await ExecuteStepAsync(step, runState, ct).ConfigureAwait(false);

        if (result.State == StepState.Failed && step.OnFailure == WorkflowStepOnFailure.Retry)
        {
            for (var i = 0; i < 3; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (i + 1)), ct).ConfigureAwait(false);
                result = await ExecuteStepAsync(step, runState, ct).ConfigureAwait(false);
                if (result.State != StepState.Failed) break;
            }
        }

        runState.StepStatuses[step.StepId] = result;
        return result;
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
                Error = ex.Message,
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
        var result = await _toolRegistry.ExecuteToolAsync(step.ToolName, args, ct).ConfigureAwait(false);
        return string.Join("\n", result.Content.Select(c => c.Text ?? string.Empty));
    }

    private async Task<string> ExecuteAgentTaskStepAsync(WorkflowStep step, CancellationToken ct)
    {
        var description = step.Description ?? step.Name;
        var options = new SubAgentOptions
        {
            AdditionalInstructions = step.AgentType is not null ? $"Agent type: {step.AgentType}" : null,
            ContentReplacementState = _subAgentContextAccessor.Current?.ContentReplacementState?.Clone(),
            SessionId = _subAgentContextAccessor.Current?.SessionId ?? "default",
        };

        var agent = await _agentLifecycleManager.SpawnSubAgentAsync(description, options, ct).ConfigureAwait(false);
        var result = await _agentLifecycleManager.ExecuteAsync(agent, ct).ConfigureAwait(false);
        await _agentLifecycleManager.DisposeAgentAsync(agent.Id, ct).ConfigureAwait(false);

        return result.Output ?? string.Empty;
    }

    private async Task<string> ExecuteSubWorkflowStepAsync(WorkflowStep step, WorkflowRunState parentState, CancellationToken ct)
    {
        var subDefinition = new WorkflowDefinition
        {
            WorkflowId = $"{parentState.Definition.WorkflowId}:{step.StepId}",
            Steps = step.Parameters?.GetValueOrDefault("steps") is JsonElement stepsEl && stepsEl.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize(stepsEl, SchedulingTasksJsonContext.Default.ListWorkflowStep) ?? []
                : [],
            ExecutionMode = step.Parameters?.GetValueOrDefault("executionMode") is JsonElement modeEl && modeEl.ValueKind == JsonValueKind.String
                ? WorkflowExecutionModeExtensions.FromValue(modeEl.GetString()) ?? WorkflowExecutionMode.Sequential : WorkflowExecutionMode.Sequential,
            Variables = step.Parameters?
                .Where(kvp => kvp.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetString() ?? string.Empty)
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
}
