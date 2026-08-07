namespace Core.Tests.Context;

/// <summary>
/// ChatContextManager 冷恢复剪裁测试 — 对齐 Reasonix Go 版 maybeColdResumePrune：
/// 会话空闲超 vendor 缓存 TTL 时，服务端缓存已冷，重写前缀零额外 miss 成本，
/// 此时剪裁过期大工具结果来给全价首请求瘦身。
/// </summary>
public sealed class ChatContextManagerColdResumeTests
{
    private readonly Mock<IStateService> _stateService;
    private readonly Mock<ISessionMetaStore> _metaStore;
    private readonly Mock<JoinCode.Abstractions.Clock.IClockService> _clock;
    private readonly ILogger<ChatContextManager> _logger;
    private readonly DateTime _now = new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);

    public ChatContextManagerColdResumeTests()
    {
        _stateService = new Mock<IStateService>();
        _stateService.Setup(s => s.SaveStateAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _metaStore = new Mock<ISessionMetaStore>();
        _metaStore.Setup(m => m.SaveAsync(It.IsAny<string>(), It.IsAny<SessionMeta>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clock = new Mock<JoinCode.Abstractions.Clock.IClockService>();
        _clock.Setup(c => c.GetUtcNow()).Returns(_now);
        _clock.Setup(c => c.GetUtcNowOffset()).Returns(new DateTimeOffset(_now));
        _logger = NullLogger<ChatContextManager>.Instance;
    }

    private ChatContextManager CreateSut(string? providerBaseUrl = "https://api.deepseek.com")
    {
        var options = new ChatContextOptions
        {
            MetaStore = _metaStore.Object,
            SessionStats = new SessionStats(),
            SessionId = "s1",
            Clock = _clock.Object,
            ProviderBaseUrl = providerBaseUrl
        };
        return new ChatContextManager(_stateService.Object, _logger, options);
    }

    /// <summary>
    /// 构造带过期大工具结果的历史：静态前缀 + [Tool(100000 chars), User recent]。
    /// </summary>
    private void SetupHistoryWithStaleToolResult()
    {
        var savedHistory = new MessageList();
        var big = new StringBuilder();
        for (var i = 0; i < 100_000; i++)
            big.Append((char)('a' + i % 26));

        savedHistory.Add(new ApiMessage(MessageRole.Tool, big.ToString(),
            new Dictionary<string, JsonElement> { ["ToolName"] = JsonSerializer.SerializeToElement("bash") }));
        savedHistory.AddUserMessage("recent follow-up");

        _stateService.Setup(s => s.LoadStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(("static prefix", savedHistory));
    }

    [Fact]
    public async Task LoadContextAsync_IdleOverTtl_SnipsStaleToolResult()
    {
        SetupHistoryWithStaleToolResult();
        _metaStore.Setup(m => m.LoadAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionMeta
            {
                UpdatedAtUtcTicks = _now.AddHours(-25).Ticks
            });

        var sut = CreateSut();
        await sut.LoadContextAsync().ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        var toolMsg = history.First(m => m.Role == MessageRole.Tool);
        toolMsg.Content.Should().Contain("snipped", "idle past DeepSeek 24h TTL must snip stale tool result");
        _stateService.Verify(
            s => s.SaveStateAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce, "post-prune persist keeps saved file and prompt in sync");
    }

    [Fact]
    public async Task LoadContextAsync_IdleWithinTtl_DoesNotSnip()
    {
        SetupHistoryWithStaleToolResult();
        _metaStore.Setup(m => m.LoadAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionMeta
            {
                UpdatedAtUtcTicks = _now.AddHours(-1).Ticks
            });

        var sut = CreateSut();
        await sut.LoadContextAsync().ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        var toolMsg = history.First(m => m.Role == MessageRole.Tool);
        toolMsg.Content.Should().NotContain("snipped", "warm cache must be left untouched");
        _stateService.Verify(
            s => s.SaveStateAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadContextAsync_NoTimestamp_SkipsColdPruneConservatively()
    {
        SetupHistoryWithStaleToolResult();
        _metaStore.Setup(m => m.LoadAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionMeta { UpdatedAtUtcTicks = 0 });

        var sut = CreateSut();
        await sut.LoadContextAsync().ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        var toolMsg = history.First(m => m.Role == MessageRole.Tool);
        toolMsg.Content.Should().NotContain("snipped", "meta without timestamp must conservatively skip prune");
    }

    [Fact]
    public async Task LoadContextAsync_NoMetaStore_NoSnip()
    {
        SetupHistoryWithStaleToolResult();
        var options = new ChatContextOptions
        {
            SessionStats = new SessionStats(),
            SessionId = "s1",
            Clock = _clock.Object,
            ProviderBaseUrl = "https://api.deepseek.com"
        };
        var sut = new ChatContextManager(_stateService.Object, _logger, options);

        await sut.LoadContextAsync().ConfigureAwait(true);

        var history = await sut.GetMessageListAsync().ConfigureAwait(true);
        var toolMsg = history.First(m => m.Role == MessageRole.Tool);
        toolMsg.Content.Should().NotContain("snipped");
    }

    [Fact]
    public async Task SaveContextAsync_WritesUpdatedAtTicksFromClock()
    {
        var options = new ChatContextOptions
        {
            MetaStore = _metaStore.Object,
            SessionStats = new SessionStats(),
            SessionId = "s1",
            Clock = _clock.Object,
            ProviderBaseUrl = "https://api.deepseek.com"
        };
        var sut = new ChatContextManager(_stateService.Object, _logger, options);
        await sut.AddUserMessageAsync("hello").ConfigureAwait(true);

        await sut.SaveContextAsync().ConfigureAwait(true);

        _metaStore.Verify(
            m => m.SaveAsync("s1",
                It.Is<SessionMeta>(meta => meta.UpdatedAtUtcTicks == _now.Ticks),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
