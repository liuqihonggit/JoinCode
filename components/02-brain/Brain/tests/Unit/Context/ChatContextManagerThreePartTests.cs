
namespace Core.Tests.Context;

public partial class ChatContextManagerThreePartTests
{
    private readonly Mock<IStateService> _stateService;
    [Inject] private readonly ILogger<ChatContextManager> _logger;

    public ChatContextManagerThreePartTests()
    {
        _stateService = new Mock<IStateService>();

        _logger = NullLogger<ChatContextManager>.Instance;
    }

    private ChatContextManager CreateSut()
    {
        return new ChatContextManager(_stateService.Object, _logger);
    }

    [Fact]
    public async Task UpdateSystemPromptAsync_ShouldStoreAsStaticPrefix()
    {
        var sut = CreateSut();

        await sut.UpdateSystemPromptAsync("You are a helpful assistant.").ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        history.Should().HaveCount(1);
        history[0].Role.Should().Be(MessageRole.System);
        history[0].Content.Should().Be("You are a helpful assistant.");
        history[0].Metadata.Should().BeNull();
    }

    [Fact]
    public async Task AddDynamicSystemMessageAsync_ShouldHaveCacheBreakMetadata()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static").ConfigureAwait(true);

        await sut.AddDynamicSystemMessageAsync("dynamic context").ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        history.Should().HaveCount(2);
        history[0].Role.Should().Be(MessageRole.System);
        history[0].Content.Should().Be("static");
        history[0].Metadata.Should().BeNull();

        history[1].Role.Should().Be(MessageRole.System);
        history[1].Content.Should().Be("dynamic context");
        history[1].Metadata.Should().NotBeNull();
        history[1].Metadata!.ContainsKey("CacheBreak").Should().BeTrue();
        history[1].Metadata!["CacheBreak"].GetBoolean().Should().Be(true);
    }

    [Fact]
    public async Task AssembleMessages_Order_ShouldBe_Static_Dynamic_Conversation()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static system").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic info").ConfigureAwait(true);
        await sut.AddUserMessageAsync("hello").ConfigureAwait(true);
        await sut.AddAssistantMessageAsync("hi there").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("reminders").ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);

        history.Should().HaveCount(5);
        history[0].Role.Should().Be(MessageRole.System);
        history[0].Content.Should().Be("static system");
        history[0].Metadata.Should().BeNull();

        history[1].Role.Should().Be(MessageRole.System);
        history[1].Content.Should().Be("dynamic info");
        history[1].Metadata.Should().ContainKey("CacheBreak");

        history[2].Role.Should().Be(MessageRole.System);
        history[2].Content.Should().Be("reminders");
        history[2].Metadata.Should().ContainKey("CacheBreak");

        history[3].Role.Should().Be(MessageRole.User);
        history[3].Content.Should().Be("hello");

        history[4].Role.Should().Be(MessageRole.Assistant);
        history[4].Content.Should().Be("hi there");
    }

    [Fact]
    public async Task ClearDynamicSystemMessagesAsync_ShouldOnlyClearDynamic()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic1").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic2").ConfigureAwait(true);
        await sut.AddUserMessageAsync("hello").ConfigureAwait(true);

        await sut.ClearDynamicSystemMessagesAsync().ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        history.Should().HaveCount(2);
        history[0].Role.Should().Be(MessageRole.System);
        history[0].Content.Should().Be("static");
        history[1].Role.Should().Be(MessageRole.User);
        history[1].Content.Should().Be("hello");
    }

    [Fact]
    public async Task ClearMessagesAsync_ShouldPreserveStaticSystemPrompt()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.AddUserMessageAsync("hello").ConfigureAwait(true);
        await sut.AddAssistantMessageAsync("hi").ConfigureAwait(true);

        await sut.ClearMessagesAsync().ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        history.Should().HaveCount(1);
        history[0].Role.Should().Be(MessageRole.System);
        history[0].Content.Should().Be("static");
    }

    [Fact]
    public async Task ClearMessagesAsync_ShouldAlsoClearDynamicMessages()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.AddUserMessageAsync("hello").ConfigureAwait(true);

        await sut.ClearMessagesAsync().ConfigureAwait(true);

        await sut.AddUserMessageAsync("new message").ConfigureAwait(true);
        var history = await sut.GetMessageListAsync().ConfigureAwait(true);

        history.Should().HaveCount(2);
        history[0].Role.Should().Be(MessageRole.System);
        history[0].Content.Should().Be("static");
        history[1].Role.Should().Be(MessageRole.User);
        history[1].Content.Should().Be("new message");
    }

    [Fact]
    public async Task MultiTurn_DynamicMessagesShouldNotAccumulate()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static").ConfigureAwait(true);

        await sut.AddDynamicSystemMessageAsync("turn1 dynamic").ConfigureAwait(true);
        await sut.AddUserMessageAsync("turn1 user").ConfigureAwait(true);
        await sut.AddAssistantMessageAsync("turn1 assistant").ConfigureAwait(true);

        await sut.ClearDynamicSystemMessagesAsync().ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("turn2 dynamic").ConfigureAwait(true);
        await sut.AddUserMessageAsync("turn2 user").ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);

        history.Should().HaveCount(5);
        history[0].Content.Should().Be("static");
        history[0].Metadata.Should().BeNull();

        history[1].Content.Should().Be("turn2 dynamic");
        history[1].Metadata.Should().ContainKey("CacheBreak");

        history[2].Content.Should().Be("turn1 user");
        history[3].Content.Should().Be("turn1 assistant");
        history[4].Content.Should().Be("turn2 user");
    }

    [Fact]
    public async Task StaticPrefix_StableAcrossMultipleTurns()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("immutable system prompt").ConfigureAwait(true);

        for (int i = 0; i < 5; i++)
        {
            await sut.ClearDynamicSystemMessagesAsync().ConfigureAwait(true);
            await sut.AddDynamicSystemMessageAsync($"dynamic turn {i}").ConfigureAwait(true);
            await sut.AddUserMessageAsync($"user turn {i}").ConfigureAwait(true);
            await sut.AddAssistantMessageAsync($"assistant turn {i}").ConfigureAwait(true);
        }

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);

        history[0].Content.Should().Be("immutable system prompt");
        history[0].Metadata.Should().BeNull();

        for (int i = 0; i < 5; i++)
        {
            var dynamicIdx = 1;
            var userIdx = 2 + i * 3;
            var assistantIdx = 3 + i * 3;

            if (i == 4)
            {
                history[dynamicIdx].Content.Should().Be("dynamic turn 4");
            }
        }
    }

    [Fact]
    public async Task UpdateSystemPromptAsync_ShouldReplaceExistingStatic()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("old system").ConfigureAwait(true);
        await sut.AddUserMessageAsync("hello").ConfigureAwait(true);

        await sut.UpdateSystemPromptAsync("new system").ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        history[0].Content.Should().Be("new system");
        history[1].Content.Should().Be("hello");
    }

    [Fact]
    public async Task SaveContextAsync_ShouldPersistStaticAndConversationOnly()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.AddUserMessageAsync("hello").ConfigureAwait(true);

        await sut.SaveContextAsync().ConfigureAwait(true);

        _stateService.Verify(
            s => s.SaveStateAsync(
                "static",
                It.Is<MessageList>(h => h.Count == 1 && h[0].Role == MessageRole.User && h[0].Content == "hello"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LoadContextAsync_ShouldRestoreStaticAndConversation()
    {
        var savedHistory = new MessageList();
        savedHistory.AddUserMessage("restored user");
        savedHistory.AddAssistantMessage("restored assistant");

        _stateService
            .Setup(s => s.LoadStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(("restored static", savedHistory));

        var sut = CreateSut();
        await sut.LoadContextAsync().ConfigureAwait(true);

        await sut.AddDynamicSystemMessageAsync("new dynamic").ConfigureAwait(true);
        var history = await sut.GetMessageListAsync().ConfigureAwait(true);

        history[0].Content.Should().Be("restored static");
        history[0].Metadata.Should().BeNull();
        history[1].Content.Should().Be("new dynamic");
        history[1].Metadata.Should().ContainKey("CacheBreak");
        history[2].Content.Should().Be("restored user");
        history[3].Content.Should().Be("restored assistant");
    }

    [Fact]
    public async Task LoadContextAsync_ShouldExtractStaticFromHistoryIfNotProvided()
    {
        var savedHistory = new MessageList();
        savedHistory.AddSystemMessage("system from history");
        savedHistory.AddUserMessage("user msg");

        _stateService
            .Setup(s => s.LoadStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((string.Empty, savedHistory));

        var sut = CreateSut();
        await sut.LoadContextAsync().ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        history[0].Content.Should().Be("system from history");
        history[0].Metadata.Should().BeNull();
        history[1].Content.Should().Be("user msg");
    }

    [Fact]
    public async Task MultipleDynamicMessages_AllShouldHaveCacheBreak()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static").ConfigureAwait(true);

        await sut.AddDynamicSystemMessageAsync("memory section").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("environment info").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("reminders").ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);

        history[0].Metadata.Should().BeNull();
        history[1].Metadata!["CacheBreak"].GetBoolean().Should().Be(true);
        history[2].Metadata!["CacheBreak"].GetBoolean().Should().Be(true);
        history[3].Metadata!["CacheBreak"].GetBoolean().Should().Be(true);
    }

    [Fact]
    public async Task NoStaticPrompt_DynamicMessagesShouldStillWork()
    {
        var sut = CreateSut();

        await sut.AddDynamicSystemMessageAsync("dynamic only").ConfigureAwait(true);
        await sut.AddUserMessageAsync("hello").ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        history.Should().HaveCount(2);
        history[0].Content.Should().Be("dynamic only");
        history[0].Metadata.Should().ContainKey("CacheBreak");
    }

    [Fact]
    public async Task SameDynamicContent_SecondRequest_NoCacheBreak()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic context").ConfigureAwait(true);

        var firstHistory = await sut.GetMessageListAsync().ConfigureAwait(true);
        firstHistory[1].Metadata.Should().ContainKey("CacheBreak", "first request should have CacheBreak");

        var secondHistory = await sut.GetMessageListAsync().ConfigureAwait(true);
        secondHistory[1].Metadata.Should().BeNull("unchanged dynamic content should not have CacheBreak on second request");
    }

    [Fact]
    public async Task DynamicContentChanged_AfterStable_SecondRequestHasCacheBreak()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("static").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic v1").ConfigureAwait(true);

        var firstHistory = await sut.GetMessageListAsync().ConfigureAwait(true);
        firstHistory[1].Metadata.Should().ContainKey("CacheBreak");

        await sut.ClearDynamicSystemMessagesAsync().ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic v2").ConfigureAwait(true);

        var secondHistory = await sut.GetMessageListAsync().ConfigureAwait(true);
        secondHistory[1].Metadata.Should().ContainKey("CacheBreak", "changed dynamic content should have CacheBreak");
        secondHistory[1].Content.Should().Be("dynamic v2");
    }
}
