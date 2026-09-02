
namespace Core.Tests.Context;

public partial class CacheBreakMonitorTests
{
    private readonly Mock<IStateService> _stateService;
    [Inject] private readonly ILogger<ChatContextManager> _logger;

    public CacheBreakMonitorTests()
    {
        _stateService = new Mock<IStateService>();

        _logger = NullLogger<ChatContextManager>.Instance;
    }

    private ChatContextManager CreateSut() =>
        new(_stateService.Object, _logger, new ChatContextOptions
        {
            ContextWindowResolver = new FixedWindowResolver(1000)
        });

    private sealed class FixedWindowResolver(int window) : IContextWindowResolver
    {
        public int ResolveCurrentContextWindow() => window;
    }

    [Fact]
    public async Task RecordPromptStateAsync_CapturesCurrentState()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic context").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        snapshot.Should().NotBeNull();
        snapshot.SystemPromptHash.Should().NotBeNullOrEmpty();
        snapshot.ToolSpecsHash.Should().NotBeNullOrEmpty();
        snapshot.ToolCount.Should().Be(1);
        snapshot.DynamicContentHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckCacheBreakAsync_SameState_NoBreak()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 80 };

        var result = await sut.CheckCacheBreakAsync(snapshot, usage).ConfigureAwait(true);

        result.BreakDetected.Should().BeFalse();
        result.Kind.Should().Be(CacheBreakKind.None);
    }

    [Fact]
    public async Task CheckCacheBreakAsync_SystemPromptChanged_SystemBreak()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system v1").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        await sut.UpdateSystemPromptAsync("system v2").ConfigureAwait(true);

        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 80 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usage).ConfigureAwait(true);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.SystemPromptChanged);
    }

    [Fact]
    public async Task CheckCacheBreakAsync_DynamicContentChanged_DynamicBreak()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic v1").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        await sut.ClearDynamicSystemMessagesAsync().ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic v2").ConfigureAwait(true);

        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 50, CacheCreationInputTokens = 30 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usage).ConfigureAwait(true);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.DynamicContentChanged);
    }

    [Fact]
    public async Task CheckCacheBreakAsync_ToolSpecsChanged_ToolBreak()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a_modified")]).ConfigureAwait(true);

        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 80 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usage).ConfigureAwait(true);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
    }

    [Fact]
    public async Task CheckCacheBreakAsync_ToolAppendNotBreak_IfCacheStillHit()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        await sut.UpdateToolSpecsAsync(
        [
            new ToolSpec("tool_a", "desc_a"),
            new ToolSpec("tool_b", "desc_b")
        ]).ConfigureAwait(true);

        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 80, CacheCreationInputTokens = 10 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usage).ConfigureAwait(true);

        result.BreakDetected.Should().BeFalse();
        result.Kind.Should().Be(CacheBreakKind.None);
    }

    [Fact]
    public async Task CheckCacheBreakAsync_CacheEviction_Detected()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        var usageWithHit = new TokenUsage(10000, 50) { CacheReadInputTokens = 10000, CacheCreationInputTokens = 0 };
        await sut.CheckCacheBreakAsync(snapshot, usageWithHit).ConfigureAwait(true);

        var usageWithMiss = new TokenUsage(10000, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usageWithMiss).ConfigureAwait(true);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.ServerSideRouting);
    }

    [Fact]
    public async Task CheckCacheBreakAsync_CacheEviction_PartialDrop_Detected()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        var usageWithHit = new TokenUsage(10000, 50) { CacheReadInputTokens = 10000, CacheCreationInputTokens = 0 };
        await sut.CheckCacheBreakAsync(snapshot, usageWithHit).ConfigureAwait(true);

        var usageWithPartialMiss = new TokenUsage(10000, 50) { CacheReadInputTokens = 2000, CacheCreationInputTokens = 8000 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usageWithPartialMiss).ConfigureAwait(true);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.ServerSideRouting);
    }

    [Fact]
    public async Task CheckCacheBreakAsync_CacheEviction_SmallRelativeDrop_NotReported()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        var usageWithHit = new TokenUsage(10000, 50) { CacheReadInputTokens = 10000, CacheCreationInputTokens = 0 };
        await sut.CheckCacheBreakAsync(snapshot, usageWithHit).ConfigureAwait(true);

        var usageWithSmallDrop = new TokenUsage(10000, 50) { CacheReadInputTokens = 9600, CacheCreationInputTokens = 400 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usageWithSmallDrop).ConfigureAwait(true);

        result.BreakDetected.Should().BeFalse();
    }

    [Fact]
    public async Task CheckCacheBreakAsync_CacheEviction_DropBelowAbsoluteThreshold_NotReported()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        var usageWithHit = new TokenUsage(3000, 50) { CacheReadInputTokens = 3000, CacheCreationInputTokens = 0 };
        await sut.CheckCacheBreakAsync(snapshot, usageWithHit).ConfigureAwait(true);

        var usageWithSmallAbsoluteDrop = new TokenUsage(3000, 50) { CacheReadInputTokens = 1500, CacheCreationInputTokens = 1500 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usageWithSmallAbsoluteDrop).ConfigureAwait(true);

        result.BreakDetected.Should().BeFalse();
    }

    [Fact]
    public void CheckCacheBreak_HaikuModel_Skipped()
    {
        var detector = new CacheBreakDetector();
        var prefix = new ImmutablePrefix("system v1", [new ToolSpec("tool_a", "desc_a")], []);
        var snapshot = detector.RecordPromptState(prefix, "dynamic", modelId: "claude-3-haiku");

        var changedPrefix = new ImmutablePrefix("system v2", [new ToolSpec("tool_a", "desc_a")], []);
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var result = detector.CheckCacheBreak(snapshot, changedPrefix, "dynamic", usage, currentModelId: "claude-3-haiku");

        result.BreakDetected.Should().BeFalse();
    }

    [Fact]
    public void CheckCacheBreak_Ttl5Min_Detected()
    {
        var time = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        var detector = new CacheBreakDetector(() => time);
        var prefix = new ImmutablePrefix("system", [new ToolSpec("tool_a", "desc_a")], []);
        var snapshot = detector.RecordPromptState(prefix, "dynamic");

        var usageWithHit = new TokenUsage(10000, 50) { CacheReadInputTokens = 10000, CacheCreationInputTokens = 0 };
        detector.CheckCacheBreak(snapshot, prefix, "dynamic", usageWithHit);

        time = time.AddMinutes(6);
        var usageWithMiss = new TokenUsage(10000, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 10000 };
        var result = detector.CheckCacheBreak(snapshot, prefix, "dynamic", usageWithMiss);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.TtlExpiration5Min);
    }

    [Fact]
    public void CheckCacheBreak_Ttl1Hour_Detected()
    {
        var time = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        var detector = new CacheBreakDetector(() => time);
        var prefix = new ImmutablePrefix("system", [new ToolSpec("tool_a", "desc_a")], []);
        var snapshot = detector.RecordPromptState(prefix, "dynamic");

        var usageWithHit = new TokenUsage(10000, 50) { CacheReadInputTokens = 10000, CacheCreationInputTokens = 0 };
        detector.CheckCacheBreak(snapshot, prefix, "dynamic", usageWithHit);

        time = time.AddMinutes(61);
        var usageWithMiss = new TokenUsage(10000, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 10000 };
        var result = detector.CheckCacheBreak(snapshot, prefix, "dynamic", usageWithMiss);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.TtlExpiration1Hour);
    }

    [Fact]
    public void CheckCacheBreak_ServerSideRouting_Detected()
    {
        var time = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        var detector = new CacheBreakDetector(() => time);
        var prefix = new ImmutablePrefix("system", [new ToolSpec("tool_a", "desc_a")], []);
        var snapshot = detector.RecordPromptState(prefix, "dynamic");

        var usageWithHit = new TokenUsage(10000, 50) { CacheReadInputTokens = 10000, CacheCreationInputTokens = 0 };
        detector.CheckCacheBreak(snapshot, prefix, "dynamic", usageWithHit);

        time = time.AddMinutes(2);
        var usageWithMiss = new TokenUsage(10000, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 10000 };
        var result = detector.CheckCacheBreak(snapshot, prefix, "dynamic", usageWithMiss);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.ServerSideRouting);
    }

    [Fact]
    public void CheckCacheBreak_NotifyCacheDeletion_SuppressesBreak()
    {
        var time = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        var detector = new CacheBreakDetector(() => time);
        var prefix = new ImmutablePrefix("system", [new ToolSpec("tool_a", "desc_a")], []);
        var snapshot = detector.RecordPromptState(prefix, "dynamic");

        var usageWithHit = new TokenUsage(10000, 50) { CacheReadInputTokens = 10000, CacheCreationInputTokens = 0 };
        detector.CheckCacheBreak(snapshot, prefix, "dynamic", usageWithHit);

        detector.NotifyCacheDeletion();

        time = time.AddMinutes(1);
        var usageAfterDeletion = new TokenUsage(10000, 50) { CacheReadInputTokens = 2000, CacheCreationInputTokens = 8000 };
        var result = detector.CheckCacheBreak(snapshot, prefix, "dynamic", usageAfterDeletion);

        result.BreakDetected.Should().BeFalse("cache deletion is expected, not a break");
    }

    [Fact]
    public void CheckCacheBreak_McpToolName_SanitizedInToolDrift()
    {
        var detector = new CacheBreakDetector();
        var prefix1 = new ImmutablePrefix("system", [new ToolSpec("mcp__filesystem_read", "desc_v1")], []);
        var snapshot = detector.RecordPromptState(prefix1, "dynamic");

        var prefix2 = new ImmutablePrefix("system", [new ToolSpec("mcp__filesystem_read", "desc_v2")], []);
        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var result = detector.CheckCacheBreak(snapshot, prefix2, "dynamic", usage);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.ToolSpecsChanged);
        result.ToolDrift!.EditedNames.Should().ContainSingle().Which.Should().Be("mcp");
    }

    [Fact]
    public async Task RecordPromptStateAsync_NoToolSpecs_StillWorks()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system prompt").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        snapshot.Should().NotBeNull();
        snapshot.ToolCount.Should().Be(0);
        snapshot.ToolSpecsHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UpdateToolSpecsAsync_ReplacesExistingSpecs()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot1 = await sut.RecordPromptStateAsync().ConfigureAwait(true);
        snapshot1.ToolCount.Should().Be(1);

        await sut.UpdateToolSpecsAsync(
        [
            new ToolSpec("tool_a", "desc_a"),
            new ToolSpec("tool_b", "desc_b")
        ]).ConfigureAwait(true);

        var snapshot2 = await sut.RecordPromptStateAsync().ConfigureAwait(true);
        snapshot2.ToolCount.Should().Be(2);
        snapshot2.SystemPromptHash.Should().Be(snapshot1.SystemPromptHash);
    }

    [Fact]
    public async Task CheckCacheBreakAsync_Priority_SystemOverToolOverDynamic()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("system v1").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic v1").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

        await sut.UpdateSystemPromptAsync("system v2").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a_modified")]).ConfigureAwait(true);
        await sut.ClearDynamicSystemMessagesAsync().ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic v2").ConfigureAwait(true);

        var usage = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usage).ConfigureAwait(true);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.SystemPromptChanged);
    }

    [Fact]
    public async Task FullPipeline_RecordCheck_RecordAgain_NoBreak()
    {
        var sut = CreateSut();
        await sut.UpdateSystemPromptAsync("stable system").ConfigureAwait(true);
        await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
        await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

        var snapshot1 = await sut.RecordPromptStateAsync().ConfigureAwait(true);
        var usage1 = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var result1 = await sut.CheckCacheBreakAsync(snapshot1, usage1).ConfigureAwait(true);
        result1.Kind.Should().Be(CacheBreakKind.None, "first request with no prior cache hit should not be CacheEviction");

        var snapshot2 = await sut.RecordPromptStateAsync().ConfigureAwait(true);
        var usage2 = new TokenUsage(100, 50) { CacheReadInputTokens = 80, CacheCreationInputTokens = 0 };
        var result2 = await sut.CheckCacheBreakAsync(snapshot2, usage2).ConfigureAwait(true);
        result2.BreakDetected.Should().BeFalse();
    }

    [Fact]
    public async Task DecideAfterUsage_AfterNoProgressFolds_PausesFolding()
    {
        var sut = CreateSut();
        var usage = new TokenUsage(600, 0);

        sut.DecideAfterUsage(usage).Should().Be(ContextFoldDecision.FoldNormal);
        await sut.FoldIfNeededAsync(ContextFoldDecision.FoldNormal).ConfigureAwait(true);
        await sut.FoldIfNeededAsync(ContextFoldDecision.FoldNormal).ConfigureAwait(true);

        sut.DecideAfterUsage(usage).Should().Be(ContextFoldDecision.None);
    }

    [Fact]
    public async Task DecideAfterUsage_SuccessfulFold_ResetsStuckGuard()
    {
        var executor = new ContextFoldExecutor(new StubSummarizer());
        var sut = new ChatContextManager(
            _stateService.Object,
            _logger,
            new ChatContextOptions
            {
                FoldExecutor = executor,
                ContextWindowResolver = new FixedWindowResolver(1000)
            });
        var usage = new TokenUsage(600, 0);

        sut.DecideAfterUsage(usage).Should().Be(ContextFoldDecision.FoldNormal);
        await sut.FoldIfNeededAsync(ContextFoldDecision.FoldNormal).ConfigureAwait(true);
        await sut.FoldIfNeededAsync(ContextFoldDecision.FoldNormal).ConfigureAwait(true);

        sut.DecideAfterUsage(usage).Should().Be(ContextFoldDecision.None);

        await sut.AddAssistantToolCallMessageAsync(null, new Dictionary<string, JsonElement>
        {
            ["ToolCalls"] = JsonSerializer.SerializeToElement("[]")
        }).ConfigureAwait(true);

        var result = await sut.FoldIfNeededAsync(ContextFoldDecision.ExitWithSummary).ConfigureAwait(true);
        result.Folded.Should().BeTrue();

        sut.DecideAfterUsage(usage).Should().Be(ContextFoldDecision.FoldNormal);
    }

    private sealed class StubSummarizer : IFoldSummarizer
    {
        public Task<string> SummarizeForFoldAsync(
            IReadOnlyList<ApiMessage> headMessages,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("[summary]");
    }
}
