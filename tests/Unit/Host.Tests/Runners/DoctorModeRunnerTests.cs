namespace Core.Tests.Runners;


public class DoctorModeRunnerTests
{
    [Fact]
    public async Task RunAsync_WithAgentRunnerAchieved_ReturnsZero()
    {
        var runner = new Mock<IAgentRunner>();
        runner.SetupGet(e => e.CurrentState).Returns(new GoalState
        {
            GoalId = "goal_001",
            Objective = "自举复盘",
            Status = GoalStatus.Achieved,
        });
        runner.Setup(e => e.RunAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string obj, string? sp, CancellationToken ct) => new GoalState
            {
                GoalId = "goal_001",
                Objective = obj,
                Status = GoalStatus.Pursuing,
            });
        runner.Setup(e => e.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var agentProvider = new Mock<IAgentDefinitionProvider>();
        agentProvider.Setup(p => p.GetAgentDefinitionAsync(It.IsAny<AgentRole>(), It.IsAny<ExecutorVariant?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDefinition
            {
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Doctor,
                WhenToUse = "自举复盘与修复",
                SystemPrompt = "你是 doctor Agent",
            });

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IAgentRunner))).Returns(runner.Object);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns(agentProvider.Object);

        var options = new CommandLineOptions { DoctorMode = true };

        var result = await DoctorModeRunner.RunAsync(options, services.Object).ConfigureAwait(true);

        result.Should().Be(0);
        runner.Verify(e => e.RunAsync(
            "自举复盘与修复",
            "你是 doctor Agent",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithAgentRunnerUnmet_ReturnsOne()
    {
        var runner = new Mock<IAgentRunner>();
        runner.SetupGet(e => e.CurrentState).Returns(new GoalState
        {
            GoalId = "goal_002",
            Objective = "自举复盘",
            Status = GoalStatus.Unmet,
        });
        runner.Setup(e => e.RunAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string obj, string? sp, CancellationToken ct) => new GoalState
            {
                GoalId = "goal_002",
                Objective = obj,
                Status = GoalStatus.Pursuing,
            });
        runner.Setup(e => e.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IAgentRunner))).Returns(runner.Object);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns((IAgentDefinitionProvider?)null);

        var options = new CommandLineOptions { DoctorMode = true };

        var result = await DoctorModeRunner.RunAsync(options, services.Object).ConfigureAwait(true);

        result.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WithNoAgentRunner_ThrowsInvalidOperationException()
    {
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IAgentRunner))).Returns((IAgentRunner?)null);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns((IAgentDefinitionProvider?)null);

        var options = new CommandLineOptions { DoctorMode = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DoctorModeRunner.RunAsync(options, services.Object)).ConfigureAwait(true);
    }

    [Fact]
    public async Task RunAsync_WithAgentRunnerAlreadyRunning_ReturnsTwo()
    {
        var runner = new Mock<IAgentRunner>();
        runner.Setup(e => e.RunAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("已有目标正在运行"));

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IAgentRunner))).Returns(runner.Object);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns((IAgentDefinitionProvider?)null);

        var options = new CommandLineOptions { DoctorMode = true };

        var result = await DoctorModeRunner.RunAsync(options, services.Object).ConfigureAwait(true);

        result.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_WithoutAgentProvider_UsesDefaultObjective()
    {
        var runner = new Mock<IAgentRunner>();
        runner.SetupGet(e => e.CurrentState).Returns(new GoalState
        {
            GoalId = "goal_003",
            Objective = "default",
            Status = GoalStatus.Achieved,
        });
        runner.Setup(e => e.RunAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string obj, string? sp, CancellationToken ct) => new GoalState
            {
                GoalId = "goal_003",
                Objective = obj,
                Status = GoalStatus.Pursuing,
            });
        runner.Setup(e => e.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IAgentRunner))).Returns(runner.Object);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns((IAgentDefinitionProvider?)null);

        var options = new CommandLineOptions { DoctorMode = true };

        var result = await DoctorModeRunner.RunAsync(options, services.Object).ConfigureAwait(true);

        result.Should().Be(0);
        runner.Verify(e => e.RunAsync(
            "自举复盘与修复 — 分析链路日志，发现缺陷，生成修复 patch",
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
