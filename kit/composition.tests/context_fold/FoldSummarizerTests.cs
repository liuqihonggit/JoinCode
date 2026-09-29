namespace Core.Tests.ContextFold;


public sealed class FoldSummarizerTests {
    private readonly Mock<IChatClient> _kernel;
    private readonly Mock<IQueryService> _queryService;

    public FoldSummarizerTests() {
        _kernel = new Mock<IChatClient>();
        _queryService = new Mock<IQueryService>();
        _kernel.Setup(k => k.GetChatCompletionService()).Returns(_queryService.Object);
    }

    private FoldSummarizer CreateSut() => new(_kernel.Object, NullLogger<FoldSummarizer>.Instance);

    [Fact]
    public async Task EmptyMessages_ReturnsEmpty() {
        var sut = CreateSut();
        var result = await sut.SummarizeForFoldAsync([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidMessages_LlmReturnsSummary_ReturnsSummary() {
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
    public async Task LlmReturnsEmpty_ThrowsInvalidOperationException() {
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
    public async Task LlmThrows_PropagatesException() {
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
    public async Task LlmReturnsMultiple_TakesFirst() {
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
    public async Task Cancellation_ThrowsOperationCanceledException() {
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

    // ===== BuildTranscript 确定性测试（internal static 纯计算） =====

    [Fact]
    public void BuildTranscript_EmptyList_ReturnsEmptyString() {
        var result = FoldSummarizer.BuildTranscript([]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildTranscript_SingleMessage_ReturnsFormattedLine() {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "hello"),
        };
        var result = FoldSummarizer.BuildTranscript(messages);
        result.Should().Be("[user]: hello");
    }

    [Fact]
    public void BuildTranscript_NullContent_ReturnsEmptyContentAfterRole() {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, null),
        };
        var result = FoldSummarizer.BuildTranscript(messages);
        result.Should().Be("[user]: ");
    }

    [Theory]
    [InlineData(MessageRole.System, "system")]
    [InlineData(MessageRole.User, "user")]
    [InlineData(MessageRole.Assistant, "assistant")]
    [InlineData(MessageRole.Tool, "tool")]
    public void BuildTranscript_AllRoles_ConvertsRoleCorrectly(MessageRole role, string expectedRoleValue) {
        var messages = new List<ApiMessage>
        {
            new(role, "content"),
        };
        var result = FoldSummarizer.BuildTranscript(messages);
        result.Should().Be($"[{expectedRoleValue}]: content");
    }

    [Fact]
    public void BuildTranscript_MultipleMessages_JoinsWithNewline() {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "first"),
            new(MessageRole.Assistant, "second"),
            new(MessageRole.User, "third"),
        };
        var result = FoldSummarizer.BuildTranscript(messages);
        result.Should().Be("[user]: first\n[assistant]: second\n[user]: third");
    }

    [Fact]
    public void BuildTranscript_MixedNullAndNonNullContent_HandlesGracefully() {
        var messages = new List<ApiMessage>
        {
            new(MessageRole.User, "has content"),
            new(MessageRole.Assistant, null),
        };
        var result = FoldSummarizer.BuildTranscript(messages);
        result.Should().Be("[user]: has content\n[assistant]: ");
    }
}