namespace Core.Agents;

using Core.Agents.ToolHandlers;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

public sealed class BuiltInAgentToolHandlersOutputTests
{
    private static Mock<IFileSystem> CreateFsMock()
    {
        var fsMock = new Mock<IFileSystem>();
        fsMock.Setup(x => x.GetCurrentDirectory()).Returns("X:\\tmp");
        fsMock.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(true);
        fsMock.Setup(x => x.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        return fsMock;
    }

    private static (BuiltInAgentToolHandlers handler, Mock<IAgentService> svc) CreateWithTruncator(string agentId, bool success, string output)
    {
        var svcMock = new Mock<IAgentService>();
        svcMock.Setup(x => x.SpawnAgentAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AgentInfo { Id = agentId, Description = "test" });
        svcMock.Setup(x => x.WaitForAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AgentResult { AgentId = agentId, Success = success, Output = output });

        var roleMock = new Mock<IAgentRoleRegistry>();
        var fsMock = CreateFsMock();
        var truncator = new SubAgentOutputTruncator(fsMock.Object, NullLogger<SubAgentOutputTruncator>.Instance, "X:\\tmp\\subagent");
        var handler = new BuiltInAgentToolHandlers(svcMock.Object, roleMock.Object, NullLogger<BuiltInAgentToolHandlers>.Instance, null, truncator);
        return (handler, svcMock);
    }

    [Fact]
    public async Task PlanAgentAsync_SmallOutput_WrappedInXml_NoArchive()
    {
        var (handler, _) = CreateWithTruncator("agent-small", true, "计划完成");

        var result = await handler.PlanAgentAsync("目标");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("<task ");
        result.GetFirstText().Should().Contain("<task_result>");
        result.GetFirstText().Should().Contain("计划完成");
        result.GetFirstText().Should().NotContain("read 查看");
    }

    [Fact]
    public async Task PlanAgentAsync_HugeOutput_ArchivedPointer_NoCrash()
    {
        var big = new string('x', 300_000);
        var (handler, _) = CreateWithTruncator("agent-big", true, big);

        var result = await handler.PlanAgentAsync("目标");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("read 查看");
        result.GetFirstText().Should().NotContain(big);
    }

    [Fact]
    public async Task PlanAgentAsync_NoTruncator_ReturnsRawOutput()
    {
        var svcMock = new Mock<IAgentService>();
        svcMock.Setup(x => x.SpawnAgentAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AgentInfo { Id = "raw", Description = "t" });
        svcMock.Setup(x => x.WaitForAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(new AgentResult { AgentId = "raw", Success = true, Output = "raw output" });
        var roleMock = new Mock<IAgentRoleRegistry>();
        var handler = new BuiltInAgentToolHandlers(svcMock.Object, roleMock.Object, null, null, null);

        var result = await handler.PlanAgentAsync("目标");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Be("raw output");
    }

    [Fact]
    public async Task ExploreAgentAsync_HugeOutput_ArchivedPointer_NoCrash()
    {
        var big = new string('y', 300_000);
        var (handler, _) = CreateWithTruncator("agent-explore", true, big);

        var result = await handler.ExploreAgentAsync("src/");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("read 查看");
    }
}
