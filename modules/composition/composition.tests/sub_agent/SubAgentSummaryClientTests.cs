// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Tests.SubAgent;

/// <summary>
/// SubAgentSummaryClient 确定性测试 — BuildSystemPrompt 纯字符串构建 + SummarizeAsync 用 Mock IChatClient 消除 LLM IO。
/// </summary>
public sealed class SubAgentSummaryClientTests {
    private readonly Mock<IChatClient> _kernel;
    private readonly Mock<IQueryService> _queryService;

    public SubAgentSummaryClientTests() {
        _kernel = new Mock<IChatClient>();
        _queryService = new Mock<IQueryService>();
        _kernel.Setup(k => k.GetChatCompletionService()).Returns(_queryService.Object);
    }

    private SubAgentSummaryClient CreateSut() => new(_kernel.Object, NullLogger<SubAgentSummaryClient>.Instance);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(4096)]
    [InlineData(int.MaxValue)]
    public void BuildSystemPrompt_PositiveOrZero_ContainsTokenValue(int maxOutputTokens) {
        var prompt = SubAgentSummaryClient.BuildSystemPrompt(maxOutputTokens);
        prompt.Should().Contain($"不超过 {maxOutputTokens} token");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void BuildSystemPrompt_Negative_ContainsNegativeValue(int maxOutputTokens) {
        var prompt = SubAgentSummaryClient.BuildSystemPrompt(maxOutputTokens);
        prompt.Should().Contain($"不超过 {maxOutputTokens} token");
    }

    [Fact]
    public void BuildSystemPrompt_AlwaysContainsKeyPhrase() {
        var prompt = SubAgentSummaryClient.BuildSystemPrompt(100);
        prompt.Should().Contain("摘要助手");
        prompt.Should().Contain("保留关键信息");
        prompt.Should().Contain("直接输出摘要内容");
    }

    [Fact]
    public void BuildSystemPrompt_DifferentValues_ProduceDifferentPrompts() {
        var prompt1 = SubAgentSummaryClient.BuildSystemPrompt(100);
        var prompt2 = SubAgentSummaryClient.BuildSystemPrompt(200);
        prompt1.Should().NotBe(prompt2);
    }

    [Fact]
    public void BuildSystemPrompt_SameValue_ProducesSamePrompt() {
        var prompt1 = SubAgentSummaryClient.BuildSystemPrompt(500);
        var prompt2 = SubAgentSummaryClient.BuildSystemPrompt(500);
        prompt1.Should().Be(prompt2);
    }

    // ===== SummarizeAsync 确定性测试（Mock IChatClient 消除 LLM IO） =====

    [Fact]
    public async Task SummarizeAsync_NullText_ReturnsNullWithoutCallingLlm() {
        await using var sut = CreateSut();
        var result = await sut.SummarizeAsync(null!, "agent1", 100).ConfigureAwait(true);

        result.Should().BeNull();
        _queryService.Verify(q => q.GetApiMessageContentsAsync(
            It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SummarizeAsync_EmptyText_ReturnsNullWithoutCallingLlm() {
        await using var sut = CreateSut();
        var result = await sut.SummarizeAsync(string.Empty, "agent1", 100).ConfigureAwait(true);

        result.Should().BeNull();
        _queryService.Verify(q => q.GetApiMessageContentsAsync(
            It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SummarizeAsync_ValidText_LlmReturnsSummary_ReturnsSummary() {
        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage(MessageRole.Assistant, "这是摘要")]);

        await using var sut = CreateSut();
        var result = await sut.SummarizeAsync("需要摘要的文本", "agent1", 100).ConfigureAwait(true);

        result.Should().Be("这是摘要");
    }

    [Fact]
    public async Task SummarizeAsync_LlmReturnsEmptyContent_ReturnsNull() {
        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage(MessageRole.Assistant, "")]);

        await using var sut = CreateSut();
        var result = await sut.SummarizeAsync("文本", "agent1", 100).ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_LlmReturnsNullContent_ReturnsNull() {
        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage(MessageRole.Assistant, null)]);

        await using var sut = CreateSut();
        var result = await sut.SummarizeAsync("文本", "agent1", 100).ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_LlmReturnsEmptyList_ReturnsNull() {
        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await using var sut = CreateSut();
        var result = await sut.SummarizeAsync("文本", "agent1", 100).ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_LlmReturnsMultiple_TakesFirst() {
        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ApiMessage(MessageRole.Assistant, "第一条摘要"),
                new ApiMessage(MessageRole.Assistant, "第二条摘要"),
            ]);

        await using var sut = CreateSut();
        var result = await sut.SummarizeAsync("文本", "agent1", 100).ConfigureAwait(true);

        result.Should().Be("第一条摘要");
    }

    [Fact]
    public async Task SummarizeAsync_LlmThrowsInvalidOperationException_ReturnsNull() {
        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM 故障"));

        await using var sut = CreateSut();
        var result = await sut.SummarizeAsync("文本", "agent1", 100).ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SummarizeAsync_LlmThrowsOperationCanceledException_Propagates() {
        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await using var sut = CreateSut();
        var act = async () => await sut.SummarizeAsync("文本", "agent1", 100).ConfigureAwait(true);
        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SummarizeAsync_PassesCorrectChatHistoryToLlm() {
        MessageList? capturedHistory = null;
        _queryService
            .Setup(q => q.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions?>(), It.IsAny<IChatClient?>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions?, IChatClient?, CancellationToken>((history, _, _, _) => capturedHistory = history)
            .ReturnsAsync([new ApiMessage(MessageRole.Assistant, "摘要")]);

        await using var sut = CreateSut();
        await sut.SummarizeAsync("用户输入文本", "agent1", 256).ConfigureAwait(true);

        capturedHistory.Should().NotBeNull();
        capturedHistory!.Count.Should().Be(2);
        capturedHistory[0].Role.Should().Be(MessageRole.System);
        capturedHistory[0].Content.Should().Contain("不超过 256 token");
        capturedHistory[1].Role.Should().Be(MessageRole.User);
        capturedHistory[1].Content.Should().Be("用户输入文本");
    }

    [Fact]
    public async Task SummarizeAsync_NullKernel_ThrowsArgumentNullException() {
        var act = () => new SubAgentSummaryClient(null!, NullLogger<SubAgentSummaryClient>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("kernel");
    }
}
