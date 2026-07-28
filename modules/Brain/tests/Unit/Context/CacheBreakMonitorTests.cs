
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
        new(_stateService.Object, _logger);

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

        var usageWithHit = new TokenUsage(100, 50) { CacheReadInputTokens = 80, CacheCreationInputTokens = 0 };
        await sut.CheckCacheBreakAsync(snapshot, usageWithHit).ConfigureAwait(true);

        var usageWithMiss = new TokenUsage(100, 50) { CacheReadInputTokens = 0, CacheCreationInputTokens = 100 };
        var result = await sut.CheckCacheBreakAsync(snapshot, usageWithMiss).ConfigureAwait(true);

        result.BreakDetected.Should().BeTrue();
        result.Kind.Should().Be(CacheBreakKind.CacheEviction);
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
}
