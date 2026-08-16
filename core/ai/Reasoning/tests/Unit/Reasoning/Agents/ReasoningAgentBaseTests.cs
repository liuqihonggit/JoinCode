namespace JoinCode.Reasoning.Tests.Agents;

public sealed class ReasoningAgentBaseTests
{
    [Fact]
    public void ExtractJsonObject_ShouldReturnObjectBetweenBraces()
    {
        var text = "前缀 {\"key\":\"value\"} 后缀";

        var json = TestAgent.ExtractJsonObject(text);

        Assert.Equal("{\"key\":\"value\"}", json);
    }

    [Fact]
    public void ExtractJsonObject_ShouldReturnNullWhenNoBraces()
    {
        var text = "没有json内容";

        var json = TestAgent.ExtractJsonObject(text);

        Assert.Null(json);
    }

    [Fact]
    public void ExtractJsonObject_ShouldReturnNullWhenEndBeforeStart()
    {
        var text = "} 内容 {";

        var json = TestAgent.ExtractJsonObject(text);

        Assert.Null(json);
    }

    [Theory]
    [InlineData("DirectEvidence", TrustLevel.DirectEvidence)]
    [InlineData("StrongCorroboration", TrustLevel.StrongCorroboration)]
    [InlineData("Weak", TrustLevel.Weak)]
    [InlineData("Hearsay", TrustLevel.Hearsay)]
    [InlineData("Unreliable", TrustLevel.Unreliable)]
    [InlineData("Unknown", TrustLevel.Moderate)]
    [InlineData(null, TrustLevel.Moderate)]
    public void ParseTrustLevel_ShouldMapCorrectly(string? input, TrustLevel expected)
    {
        var actual = TestAgent.ParseTrustLevel(input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task CompressPromptIfNeededAsync_WhenContextManagerIsNull_ReturnsOriginal()
    {
        var context = CreateContext(maxPromptTokens: 10);
        var prompt = "这是一个很长的提示词";
        var agent = new TestAgent(NullLogger<TestAgent>.Instance);

        var result = await agent.CompressPromptIfNeededAsync(context, AgentRole.Prosecutor, prompt, CancellationToken.None);

        Assert.Same(prompt, result);
    }

    [Fact]
    public async Task CallLlmAsync_WhenChatClientIsNull_ReturnsNullAndZeroTokens()
    {
        var agent = new TestAgent(NullLogger<TestAgent>.Instance);

        var (content, usage, promptTokens) = await agent.CallLlmAsync("user prompt");

        Assert.Null(content);
        Assert.Null(usage);
        Assert.Equal(0, promptTokens);
    }

    [Fact]
    public async Task CallLlmAsync_WhenChatClientReturnsResponse_ReturnsContentAndUsage()
    {
        var agent = new TestAgent(
            NullLogger<TestAgent>.Instance,
            new FakeChatClient("assistant response"));

        var (content, usage, promptTokens) = await agent.CallLlmAsync("user prompt");

        Assert.Equal("assistant response", content);
        Assert.NotNull(usage);
        Assert.True(promptTokens > 0);
    }

    [Fact]
    public async Task CallLlmAsync_WhenChatClientThrows_ReturnsNullUsage()
    {
        var agent = new TestAgent(
            NullLogger<TestAgent>.Instance,
            new FakeChatClient(new InvalidOperationException("boom")));

        var (content, usage, promptTokens) = await agent.CallLlmAsync("user prompt");

        Assert.Null(content);
        Assert.Null(usage);
        Assert.True(promptTokens > 0);
    }

    [Fact]
    public async Task SendMessageAsync_WhenBrokerIsNull_DoesNothing()
    {
        var agent = new TestAgent(NullLogger<TestAgent>.Instance);

        await agent.SendAsync("to", "type", "content", CancellationToken.None);

        Assert.True(true);
    }

    [Fact]
    public async Task SendMessageAsync_WhenBrokerIsNotNull_SendsMessage()
    {
        var broker = new FakeMessageBroker();
        var agent = new TestAgent(NullLogger<TestAgent>.Instance, messageBroker: broker);

        await agent.SendAsync("defender", "evidence", "hello", CancellationToken.None);

        Assert.Single(broker.SentMessages);
        Assert.Equal("prosecutor", broker.SentMessages[0].FromAgentId);
        Assert.Equal("defender", broker.SentMessages[0].ToAgentId);
    }

    [Fact]
    public async Task BroadcastAsync_WhenBrokerIsNotNull_BroadcastsMessage()
    {
        var broker = new FakeMessageBroker();
        var agent = new TestAgent(NullLogger<TestAgent>.Instance, messageBroker: broker);

        await agent.BroadcastAsync("verdict", "done", CancellationToken.None);

        Assert.Single(broker.BroadcastMessages);
        Assert.Equal("broadcast", broker.BroadcastMessages[0].ToAgentId);
    }

    private static ReasoningContext CreateContext(int maxPromptTokens)
    {
        return new ReasoningContext
        {
            AllItems = [],
            AllEvidence = [],
            Dag = new Dag<ReasoningPayload>(),
            Options = new ReasoningOptions { MaxPromptTokens = maxPromptTokens },
        };
    }

    private sealed class TestAgent : ReasoningAgent
    {
        public override string SystemPrompt => "你是测试Agent";

        public TestAgent(ILogger logger, IChatClient? chatClient = null, IMailbox? messageBroker = null)
            : base(new FakeQueryEngine(), logger, AgentRole.Prosecutor, "测试Agent", chatClient, messageBroker) { }

        public override Task<AgentAction> ReasonAsync(ReasoningContext context, CancellationToken ct)
        {
            return System.Threading.Tasks.Task.FromResult(new AgentAction { AgentRole = Role });
        }

        public new static string? ExtractJsonObject(string content, ILogger? logger = null) => ReasoningAgent.ExtractJsonObject(content, logger);

        public new static TrustLevel ParseTrustLevel(string? value) => ReasoningAgent.ParseTrustLevel(value);

        public new Task<string> CompressPromptIfNeededAsync(ReasoningContext context, AgentRole role, string userPrompt, CancellationToken ct)
            => base.CompressPromptIfNeededAsync(context, role, userPrompt, ct);

        public new Task<(string? Content, TokenUsage? Usage, int EstimatedPromptTokens)> CallLlmAsync(string userPrompt, float temperature = 0.3f, int maxTokens = 2000, CancellationToken ct = default)
            => base.CallLlmAsync(userPrompt, temperature, maxTokens, ct);

        public new Task SendMessageAsync(string toAgentId, string messageType, string content, CancellationToken ct = default)
            => base.SendAsync(toAgentId, messageType, content, ct);

        public new Task BroadcastAsync(string messageType, string content, CancellationToken ct = default)
            => base.BroadcastAsync(messageType, content, ct);
    }
}
