namespace Core.Tests.Context;

/// <summary>
/// ChatContextManager 撤回操作集成测试 — 验证 /rewind 命令的三种模式：
/// 1. RewindLastTurnAsync (last) — 撤回最后一轮对话
/// 2. RewindToMessageIndexAsync (n) — 撤回到指定消息索引
/// 3. RewindToStartAsync (all) — 撤回全部对话
/// 同时验证撤回后剩余消息是原始消息的前缀（前缀缓存保持）
/// </summary>
public sealed class ChatContextManagerRewindTests
{
    private readonly Mock<IStateService> _stateService;
    private readonly ILogger<ChatContextManager> _logger;

    public ChatContextManagerRewindTests()
    {
        _stateService = new Mock<IStateService>();
        _stateService.Setup(s => s.SaveStateAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _logger = NullLogger<ChatContextManager>.Instance;
    }

    private ChatContextManager CreateSut() =>
        new(_stateService.Object, _logger);

    /// <summary>
    /// 构造包含多轮对话的 ChatContextManager：
    /// [User1, Assistant1, User2, Assistant2, User3, Assistant3]
    /// </summary>
    private static async Task<ChatContextManager> BuildMultiTurnContextAsync()
    {
        var sut = new ChatContextManager(new Mock<IStateService>().Object, NullLogger<ChatContextManager>.Instance);
        await sut.AddUserMessageAsync("用户消息1").ConfigureAwait(true);
        await sut.AddAssistantMessageAsync("助手回复1").ConfigureAwait(true);
        await sut.AddUserMessageAsync("用户消息2").ConfigureAwait(true);
        await sut.AddAssistantMessageAsync("助手回复2").ConfigureAwait(true);
        await sut.AddUserMessageAsync("用户消息3").ConfigureAwait(true);
        await sut.AddAssistantMessageAsync("助手回复3").ConfigureAwait(true);
        return sut;
    }

    // === RewindLastTurnAsync 测试 ===

    [Fact]
    public async Task RewindLastTurn_RemovesLastUserAndAssistant()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);

        var result = await sut.RewindLastTurnAsync().ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.RemovedCount.Should().Be(2);
        result.RemainingCount.Should().Be(4);
        result.Kind.Should().Be(RewindKind.TrimLastTurn);
    }

    [Fact]
    public async Task RewindLastTurn_EmptyHistory_ReturnsZeroRemoved()
    {
        var sut = CreateSut();

        var result = await sut.RewindLastTurnAsync().ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.RemovedCount.Should().Be(0);
        result.RemainingCount.Should().Be(0);
    }

    [Fact]
    public async Task RewindLastTurn_PreservesPrefixOfOriginalMessages()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);
        var originalMessages = await sut.GetMessageListAsync().ConfigureAwait(true);

        await sut.RewindLastTurnAsync().ConfigureAwait(true);

        var remainingMessages = await sut.GetMessageListAsync().ConfigureAwait(true);
        remainingMessages.Count.Should().Be(4);
        for (var i = 0; i < remainingMessages.Count; i++)
        {
            remainingMessages[i].Role.Should().Be(originalMessages[i].Role);
            remainingMessages[i].Content.Should().Be(originalMessages[i].Content);
        }
    }

    // === RewindToMessageIndexAsync 测试 ===

    [Fact]
    public async Task RewindToMessageIndex_RemovesMessagesAfterIndex()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);

        var result = await sut.RewindToMessageIndexAsync(3).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.RemovedCount.Should().Be(3);
        result.RemainingCount.Should().Be(3);
        result.Kind.Should().Be(RewindKind.TruncateToIndex);
    }

    [Fact]
    public async Task RewindToMessageIndex_Zero_RemovesAll()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);

        var result = await sut.RewindToMessageIndexAsync(0).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.RemovedCount.Should().Be(6);
        result.RemainingCount.Should().Be(0);
    }

    [Fact]
    public async Task RewindToMessageIndex_OutOfRange_ReturnsFail()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);

        var result = await sut.RewindToMessageIndexAsync(100).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RewindToMessageIndex_PreservesPrefixOfOriginalMessages()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);
        var originalMessages = await sut.GetMessageListAsync().ConfigureAwait(true);

        await sut.RewindToMessageIndexAsync(3).ConfigureAwait(true);

        var remainingMessages = await sut.GetMessageListAsync().ConfigureAwait(true);
        remainingMessages.Count.Should().Be(3);
        for (var i = 0; i < remainingMessages.Count; i++)
        {
            remainingMessages[i].Role.Should().Be(originalMessages[i].Role);
            remainingMessages[i].Content.Should().Be(originalMessages[i].Content);
        }
    }

    // === RewindToStartAsync 测试 ===

    [Fact]
    public async Task RewindToStart_RemovesAllMessages()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);

        var result = await sut.RewindToStartAsync().ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.RemovedCount.Should().Be(6);
        result.RemainingCount.Should().Be(0);
        result.Kind.Should().Be(RewindKind.ClearHistory);
    }

    [Fact]
    public async Task RewindToStart_EmptyHistory_ReturnsZeroRemoved()
    {
        var sut = CreateSut();

        var result = await sut.RewindToStartAsync().ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.RemovedCount.Should().Be(0);
        result.RemainingCount.Should().Be(0);
    }

    // === 连续撤回测试 ===

    [Fact]
    public async Task RewindLast_Twice_RemovesTwoTurns()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);

        await sut.RewindLastTurnAsync().ConfigureAwait(true);
        var result = await sut.RewindLastTurnAsync().ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.RemovedCount.Should().Be(2);
        result.RemainingCount.Should().Be(2);
    }

    [Fact]
    public async Task RewindLast_ThenRewindToStart_ClearsAll()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);

        await sut.RewindLastTurnAsync().ConfigureAwait(true);
        var result = await sut.RewindToStartAsync().ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.RemovedCount.Should().Be(4);
        result.RemainingCount.Should().Be(0);
    }

    // === 撤回后继续对话测试 ===

    [Fact]
    public async Task RewindLast_ThenAddNewMessage_NewMessageAppended()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);

        await sut.RewindLastTurnAsync().ConfigureAwait(true);
        await sut.AddUserMessageAsync("新的用户消息").ConfigureAwait(true);

        var messages = await sut.GetMessageListAsync().ConfigureAwait(true);
        messages.Count.Should().Be(5);
        messages[4].Content.Should().Be("新的用户消息");
        messages[4].Role.Should().Be(MessageRole.User);
    }

    [Fact]
    public async Task RewindToIndex_ThenAddNewMessage_NewMessageAppendedAtIndex()
    {
        var sut = await BuildMultiTurnContextAsync().ConfigureAwait(true);

        await sut.RewindToMessageIndexAsync(2).ConfigureAwait(true);
        await sut.AddAssistantMessageAsync("新的助手回复").ConfigureAwait(true);

        var messages = await sut.GetMessageListAsync().ConfigureAwait(true);
        messages.Count.Should().Be(3);
        messages[2].Content.Should().Be("新的助手回复");
        messages[2].Role.Should().Be(MessageRole.Assistant);
    }
}
