namespace Pipelines.Tests;

/// <summary>
/// ChatTimingMiddleware 确定性单元测试 — 用 internal 构造函数注入 debugLog 开关，
/// 验证 _debugLog=false 仅转发、_debugLog=true 追加 TimingSummary。
/// 无 IO/时序依赖：TimingSummary 内容由 ChatTiming.FormatSummary 确定性生成，
/// Stopwatch 数值不断言具体数字，仅断言结构标记。
/// </summary>
public sealed class ChatTimingMiddlewareTest {
    // === 构造函数 ===

    [Fact]
    public async Task Constructor_Default_ReadsDiagIsDebugLogWithoutThrowing() {
        // public 无参构造函数从 Diag.IsDebugLog 读取 — 验证构造不抛异常
        // 确定性：不依赖具体 IsDebugLog 值，仅验证构造成功
        await using var mw = new ChatTimingMiddleware();
        mw.Should().NotBeNull();
    }

    // === InvokeAsync: _debugLog=false ===

    [Fact]
    public async Task InvokeAsync_DebugLogFalse_PassesThroughWithoutTimingSummary() {
        await using var mw = new ChatTimingMiddleware(debugLog: false);
        var ctx = TestHelpers.NewContext();
        var events = new[] { ChatStreamEvent.Text("a"), ChatStreamEvent.Done() };

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync(events, ct), default));

        result.Should().HaveCount(2);
        result[0].Content.Should().Be("a");
        result[1].Type.Should().Be(ChatStreamEventType.Complete);
        result.Should().NotContain(e => e.Type == ChatStreamEventType.TimingSummary);
    }

    // === InvokeAsync: _debugLog=true ===

    [Fact]
    public async Task InvokeAsync_DebugLogTrue_AppendsTimingSummaryAfterNextEvents() {
        await using var mw = new ChatTimingMiddleware(debugLog: true);
        var ctx = TestHelpers.NewContext();
        var events = new[] { ChatStreamEvent.Text("a"), ChatStreamEvent.Done() };

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync(events, ct), default));

        result.Should().HaveCount(3);
        result[0].Content.Should().Be("a");
        result[1].Type.Should().Be(ChatStreamEventType.Complete);
        result[2].Type.Should().Be(ChatStreamEventType.TimingSummary);
        result[2].Content.Should().Contain("[Timing]");
    }

    [Fact]
    public async Task InvokeAsync_DebugLogTrue_EmptyNext_StillAppendsTimingSummary() {
        await using var mw = new ChatTimingMiddleware(debugLog: true);
        var ctx = TestHelpers.NewContext();

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync([], ct), default));

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(ChatStreamEventType.TimingSummary);
    }

    [Fact]
    public async Task InvokeAsync_DebugLogTrue_WithFinalUsage_TimingSummaryContainsCacheStats() {
        await using var mw = new ChatTimingMiddleware(debugLog: true);
        var ctx = TestHelpers.NewContext();
        ctx.FinalUsage = new TokenUsage { CacheCreationInputTokens = 5, CacheReadInputTokens = 3 };

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync([], ct), default));

        result.Should().HaveCount(1);
        result[0].Content.Should().Contain("缓存=创建5,读取3");
    }

    [Fact]
    public async Task InvokeAsync_DebugLogTrue_WithoutFinalUsage_TimingSummaryOmitsCacheStats() {
        await using var mw = new ChatTimingMiddleware(debugLog: true);
        var ctx = TestHelpers.NewContext();

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync([], ct), default));

        result.Should().HaveCount(1);
        result[0].Content.Should().NotContain("缓存=");
    }
}
