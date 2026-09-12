namespace Dream.Tests.Client;

/// <summary>
/// 聊天完成客户端单元测试
/// </summary>
public sealed class ChatCompletionClientTests
{
    [Fact]
    public void Constructor_NullKernel_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatCompletionClient(null!));
    }

    [Fact]
    public async Task GetCompletionAsync_WithSingleResult_ReturnsContent()
    {
        var kernel = new Mock<IChatClient>();
        var queryService = new Mock<IQueryService>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(queryService.Object);
        queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, "result") });

        var client = new ChatCompletionClient(kernel.Object);

        var result = await client.GetCompletionAsync(new MessageList()).ConfigureAwait(true);

        Assert.Equal("result", result);
    }

    [Fact]
    public async Task GetCompletionAsync_WithMultipleResults_ReturnsFirstContent()
    {
        var kernel = new Mock<IChatClient>();
        var queryService = new Mock<IQueryService>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(queryService.Object);
        queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage>
            {
                new(MessageRole.Assistant, "first"),
                new(MessageRole.Assistant, "second")
            });

        var client = new ChatCompletionClient(kernel.Object);

        var result = await client.GetCompletionAsync(new MessageList()).ConfigureAwait(true);

        Assert.Equal("first", result);
    }

    [Fact]
    public async Task GetCompletionAsync_WithEmptyResults_ReturnsEmptyString()
    {
        var kernel = new Mock<IChatClient>();
        var queryService = new Mock<IQueryService>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(queryService.Object);
        queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage>());

        var client = new ChatCompletionClient(kernel.Object);

        var result = await client.GetCompletionAsync(new MessageList()).ConfigureAwait(true);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task GetCompletionAsync_WithNullContent_ReturnsEmptyString()
    {
        var kernel = new Mock<IChatClient>();
        var queryService = new Mock<IQueryService>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(queryService.Object);
        queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, null) });

        var client = new ChatCompletionClient(kernel.Object);

        var result = await client.GetCompletionAsync(new MessageList()).ConfigureAwait(true);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task GetCompletionAsync_PassesCancellationToken()
    {
        var kernel = new Mock<IChatClient>();
        var queryService = new Mock<IQueryService>();
        kernel.Setup(k => k.GetChatCompletionService()).Returns(queryService.Object);
        using var cts = new CancellationTokenSource();
        queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), null, null, cts.Token))
            .ReturnsAsync(new List<ApiMessage> { new(MessageRole.Assistant, "ok") });

        var client = new ChatCompletionClient(kernel.Object);

        var result = await client.GetCompletionAsync(new MessageList(), cts.Token).ConfigureAwait(true);

        Assert.Equal("ok", result);
    }
}
