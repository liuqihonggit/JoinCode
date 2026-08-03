namespace Core.Tests.Runners;

using JoinCode;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Interfaces.Scheduling;
using JoinCode.Abstractions.Models.Agent;
using JoinCode.Abstractions.Models.Goal;
using JoinCode.Abstractions.Prompts.ToolPrompts;
using JoinCode.Entry;
using Moq;

public class DoctorModeRunnerTests
{
    [Fact]
    public async Task RunAsync_WithGoalEngineAchieved_ReturnsZero()
    {
        var goalEngine = new Mock<IGoalEngine>();
        goalEngine.SetupGet(e => e.CurrentState).Returns(new GoalState
        {
            GoalId = "goal_001",
            Objective = "自举复盘",
            Status = GoalStatus.Achieved,
        });
        goalEngine.Setup(e => e.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string obj, List<string>? c, int? b, string? sp, CancellationToken ct) => new GoalState
            {
                GoalId = "goal_001",
                Objective = obj,
                Status = GoalStatus.Pursuing,
            });
        goalEngine.Setup(e => e.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
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
        services.Setup(s => s.GetService(typeof(IGoalEngine))).Returns(goalEngine.Object);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns(agentProvider.Object);

        var options = new CommandLineOptions { DoctorMode = true };

        var result = await DoctorModeRunner.RunAsync(options, services.Object).ConfigureAwait(true);

        result.Should().Be(0);
        goalEngine.Verify(e => e.StartAsync(
            "自举复盘与修复",
            It.IsAny<List<string>?>(),
            It.IsAny<int?>(),
            "你是 doctor Agent",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithGoalEngineUnmet_ReturnsOne()
    {
        var goalEngine = new Mock<IGoalEngine>();
        goalEngine.SetupGet(e => e.CurrentState).Returns(new GoalState
        {
            GoalId = "goal_002",
            Objective = "自举复盘",
            Status = GoalStatus.Unmet,
        });
        goalEngine.Setup(e => e.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string obj, List<string>? c, int? b, string? sp, CancellationToken ct) => new GoalState
            {
                GoalId = "goal_002",
                Objective = obj,
                Status = GoalStatus.Pursuing,
            });
        goalEngine.Setup(e => e.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IGoalEngine))).Returns(goalEngine.Object);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns((IAgentDefinitionProvider?)null);

        var options = new CommandLineOptions { DoctorMode = true };

        var result = await DoctorModeRunner.RunAsync(options, services.Object).ConfigureAwait(true);

        result.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_WithNoGoalEngine_ThrowsInvalidOperationException()
    {
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IGoalEngine))).Returns((IGoalEngine?)null);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns((IAgentDefinitionProvider?)null);

        var options = new CommandLineOptions { DoctorMode = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DoctorModeRunner.RunAsync(options, services.Object)).ConfigureAwait(true);
    }

    [Fact]
    public async Task RunAsync_WithGoalEngineAlreadyRunning_ReturnsTwo()
    {
        var goalEngine = new Mock<IGoalEngine>();
        goalEngine.Setup(e => e.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("已有目标正在运行"));

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IGoalEngine))).Returns(goalEngine.Object);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns((IAgentDefinitionProvider?)null);

        var options = new CommandLineOptions { DoctorMode = true };

        var result = await DoctorModeRunner.RunAsync(options, services.Object).ConfigureAwait(true);

        result.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_WithoutAgentProvider_UsesDefaultObjective()
    {
        var goalEngine = new Mock<IGoalEngine>();
        goalEngine.SetupGet(e => e.CurrentState).Returns(new GoalState
        {
            GoalId = "goal_003",
            Objective = "default",
            Status = GoalStatus.Achieved,
        });
        goalEngine.Setup(e => e.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string obj, List<string>? c, int? b, string? sp, CancellationToken ct) => new GoalState
            {
                GoalId = "goal_003",
                Objective = obj,
                Status = GoalStatus.Pursuing,
            });
        goalEngine.Setup(e => e.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IGoalEngine))).Returns(goalEngine.Object);
        services.Setup(s => s.GetService(typeof(IAgentDefinitionProvider))).Returns((IAgentDefinitionProvider?)null);

        var options = new CommandLineOptions { DoctorMode = true };

        var result = await DoctorModeRunner.RunAsync(options, services.Object).ConfigureAwait(true);

        result.Should().Be(0);
        goalEngine.Verify(e => e.StartAsync(
            "自举复盘与修复 — 分析链路日志，发现缺陷，生成修复 patch",
            It.IsAny<List<string>?>(),
            It.IsAny<int?>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
