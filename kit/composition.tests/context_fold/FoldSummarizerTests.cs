namespace Core.Tests.ContextFold;


public sealed class FoldSummarizerTests
{
    private readonly Mock<IChatClient> _kernel;
    private readonly Mock<IQueryService> _queryService;

    public FoldSummarizerTests()
    {
        _kernel = new Mock<IChatClient>();
        _queryService = new Mock<IQueryService>();
        _kernel.Setup(k => k.GetChatCompletionService()).Returns(_queryService.Object);
    }

    private FoldSummarizer CreateSut() => new(_kernel.Object, NullLogger<FoldSummarizer>.Instance);

    [Fact]
    public async Task EmptyMessages_ReturnsEmpty()
    {
        var sut = CreateSut();
        var result = await sut.SummarizeForFoldAsync([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidMessages_LlmReturnsSummary_ReturnsSummary()
    {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "hello"),
            new(MessageRole.Assistant, "hi there"),
        };

        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage(MessageRole.Assistant, "摘要内容")]);

        var sut = CreateSut();
        var result = await sut.SummarizeForFoldAsync(messages);

        result.Should().Be("摘要内容");
    }

    [Fact]
    public async Task LlmReturnsEmpty_ThrowsInvalidOperationException()
    {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "短消息"),
        };

        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut();
        var act = async () => await sut.SummarizeForFoldAsync(messages);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LlmThrows_PropagatesException()
    {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "测试消息"),
        };

        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM 挂了"));

        var sut = CreateSut();
        var act = async () => await sut.SummarizeForFoldAsync(messages);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("LLM 挂了");
    }

    [Fact]
    public async Task LlmReturnsMultiple_TakesFirst()
    {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "input"),
        };

        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ApiMessage(MessageRole.Assistant, "第一条"),
                new ApiMessage(MessageRole.Assistant, "第二条"),
            ]);

        var sut = CreateSut();
        var result = await sut.SummarizeForFoldAsync(messages);

        result.Should().Be("第一条");
    }

    [Fact]
    public async Task Cancellation_ThrowsOperationCanceledException()
    {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "input"),
        };

        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = CreateSut();
        var act = async () => await sut.SummarizeForFoldAsync(messages);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
