namespace Host.Tests.Engine;

/// <summary>
/// EngineAgentFactory 主代理构造测试 — ADR 0143 引擎库（从 CliSession.CreateMainAgent 提取）。
/// 契约：serviceProvider null 或 DI 无 IQueryEngine → null（SessionController 回退直接 ChatService 路径）；
/// 有 IQueryEngine → 返回 name=mainAgent 的 Coordinator 主代理。
/// </summary>
public sealed class EngineAgentFactoryTests {
    [Fact]
    public void CreateMainAgent_NullServiceProvider_ReturnsNull() {
        var result = EngineAgentFactory.CreateMainAgent(serviceProvider: null, sessionObjectId: new Session().ObjectId);

        Assert.Null(result);
    }

    [Fact]
    public void CreateMainAgent_NoQueryEngineInDi_ReturnsNull() {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var result = EngineAgentFactory.CreateMainAgent(provider, new Session().ObjectId);

        Assert.Null(result);
    }

    [Fact]
    public void CreateMainAgent_WithQueryEngine_ReturnsCoordinatorMainAgent() {
        using var provider = new ServiceCollection()
            .AddSingleton(CreateMockQueryEngine())
            .BuildServiceProvider();

        var result = EngineAgentFactory.CreateMainAgent(provider, new Session().ObjectId);

        Assert.NotNull(result);
        Assert.Equal("mainAgent", result.Name);
    }

    private static IQueryEngine CreateMockQueryEngine() {
        var mock = new Mock<IQueryEngine>();
        var chunks = new QueryStreamChunk[]
        {
            new() { Type = AgentStreamChunkType.Content, Content = "Hello" },
            new() { Type = AgentStreamChunkType.Complete, ModelId = "test-model" }
        };
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, CancellationToken ct) => EmitChunksAsync(chunks, ct));
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, QueryOptions? opts, CancellationToken ct) => EmitChunksAsync(chunks, ct));
        return mock.Object;
    }

    private static async IAsyncEnumerable<QueryStreamChunk> EmitChunksAsync(
        IReadOnlyList<QueryStreamChunk> chunks,
        [EnumeratorCancellation] CancellationToken ct) {
        foreach (var chunk in chunks) {
            ct.ThrowIfCancellationRequested();
            yield return chunk;
        }
    }
}
