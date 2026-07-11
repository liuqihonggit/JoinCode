
namespace Core.Tests.Agents;

public class PlanAgentTests
{
    private readonly IChatClient _kernel;
    private readonly PlanAgent _agent;

    public PlanAgentTests()
    {
        _kernel = ServiceRegistration.CreateEmptyKernel();

        _agent = new PlanAgent(
            _kernel,
            JoinCode.Abstractions.Clock.SystemClockService.Instance);
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        Assert.Equal("PlanAgent", _agent.Name);
        Assert.Equal(BuiltInAgentType.Plan, _agent.AgentType);
        Assert.Contains("计划", _agent.Description);
        Assert.NotNull(_agent.SystemPrompt);
    }

    [Fact]
    public async Task GetContextAsync_ReturnsValidContext()
    {
        var context = await _agent.GetContextAsync().ConfigureAwait(true);

        Assert.NotNull(context);
        Assert.NotEmpty(context.Messages);
        Assert.Equal("system", context.Messages[0].Role);
        Assert.Equal(_agent.SystemPrompt, context.Messages[0].Content);
    }

    [Fact]
    public async Task ClearContextAsync_ResetsToInitialState()
    {
        var originalContext = await _agent.GetContextAsync().ConfigureAwait(true);

        await _agent.ClearContextAsync().ConfigureAwait(true);
        var newContext = await _agent.GetContextAsync().ConfigureAwait(true);

        Assert.Equal(originalContext.Messages.Count, newContext.Messages.Count);
        Assert.Equal(originalContext.Messages[0].Content, newContext.Messages[0].Content);
    }

    [Fact]
    public void PlanRequest_CanBeCreated()
    {
        var request = new PlanRequest
        {
            Goal = "测试目标",
            Context = "测试上下文",
            Constraints = new List<string> { "约束1", "约束2" },
            Preferences = new List<string> { "偏好1" }
        };

        Assert.Equal("测试目标", request.Goal);
        Assert.Equal("测试上下文", request.Context);
        Assert.Equal(2, request.Constraints.Count);
        Assert.Single(request.Preferences);
    }

    [Fact]
    public void PlanResult_CanBeCreated()
    {
        var result = new PlanResult
        {
            Success = true,
            PlanId = "test123",
            Content = "测试计划内容",
            ExecutionTimeMs = 1000
        };

        Assert.True(result.Success);
        Assert.Equal("test123", result.PlanId);
        Assert.Equal("测试计划内容", result.Content);
        Assert.Equal(1000, result.ExecutionTimeMs);
    }

    [Fact]
    public void ExploreRequest_CanBeCreated()
    {
        var request = new ExploreRequest
        {
            TargetPath = "/test/path",
            FocusArea = "核心组件",
            Questions = new List<string> { "问题1", "问题2" },
            Depth = ExploreDepth.Detailed
        };

        Assert.Equal("/test/path", request.TargetPath);
        Assert.Equal("核心组件", request.FocusArea);
        Assert.Equal(2, request.Questions.Count);
        Assert.Equal(ExploreDepth.Detailed, request.Depth);
    }

    [Fact]
    public void VerificationRequest_CanBeCreated()
    {
        var request = new VerificationRequest
        {
            Code = "console.log('test');",
            Language = "javascript",
            Context = "测试代码",
            Aspects = new List<VerificationAspect> { VerificationAspect.Security, VerificationAspect.Performance }
        };

        Assert.Equal("console.log('test');", request.Code);
        Assert.Equal("javascript", request.Language);
        Assert.Equal(2, request.Aspects.Count);
    }

    [Fact]
    public void GeneralTaskRequest_CanBeCreated()
    {
        var request = new GeneralTaskRequest
        {
            TaskDescription = "测试任务",
            Input = "测试输入",
            Constraints = new List<string> { "约束1" },
            ExpectedOutput = ExpectedOutputFormat.Markdown
        };

        Assert.Equal("测试任务", request.TaskDescription);
        Assert.Equal("测试输入", request.Input);
        Assert.Equal(ExpectedOutputFormat.Markdown, request.ExpectedOutput);
    }
}
