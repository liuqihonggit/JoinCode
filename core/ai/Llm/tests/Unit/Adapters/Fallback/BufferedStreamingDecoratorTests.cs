namespace Llm.Tests.Adapters.Fallback;

public class BufferedStreamingDecoratorTests
{
    [Fact]
    public async Task GetApiMessageContentsAsync_CollectsStreamEventsIntoMessage()
    {
        var mock = new Mock<IQueryService>();
        mock.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Returns(ProduceStreamEventsAsync());

        var decorator = new BufferedStreamingDecorator(mock.Object);

        var result = await decorator.GetApiMessageContentsAsync(new MessageList());

        result.Should().HaveCount(1);
        result[0].Content.Should().Be("hello world");
        result[0].Role.Should().Be(MessageRole.Assistant);
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_DelegatesToInner()
    {
        var mock = new Mock<IQueryService>();
        mock.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Returns(ProduceStreamEventsAsync());

        var decorator = new BufferedStreamingDecorator(mock.Object);

        var events = new List<StreamEvent>();
        await foreach (var evt in decorator.GetStreamEventContentsAsync(new MessageList()))
        {
            events.Add(evt);
        }

        events.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetApiMessageContentsAsync_ExtractsUsageFromMetadata()
    {
        var usage = new TokenUsage(100, 50);
        var usageJson = JsonElementHelper.FromObject(usage, NativeJsonContext.Default.TokenUsage);

        var mock = new Mock<IQueryService>();
        mock.Setup(s => s.GetStreamEventContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(),
                It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Returns(ProduceStreamWithUsageAsync(usageJson));

        var decorator = new BufferedStreamingDecorator(mock.Object);

        var result = await decorator.GetApiMessageContentsAsync(new MessageList());

        result.Should().HaveCount(1);
        result[0].TokenUsage.Should().NotBeNull();
        result[0].TokenUsage!.PromptTokens.Should().Be(100);
        result[0].TokenUsage!.CompletionTokens.Should().Be(50);
    }

    private static async IAsyncEnumerable<StreamEvent> ProduceStreamEventsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(1, ct);
        yield return new StreamEvent(MessageRole.Assistant, "hello ", "test-model");
        yield return new StreamEvent(MessageRole.Assistant, "world", "test-model");
    }

    private static async IAsyncEnumerable<StreamEvent> ProduceStreamWithUsageAsync(
        JsonElement usageJson,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(1, ct);
        yield return new StreamEvent(MessageRole.Assistant, "response", "test-model",
            new Dictionary<string, JsonElement> { ["Usage"] = usageJson });
    }
}
