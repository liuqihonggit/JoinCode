namespace Core.Agents.Tests.Unit.Agents;


/// <summary>
/// AgentBase MaxIterations 最大迭代次数限制测试
/// 验证执行循环在超过 MaxIterations 时停止
/// </summary>
public sealed class AgentBaseMaxIterationsTests
{
    [Fact]
    public async Task ExecuteAsync_ExceedsMaxIterations_ReturnsMaxIterationsMessage()
    {
        var queryEngineMock = new Mock<IQueryEngine>();
        queryEngineMock
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<QueryStreamChunk>().ToAsyncEnumerable());

        var options = new SubAgentOptions { MaxIterations = 1 };
        var agent = new AgentBase("test task", options, queryEngineMock.Object, null);

        var result1 = await agent.ExecuteAsync();
        result1.IsSuccess.Should().BeTrue();
        result1.Output.Should().NotContain("已达最大迭代次数");

        var result2 = await agent.ExecuteAsync();
        result2.IsSuccess.Should().BeTrue();
        result2.Output.Should().Contain("已达最大迭代次数");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultMaxIterations_Allows50Executions()
    {
        var queryEngineMock = new Mock<IQueryEngine>();
        queryEngineMock
            .Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Array.Empty<QueryStreamChunk>().ToAsyncEnumerable());

        var agent = new AgentBase("test task", null, queryEngineMock.Object, null);

        var result = await agent.ExecuteAsync();
        result.IsSuccess.Should().BeTrue();
        result.Output.Should().NotContain("已达最大迭代次数");
    }
}
