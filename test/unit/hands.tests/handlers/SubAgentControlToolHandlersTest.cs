namespace Hands.Tests.Handlers;

/// <summary>
/// SubAgentControlToolHandlers 单元测试 — 验证 list/pause/resume/stop/pause_all/resume_all/stop_all/clean 各 action 路由
/// </summary>
public sealed class SubAgentControlToolHandlersTest {
    private static string GetText(ToolResult result) =>
        result.Content.FirstOrDefault(c => c.Type == ToolContentType.Text)?.Text ?? "";

    private static Mock<IAgentService> CreateAgentService(IEnumerable<RunningAgentInfo>? agents = null) {
        var mock = new Mock<IAgentService>();
        mock.Setup(x => x.GetRunningAgentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents ?? []);
        mock.Setup(x => x.StopAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mock.Setup(x => x.ForwardUserInputToAgentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return mock;
    }

    private static RunningAgentInfo Agent(string id, string state = "running") => new() {
        Id = id, Description = $"agent-{id}", State = ParseState(state)
    };

    private static AgentStatus ParseState(string state) => state switch {
        "running" => AgentStatus.Running,
        "paused" => AgentStatus.Paused,
        "pending" => AgentStatus.Pending,
        _ => AgentStatus.Idle
    };

    private static SubAgentControlOptions Opts(string action, string? id = null) => new() { Action = action, Id = id };

    [Fact]
    public async Task ControlAsync_List_NoAgents_ReturnsEmptyMessage() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService().Object);
        var result = await handlers.ControlAsync(Opts("list"));
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("没有运行中");
    }

    [Fact]
    public async Task ControlAsync_List_WithAgents_ReturnsAgentLines() {
        var agents = new[] { Agent("a1"), Agent("a2", "paused") };
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService(agents).Object);
        var result = await handlers.ControlAsync(Opts("list"));
        GetText(result).Should().Contain("a1").And.Contain("a2");
    }

    [Fact]
    public async Task ControlAsync_Pause_NoId_ReturnsError() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService().Object);
        var result = await handlers.ControlAsync(Opts("pause"));
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("需要 id");
    }

    [Fact]
    public async Task ControlAsync_Pause_NoTeammateExecutor_ReturnsError() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService().Object);
        var result = await handlers.ControlAsync(Opts("pause", "a1"));
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("teammate 执行器未注入");
    }

    [Fact]
    public async Task ControlAsync_Resume_NoId_ReturnsError() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService().Object);
        var result = await handlers.ControlAsync(Opts("resume"));
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("需要 id");
    }

    [Fact]
    public async Task ControlAsync_Resume_WithId_ReturnsSuccess() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService().Object);
        var result = await handlers.ControlAsync(Opts("resume", "a1"));
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("已恢复");
    }

    [Fact]
    public async Task ControlAsync_Stop_NoId_ReturnsError() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService().Object);
        var result = await handlers.ControlAsync(Opts("stop"));
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("需要 id");
    }

    [Fact]
    public async Task ControlAsync_Stop_WithId_DelegatesToStopAgent() {
        var agentMock = CreateAgentService();
        await using var handlers = new SubAgentControlToolHandlers(agentMock.Object);
        var result = await handlers.ControlAsync(Opts("stop", "a1"));
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("已终止");
        agentMock.Verify(x => x.StopAgentAsync("a1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ControlAsync_Stop_StopFails_ReturnsError() {
        var agentMock = new Mock<IAgentService>();
        agentMock.Setup(x => x.StopAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        await using var handlers = new SubAgentControlToolHandlers(agentMock.Object);
        var result = await handlers.ControlAsync(Opts("stop", "a1"));
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("终止失败");
    }

    [Fact]
    public async Task ControlAsync_StopAll_DelegatesToStopAgentForEach() {
        var agents = new[] { Agent("a1"), Agent("a2"), Agent("a3", "paused") };
        var agentMock = CreateAgentService(agents);
        await using var handlers = new SubAgentControlToolHandlers(agentMock.Object);
        var result = await handlers.ControlAsync(Opts("stop_all"));
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("已终止 3");
        agentMock.Verify(x => x.StopAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ControlAsync_StopAll_NoAgents_ReturnsZero() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService().Object);
        var result = await handlers.ControlAsync(Opts("stop_all"));
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("已终止 0");
    }

    [Fact]
    public async Task ControlAsync_Clean_NoAgents_ReturnsCleanMessage() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService().Object);
        var result = await handlers.ControlAsync(Opts("clean"));
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("清理完成").And.Contain("没有运行中");
    }

    [Fact]
    public async Task ControlAsync_Clean_WithAgents_ReturnsRemainingList() {
        var agents = new[] { Agent("a1"), Agent("a2") };
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService(agents).Object);
        var result = await handlers.ControlAsync(Opts("clean"));
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("清理完成").And.Contain("2 个子代理运行中");
    }

    [Fact]
    public async Task ControlAsync_UnknownAction_ReturnsErrorWithValidValues() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService().Object);
        var result = await handlers.ControlAsync(Opts("frobnicate"));
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("未知 action").And.Contain("stop").And.Contain("stop_all").And.Contain("clean");
    }

    [Fact]
    public async Task ControlAsync_PauseAll_NoTeammateExecutor_ReturnsError() {
        await using var handlers = new SubAgentControlToolHandlers(CreateAgentService(new[] { Agent("a1") }).Object);
        var result = await handlers.ControlAsync(Opts("pause_all"));
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("teammate 执行器未注入");
    }

    [Fact]
    public async Task ControlAsync_ResumeAll_DelegatesToForwardInput() {
        var agents = new[] { Agent("a1", "paused"), Agent("a2", "paused") };
        var agentMock = CreateAgentService(agents);
        await using var handlers = new SubAgentControlToolHandlers(agentMock.Object);
        var result = await handlers.ControlAsync(Opts("resume_all"));
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("已恢复 2");
    }
}
