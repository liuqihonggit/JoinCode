
namespace Sync.Tests.Scheduling.Tasks;

public sealed class WorkflowTaskExecutorCheckpointTests : IDisposable
{
    private readonly Mock<JoinCode.Abstractions.Tools.IToolExecutionGateway> _toolGatewayMock;
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly InMemoryFileOperationService _fileOperationService;
    private readonly WorkflowStateStore _stateStore;
    private const string PersistDir = "/test/workflow-checkpoint";

    public WorkflowTaskExecutorCheckpointTests()
    {
        _toolGatewayMock = new Mock<JoinCode.Abstractions.Tools.IToolExecutionGateway>();
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _fileOperationService = new InMemoryFileOperationService();
        _stateStore = new WorkflowStateStore(_fileOperationService, PersistDir);
    }

    public void Dispose()
    {
        _fileOperationService.Dispose();
    }

    private WorkflowTaskExecutor CreateExecutor() => new(
        _toolGatewayMock.Object,
        _lifecycleManagerMock.Object,
        NullLogger<WorkflowTaskExecutor>.Instance,
        stateStore: _stateStore);

    private void SetupToolSuccess(string resultText = "ok")
    {
        _toolGatewayMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(new ToolResult { Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = resultText } } });
    }

    private static WorkflowDefinition CreateDagWorkflow(string workflowId = "wf-dag")
    {
        return new WorkflowDefinition
        {
            WorkflowId = workflowId,
            ExecutionMode = WorkflowExecutionMode.Dag,
            Steps = new List<WorkflowStep>
            {
                new() { StepId = "step-a", Name = "A", StepType = WorkflowStepType.ToolCall, ToolName = "tool_a" },
                new() { StepId = "step-b", Name = "B", StepType = WorkflowStepType.ToolCall, ToolName = "tool_b" },
                new() { StepId = "step-c", Name = "C", StepType = WorkflowStepType.ToolCall, ToolName = "tool_c", DependsOn = new List<string> { "step-a", "step-b" } }
            }
        };
    }

    [Fact]
    public async Task ExecuteDagAsync_WithStateStore_ShouldSaveSnapshot()
    {
        SetupToolSuccess();
        var executor = CreateExecutor();

        var result = await executor.ExecuteWorkflowAsync(CreateDagWorkflow()).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Completed);
        var snapshot = await _stateStore.LoadSnapshotAsync("wf-dag").ConfigureAwait(true);
        snapshot.Should().NotBeNull("DAG 执行后应保存快照");
    }

    [Fact]
    public async Task ExecuteDagAsync_Snapshot_ShouldContainAllCompletedSteps()
    {
        SetupToolSuccess();
        var executor = CreateExecutor();

        await executor.ExecuteWorkflowAsync(CreateDagWorkflow("wf-steps")).ConfigureAwait(true);

        var snapshot = await _stateStore.LoadSnapshotAsync("wf-steps").ConfigureAwait(true);
        snapshot!.StepStates.Should().HaveCount(3);
        snapshot.StepStates["step-a"].Should().Be(StepState.Completed);
        snapshot.StepStates["step-b"].Should().Be(StepState.Completed);
        snapshot.StepStates["step-c"].Should().Be(StepState.Completed);
    }

    [Fact]
    public async Task ExecuteDagAsync_WithoutStateStore_ShouldNotThrow()
    {
        SetupToolSuccess();
        var executor = new WorkflowTaskExecutor(
            _toolGatewayMock.Object,
            _lifecycleManagerMock.Object,
            NullLogger<WorkflowTaskExecutor>.Instance);

        var result = await executor.ExecuteWorkflowAsync(CreateDagWorkflow("wf-no-store")).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Completed);
    }

    [Fact]
    public async Task ExecuteDagAsync_StepFailure_ShouldRecordFailedInSnapshot()
    {
        _toolGatewayMock
            .Setup(x => x.ExecuteAsync("tool_a", It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(new ToolResult { Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "ok" } } });
        _toolGatewayMock
            .Setup(x => x.ExecuteAsync("tool_fail", It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ThrowsAsync(new JoinCode.Abstractions.Exceptions.WorkflowException("tool failed", "TOOL_ERROR"));

        var executor = CreateExecutor();

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-fail",
            ExecutionMode = WorkflowExecutionMode.Dag,
            Steps = new List<WorkflowStep>
            {
                new() { StepId = "step-ok", Name = "OK", StepType = WorkflowStepType.ToolCall, ToolName = "tool_a" },
                new() { StepId = "step-fail", Name = "Fail", StepType = WorkflowStepType.ToolCall, ToolName = "tool_fail", OnFailure = WorkflowStepOnFailure.Continue }
            }
        };

        await executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        var snapshot = await _stateStore.LoadSnapshotAsync("wf-fail").ConfigureAwait(true);
        snapshot!.StepStates["step-ok"].Should().Be(StepState.Completed);
        snapshot.StepStates["step-fail"].Should().Be(StepState.Failed);
    }

    [Fact]
    public async Task ExecuteDagAsync_WithExistingSnapshot_ShouldSkipCompletedSteps()
    {
        SetupToolSuccess();
        var executor = CreateExecutor();

        var snapshot = new WorkflowSnapshot
        {
            WorkflowId = "wf-resume",
            StepStates = new Dictionary<string, StepState> { ["step-a"] = StepState.Completed },
            LastUpdated = DateTimeOffset.UtcNow
        };
        await _stateStore.SaveSnapshotAsync("wf-resume", snapshot).ConfigureAwait(true);

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-resume",
            ExecutionMode = WorkflowExecutionMode.Dag,
            Steps = new List<WorkflowStep>
            {
                new() { StepId = "step-a", Name = "A", StepType = WorkflowStepType.ToolCall, ToolName = "tool_a" },
                new() { StepId = "step-b", Name = "B", StepType = WorkflowStepType.ToolCall, ToolName = "tool_b", DependsOn = new List<string> { "step-a" } }
            }
        };

        await executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        _toolGatewayMock.Verify(x => x.ExecuteAsync("tool_a", It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()), Times.Never, "快照中已完成的步骤不应重复执行");
        _toolGatewayMock.Verify(x => x.ExecuteAsync("tool_b", It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()), Times.Once, "未完成的步骤应执行");
    }

    [Fact]
    public async Task ExecuteDagAsync_WithExistingSnapshot_ShouldProduceCompletedResult()
    {
        SetupToolSuccess();
        var executor = CreateExecutor();

        var snapshot = new WorkflowSnapshot
        {
            WorkflowId = "wf-resume-result",
            StepStates = new Dictionary<string, StepState> { ["step-a"] = StepState.Completed },
            LastUpdated = DateTimeOffset.UtcNow
        };
        await _stateStore.SaveSnapshotAsync("wf-resume-result", snapshot).ConfigureAwait(true);

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-resume-result",
            ExecutionMode = WorkflowExecutionMode.Dag,
            Steps = new List<WorkflowStep>
            {
                new() { StepId = "step-a", Name = "A", StepType = WorkflowStepType.ToolCall, ToolName = "tool_a" },
                new() { StepId = "step-b", Name = "B", StepType = WorkflowStepType.ToolCall, ToolName = "tool_b", DependsOn = new List<string> { "step-a" } }
            }
        };

        var result = await executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Completed);
    }

    [Fact]
    public async Task ExecuteDagAsync_RestartWithSameStore_ShouldSkipAllCompletedSteps()
    {
        SetupToolSuccess();

        var executor1 = CreateExecutor();
        await executor1.ExecuteWorkflowAsync(CreateDagWorkflow("wf-restart")).ConfigureAwait(true);

        var snapshotAfterFirstRun = await _stateStore.LoadSnapshotAsync("wf-restart").ConfigureAwait(true);
        snapshotAfterFirstRun!.StepStates.Should().HaveCount(3, "首次执行后应保存全部 3 步骤快照");

        _toolGatewayMock.Reset();
        SetupToolSuccess();

        var executor2 = CreateExecutor();
        var result = await executor2.ExecuteWorkflowAsync(CreateDagWorkflow("wf-restart")).ConfigureAwait(true);

        _toolGatewayMock.Verify(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()), Times.Never, "重启后所有步骤应从快照恢复,不重复执行");
        result.Status.Should().Be(TaskExecutionStatus.Completed);
    }

    [Fact]
    public async Task ExecuteDagAsync_SnapshotInconsistent_ShouldDiscardAndExecuteFromScratch()
    {
        SetupToolSuccess();
        var executor = CreateExecutor();

        var snapshot = new WorkflowSnapshot
        {
            WorkflowId = "wf-inconsistent",
            StepStates = new Dictionary<string, StepState> { ["non-existent-step"] = StepState.Completed },
            LastUpdated = DateTimeOffset.UtcNow
        };
        await _stateStore.SaveSnapshotAsync("wf-inconsistent", snapshot).ConfigureAwait(true);

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-inconsistent",
            ExecutionMode = WorkflowExecutionMode.Dag,
            Steps = new List<WorkflowStep>
            {
                new() { StepId = "step-a", Name = "A", StepType = WorkflowStepType.ToolCall, ToolName = "tool_a" }
            }
        };

        var result = await executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Completed);
        _toolGatewayMock.Verify(x => x.ExecuteAsync("tool_a", It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()), Times.Once, "快照不一致时应从头执行");
    }
}
