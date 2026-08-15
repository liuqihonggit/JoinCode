
namespace Sync.Tests.Scheduling.Tasks;

public class WorkflowTaskExecutorTests
{
    private readonly Mock<JoinCode.Abstractions.Tools.IToolExecutionGateway> _toolGatewayMock;
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly WorkflowTaskExecutor _executor;

    public WorkflowTaskExecutorTests()
    {
        _toolGatewayMock = new Mock<JoinCode.Abstractions.Tools.IToolExecutionGateway>();
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _executor = new WorkflowTaskExecutor(
            _toolGatewayMock.Object,
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

        _toolGatewayMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
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

        _toolGatewayMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, System.Text.Json.JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
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
        var agent = new AgentBase("Test task", null,
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
            .Setup(x => x.DisposeAgentAsync(agent.ObjectId.UniqueId, It.IsAny<CancellationToken>()))
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
            x => x.DisposeAgentAsync(agent.ObjectId.UniqueId, It.IsAny<CancellationToken>()),
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

    [Fact]
    public async Task ExecuteWorkflowAsync_ToolCallStep_ShouldInvokeGatewayExecute()
    {
        // 权限拦截回归守卫：ToolCall 步骤必须经由 IToolExecutionGateway.ExecuteAsync 入口，
        // 而非绕过权限管道直接调用 IToolRegistry.ExecuteToolAsync。
        var gatewayMock = new Mock<IToolExecutionGateway>();
        var lifecycleMock = new Mock<IAgentLifecycleManager>();
        var executor = new WorkflowTaskExecutor(
            gatewayMock.Object,
            lifecycleMock.Object,
            NullLogger<WorkflowTaskExecutor>.Instance);

        var expectedToolName = "permission_guarded_tool";
        var toolResult = new ToolResult
        {
            Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "executed via gateway" } }
        };

        gatewayMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(toolResult);

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-gateway-guard",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepId = "step-1",
                    Name = "Tool call via gateway",
                    StepType = WorkflowStepType.ToolCall,
                    ToolName = expectedToolName
                }
            },
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        var result = await executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Completed);
        gatewayMock.Verify(
            x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_ShouldNotAcceptIToolRegistry()
    {
        // 接口不可绕过守卫：WorkflowTaskExecutor 构造函数不得再接受 IToolRegistry 参数，
        // 确保工具调用只能经由 IToolExecutionGateway 收敛到权限管道。
        var ctorParameterTypes = typeof(WorkflowTaskExecutor)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        ctorParameterTypes.Should().NotContain(typeof(IToolRegistry));
        ctorParameterTypes.Should().Contain(typeof(IToolExecutionGateway));
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WorkflowExceptionFailure_ShouldCaptureErrorCode()
    {
        var inner = new InvalidOperationException("inner failure detail");
        var apiEx = new JoinCode.Abstractions.Exceptions.ApiException(
            "API failed", inner, statusCode: 500, errorCode: "API008");

        _toolGatewayMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ThrowsAsync(apiEx);

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-errorcode",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepId = "step-1",
                    Name = "Fail",
                    StepType = WorkflowStepType.ToolCall,
                    ToolName = "fail_tool",
                    OnFailure = WorkflowStepOnFailure.Stop
                }
            },
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        var result = await _executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Failed);
        result.ErrorMessage.Should().Be("API failed");
        var serialized = System.Text.Json.JsonSerializer.Serialize(
            new StepStatus
            {
                StepId = "step-1",
                State = StepState.Failed,
                Result = JsonElementHelper.FromString(string.Empty),
                Error = "API failed",
                ErrorCode = "API008",
                ErrorDetail = apiEx.ToString()
            },
            Core.Scheduling.SchedulingTasksJsonContext.Default.StepStatus);
        serialized.Should().Contain("API008");
        serialized.Should().Contain("inner failure detail");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_PlainExceptionFailure_ShouldCaptureFullDetail()
    {
        _toolGatewayMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-plain",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepId = "step-1",
                    Name = "Fail",
                    StepType = WorkflowStepType.ToolCall,
                    ToolName = "fail_tool",
                    OnFailure = WorkflowStepOnFailure.Stop
                }
            },
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        var result = await _executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        result.Status.Should().Be(TaskExecutionStatus.Failed);
        result.ErrorMessage.Should().Be("boom");
        var serialized = System.Text.Json.JsonSerializer.Serialize(
            new StepStatus
            {
                StepId = "step-1",
                State = StepState.Failed,
                Result = JsonElementHelper.FromString(string.Empty),
                Error = "boom",
                ErrorDetail = "System.InvalidOperationException: boom"
            },
            Core.Scheduling.SchedulingTasksJsonContext.Default.StepStatus);
        serialized.Should().Contain("InvalidOperationException");
        serialized.Should().Contain("boom");
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_RetryWithMaxRetries_ShouldUseConfiguredCount()
    {
        var callCount = 0;
        _toolGatewayMock
            .Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                throw new InvalidOperationException("transient");
            });

        var definition = new WorkflowDefinition
        {
            WorkflowId = "wf-retry",
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    StepId = "step-1",
                    Name = "Retry",
                    StepType = WorkflowStepType.ToolCall,
                    ToolName = "retry_tool",
                    OnFailure = WorkflowStepOnFailure.Retry,
                    MaxRetries = 2
                }
            },
            ExecutionMode = WorkflowExecutionMode.Sequential
        };

        var result = await _executor.ExecuteWorkflowAsync(definition).ConfigureAwait(true);

        callCount.Should().Be(3);
    }
}
