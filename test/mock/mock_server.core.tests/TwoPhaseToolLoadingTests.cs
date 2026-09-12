namespace MockServer.Core.Tests;

/// <summary>
/// 两阶段工具加载测试 — TS 原版风格的惰性工具描述加载
///
/// 流程:
/// 1. 首次请求: 客户端发送 tool_groups(只有分组,不含完整 schema)
/// 2. 服务端 LLM 询问: 返回 tool_description_request(请求特定工具的描述)
/// 3. 客户端返回: 发送第二次请求,含 tool_descriptions(完整定义)
/// 4. 服务端执行: 收到描述后加入 tools map,返回工具调用
///
/// 目标: 首次请求不含 304 个工具的完整 schema,只含分组列表
/// </summary>
public sealed class TwoPhaseToolLoadingTests
{
    [Fact]
    public void BuildToolDescriptionRequest_WithToolCalls_ReturnsRequestedTools()
    {
        var turns = new List<ScriptedTurn>
        {
            new() { TextResponse = "I will read a file.", ToolCalls = [new ToolCallConfig { ToolName = "read", Arguments = "{}" }] }
        };
        var strategy = new TestableStrategy(turns);
        strategy.OnRequestStarted(JsonDocument.Parse("{}").RootElement.Clone());

        var result = strategy.BuildToolDescriptionRequest(JsonDocument.Parse("""{"tool_groups":[]}""").RootElement.Clone());

        result.Should().NotBeNull();
        result.Should().Contain("tool_description_request");
        result.Should().Contain("\"read\"");
    }

    [Fact]
    public void BuildToolDescriptionRequest_NoToolCalls_ReturnsNull()
    {
        var turns = new List<ScriptedTurn>
        {
            new() { TextResponse = "Hello!" }
        };
        var strategy = new TestableStrategy(turns);
        strategy.OnRequestStarted(JsonDocument.Parse("{}").RootElement.Clone());

        var result = strategy.BuildToolDescriptionRequest(JsonDocument.Parse("""{"tool_groups":[]}""").RootElement.Clone());

        result.Should().BeNull();
    }

    [Fact]
    public void BuildToolDescriptionRequest_MultipleToolCalls_ReturnsAllDistinctTools()
    {
        var turns = new List<ScriptedTurn>
        {
            new()
            {
                TextResponse = "Reading and writing.",
                ToolCalls = [
                    new ToolCallConfig { ToolName = "read", Arguments = "{}" },
                    new ToolCallConfig { ToolName = "write", Arguments = "{}" },
                    new ToolCallConfig { ToolName = "read", Arguments = "{}" }
                ]
            }
        };
        var strategy = new TestableStrategy(turns);
        strategy.OnRequestStarted(JsonDocument.Parse("{}").RootElement.Clone());

        var result = strategy.BuildToolDescriptionRequest(JsonDocument.Parse("""{"tool_groups":[]}""").RootElement.Clone());

        result.Should().NotBeNull();
        result.Should().Contain("\"read\"");
        result.Should().Contain("\"write\"");
        var readCount = result!.Split("\"read\"").Length - 1;
        readCount.Should().Be(1, "重复工具应去重,只请求一次");
    }

    [Fact]
    public void BuildToolDescriptionRequest_DefaultImplementation_ReturnsNull()
    {
        IResponseStrategy strategy = new DefaultStrategy();

        var result = strategy.BuildToolDescriptionRequest(JsonDocument.Parse("""{"tool_groups":[]}""").RootElement.Clone());

        result.Should().BeNull();
    }

    private sealed class TestableStrategy : ScriptedResponseStrategyBase
    {
        public TestableStrategy(List<ScriptedTurn>? turns) : base(turns, "default") { }

        public override string BuildResponse(JsonElement request, CacheStats cacheStats) => "{}";
        public override string BuildStreamChunk(string id, string content, bool isLast) => "";
        public override string? BuildStreamPreamble(string id) => null;
        public override string BuildToolCallResponse(JsonElement request, CacheStats cacheStats) => "{}";
        public override string BuildStreamToolCallResponse(string id, CacheStats cacheStats) => "";
        public override string BuildStreamThinkingResponse(string id) => "";
    }

    private sealed class DefaultStrategy : IResponseStrategy
    {
        public string BuildResponse(JsonElement request, CacheStats cacheStats) => "{}";
        public bool SupportsStreaming => true;
        public string BuildStreamChunk(string id, string content, bool isLast) => "";
    }
}
