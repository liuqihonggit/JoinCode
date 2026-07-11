
namespace Sync.Tests.Scheduling.Tasks;

public class WorkflowTaskExecutorTests
{
    private readonly Mock<JoinCode.Abstractions.Tools.IToolRegistry> _toolRegistryMock;
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly WorkflowTaskExecutor _executor;

    public WorkflowTaskExecutorTests()
    {
        _toolRegistryMock = new Mock<JoinCode.Abstractions.Tools.IToolRegistry>();
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _executor = new WorkflowTaskExecutor(
            _toolRegistryMock.Object,
            _lifecycleManagerMock.Object,
            NullLogger<WorkflowTaskExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_SequentialMode_ShouldReturnCompletedResult()
    {
        var toolResult = new ToolResult
        {
            Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "ok" } }
        };

        _toolRegistryMock
            .Setup(x => x.ExecuteToolAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(toolResult);

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-001",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepId = "step-1",
                    Name = "First step",
                    StepType = WorkflowStepType.ToolCall,
                    ToolName = "test_tool"
                }
            },
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        var result = await _executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        result.WorkflowId.Should().Be("wf-001");
        result.Status.Should().Be(TaskExecutionStatus.Completed);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_NullDefinition_ShouldThrowArgumentNullException()
    {
        var act = () => _executor.ExecuteWorkflowAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_ToolCallStepWithoutToolName_ShouldReturnFailedResult()
    {
        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-002",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepId = "step-1",
                    Name = "Missing tool",
                    StepType = WorkflowStepType.ToolCall,
                    ToolName = null,
                    OnFailure = WorkflowStepOnFailure.Stop
                }
            },
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        var result = await _executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Failed);
    }

    [Fact]
    public async Task CancelWorkflowAsync_ActiveWorkflow_ShouldChangeStateToCancelled()
    {
        var toolResult = new ToolResult
        {
            Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "ok" } }
        };

        _toolRegistryMock
            .Setup(x => x.ExecuteToolAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(toolResult);

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-cancel",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepId = "step-1",
                    Name = "Step",
                    StepType = WorkflowStepType.ToolCall,
                    ToolName = "test_tool"
                }
            },
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        await _executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        await _executor.CancelWorkflowAsync("wf-cancel").ConfigureAwait(true);

        var status = await _executor.GetWorkflowStatusAsync("wf-cancel").ConfigureAwait(true);
        status.State.Should().Be(TaskExecutionStatus.Failed);
    }

    [Fact]
    public async Task GetWorkflowStatusAsync_NonExistentWorkflow_ShouldReturnFailedStatus()
    {
        var status = await _executor.GetWorkflowStatusAsync("nonexistent").ConfigureAwait(true);

        status.WorkflowId.Should().Be("nonexistent");
        status.State.Should().Be(TaskExecutionStatus.Failed);
        status.CompletedSteps.Should().Be(0);
        status.TotalSteps.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_AgentTaskStep_ShouldExecuteAgent()
    {
        var agent = new SubAgent("agent-1", "Test task", null,
            new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>().Object, null);

        var agentResult = new SubAgentResult
        {
            AgentId = "agent-1",
            IsSuccess = true,
            Output = "Agent completed"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);
        _lifecycleManagerMock
            .Setup(x => x.DisposeAgentAsync(agent.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-agent",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepId = "step-1",
                    Name = "Agent step",
                    Description = "Run agent",
                    StepType = WorkflowStepType.AgentTask
                }
            },
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        var result = await _executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Completed);
        _lifecycleManagerMock.Verify(
            x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _lifecycleManagerMock.Verify(
            x => x.DisposeAgentAsync(agent.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_ConditionalStep_ShouldEvaluateCondition()
    {
        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-cond",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepId = "step-1",
                    Name = "Conditional",
                    StepType = WorkflowStepType.Conditional,
                    Parameters = new Dictionary<string, JsonElement>
                    {
                        ["condition"] = JsonElementHelper.FromBoolean(true),
                        ["onTrue"] = JsonElementHelper.FromString("branch-a"),
                        ["onFalse"] = JsonElementHelper.FromString("branch-b")
                    }
                }
            },
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        var result = await _executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Completed);
    }
}
