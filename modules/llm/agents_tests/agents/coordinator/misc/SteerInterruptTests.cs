namespace Core.Agents.Tests;

/// <summary>
/// 打断并发送（steer）测试 — 用户 @子代理 邮箱转发必须打断当前执行：
/// ①协议属性 CoordinatorMessage.InterruptAndSend（默认 false 向后兼容）；
/// ②AgentBase.RequestSteer 轮级打断信号（chunk 循环检查点跳出 — 当前流尽快收尾，
/// 转发消息在下轮 ExecuteStreamAsync 开头 TryDrain 消费）；
/// ③IAgent 接口面 RequestSteer（生命周期管理器路由触达）。
/// </summary>
public class SteerInterruptTests
{
    /// <summary>协议属性 — InterruptAndSend 默认 false，可显式置 true。</summary>
    [Fact]
    public void CoordinatorMessage_InterruptAndSend_Property()
    {
        var legacy = new CoordinatorMessage { FromAgentId = "a", ToAgentId = "b", MessageType = "user_message", Content = "hi" };
        var steer = new CoordinatorMessage { FromAgentId = "a", ToAgentId = "b", MessageType = "user_message", Content = "hi", InterruptAndSend = true };

        Assert.False(legacy.InterruptAndSend);
        Assert.True(steer.InterruptAndSend);
    }

    /// <summary>RequestSteer — 信号置位后 ExecuteStreamAsync chunk 循环尽快跳出（早于流自然结束）。</summary>
    [Fact]
    public async Task RequestSteer_BreaksChunkLoopEarly()
    {
        var chunks = new List<QueryStreamChunk>();
        for (var i = 0; i < 100; i++)
            chunks.Add(new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = $"chunk{i}" });

        var engine = new SteerableQueryEngine(chunks);
        await using var agent = new TestSteerAgent("任务", engine);

        var consumed = new List<AgentStreamChunk>();
        await using var reader = agent.ExecuteStreamAsync(CancellationToken.None).GetAsyncEnumerator();
        await reader.MoveNextAsync();
        agent.RequestSteer();

        while (await reader.MoveNextAsync()) {
            consumed.Add(reader.Current);
            // 打断后不应消费全部 100 chunk — 收到 Complete 即止
            if (reader.Current.Type == AgentStreamChunkType.Complete)
                break;
        }

        consumed.Count.Should().BeLessThan(99, "steer 信号应尽快打断 chunk 流");
        consumed.Last().Type.Should().Be(AgentStreamChunkType.Complete);
    }

    /// <summary>无信号 — 全量消费不打断（回归保护）。</summary>
    [Fact]
    public async Task NoSteer_ConsumesAllChunks()
    {
        var chunks = new List<QueryStreamChunk>();
        for (var i = 0; i < 5; i++)
            chunks.Add(new QueryStreamChunk { Type = AgentStreamChunkType.Content, Content = $"c{i}" });

        var engine = new SteerableQueryEngine(chunks);
        await using var agent = new TestSteerAgent("任务", engine);

        var count = 0;
        await foreach (var chunk in agent.ExecuteStreamAsync(CancellationToken.None)) {
            count++;
            if (chunk.Type == AgentStreamChunkType.Complete)
                break;
        }

        // 5 content + 1 complete（AgentBase 收尾补发 — QueryEngine 无 Complete chunk 时 sawComplete=false 兜底）
        count.Should().Be(6);
    }

    /// <summary>可控 QueryEngine 假体 — 逐 chunk 慢速产出（每 chunk yield 后 Task.Yield 让测试线程置信号）。</summary>
    private sealed class SteerableQueryEngine(List<QueryStreamChunk> chunks) : IQueryEngine
    {
        public Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public async IAsyncEnumerable<QueryStreamChunk> QueryAsync(
            string userInput, MessageList chatHistory,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            foreach (var chunk in chunks) {
                await Task.Yield();
                yield return chunk;
            }
        }

        public IAsyncEnumerable<QueryStreamChunk> QueryAsync(
            string userInput, MessageList chatHistory, QueryOptions? options,
            CancellationToken cancellationToken = default)
            => QueryAsync(userInput, chatHistory, cancellationToken);

        public JoinCode.Abstractions.LLM.IQueryService GetChatCompletionService()
            => throw new NotSupportedException("测试假体不提供查询服务");

        public JoinCode.Abstractions.LLM.IChatClient GetKernel()
            => throw new NotSupportedException("测试假体不提供 kernel");
    }

    /// <summary>最小 TestAgent（AgentBase 直构造 — 无 spawn 管道依赖）。</summary>
    private sealed class TestSteerAgent(string task, IQueryEngine queryEngine)
        : AgentBase(task, options: null, queryEngine, logger: null, clock: null, name: "steer-test");
}
