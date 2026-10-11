namespace Pipelines.Tests;

/// <summary>
/// AuditLogMiddleware 确定性单元测试 — 验证透传、字符累积上限、工具调用统计、
/// Complete 触发 Done 日志、长消息截断。用 CapturingLogger 捕获日志路径。
/// 无 IO/时序依赖。
/// </summary>
public sealed class AuditLogMiddlewareTest {
    private static AuditLogMiddleware NewMiddleware(ILogger<AuditLogMiddleware>? logger = null)
        => new(logger ?? NullLogger<AuditLogMiddleware>.Instance);

    // === InvokeAsync: 透传 ===

    [Fact]
    public async Task InvokeAsync_PassesThroughAllEventsInOrder() {
        await using var mw = NewMiddleware();
        var ctx = TestHelpers.NewContext();
        var events = new[] {
            ChatStreamEvent.Text("hello"),
            ChatStreamEvent.ToolStart("bash"),
            ChatStreamEvent.Done()
        };

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync(events, ct), default));

        result.Should().HaveCount(3);
        result.Should().ContainInOrder(events);
    }

    [Fact]
    public async Task InvokeAsync_CompleteEvent_LogsDoneWithModelAndUsage() {
        var logger = new CapturingLogger<AuditLogMiddleware>();
        await using var mw = NewMiddleware(logger);
        var ctx = TestHelpers.NewContext();
        var usage = new TokenUsage(100, 50);
        var events = new[] { ChatStreamEvent.Done(usage, "gpt-4o") };

        await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync(events, ct), default));

        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("[Audit] Done:"));
    }

    [Fact]
    public async Task InvokeAsync_ToolCallStart_LogsToolName() {
        var logger = new CapturingLogger<AuditLogMiddleware>();
        await using var mw = NewMiddleware(logger);
        var ctx = TestHelpers.NewContext();
        var events = new[] { ChatStreamEvent.ToolStart("Read"), ChatStreamEvent.Done() };

        await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync(events, ct), default));

        logger.Entries.Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.Contains("[Audit] Tool:") && e.Message.Contains("Read"));
    }

    [Fact]
    public async Task InvokeAsync_Always_LogsUserAndAssistantSummary() {
        var logger = new CapturingLogger<AuditLogMiddleware>();
        await using var mw = NewMiddleware(logger);
        var ctx = TestHelpers.NewContext(message: "user question", turn: 3);

        await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync([], ct), default));

        logger.Entries.Should().Contain(e => e.Message.Contains("[Audit] User") && e.Message.Contains("user question"));
        logger.Entries.Should().Contain(e => e.Message.Contains("[Audit] Assistant"));
    }

    // === TruncateForAudit: 纯函数确定性测试 ===

    [Fact]
    public void TruncateForAudit_ShortMessage_ReturnsAsIs() {
        AuditLogMiddleware.TruncateForAudit("short", 200).Should().Be("short");
    }

    [Fact]
    public void TruncateForAudit_ExactMaxLen_ReturnsAsIs() {
        var msg = new string('x', 200);
        AuditLogMiddleware.TruncateForAudit(msg, 200).Should().Be(msg);
    }

    [Fact]
    public void TruncateForAudit_LongerThanMax_TruncatesWithEllipsis() {
        var msg = new string('x', 250);
        var result = AuditLogMiddleware.TruncateForAudit(msg, 200);
        result.Should().HaveLength(203);
        result.Should().EndWith("...");
        result.Should().StartWith(new string('x', 200));
    }

    // === AccumulateEvent: 纯函数确定性测试 ===

    [Fact]
    public void AccumulateEvent_Content_AccumulatesCharsUpToMax() {
        var evt = ChatStreamEvent.Text("hello");
        var (newChars, isToolCall) = AuditLogMiddleware.AccumulateEvent(evt, 0, 200);
        newChars.Should().Be(5);
        isToolCall.Should().BeFalse();
    }

    [Fact]
    public void AccumulateEvent_Content_PartialWhenExceedingRemaining() {
        var evt = ChatStreamEvent.Text("hello world");
        var (newChars, _) = AuditLogMiddleware.AccumulateEvent(evt, 198, 200);
        newChars.Should().Be(200);
    }

    [Fact]
    public void AccumulateEvent_Content_BeyondMax_DoesNotAccumulate() {
        var evt = ChatStreamEvent.Text("hello");
        var (newChars, isToolCall) = AuditLogMiddleware.AccumulateEvent(evt, 200, 200);
        newChars.Should().Be(200);
        isToolCall.Should().BeFalse();
    }

    [Fact]
    public void AccumulateEvent_NullContent_DoesNotAccumulate() {
        var evt = new ChatStreamEvent { Type = ChatStreamEventType.Content, Content = null };
        var (newChars, isToolCall) = AuditLogMiddleware.AccumulateEvent(evt, 10, 200);
        newChars.Should().Be(10);
        isToolCall.Should().BeFalse();
    }

    [Fact]
    public void AccumulateEvent_ToolCallStart_ReturnsIsToolCallTrue() {
        var evt = ChatStreamEvent.ToolStart("bash");
        var (newChars, isToolCall) = AuditLogMiddleware.AccumulateEvent(evt, 50, 200);
        newChars.Should().Be(50);
        isToolCall.Should().BeTrue();
    }

    [Fact]
    public void AccumulateEvent_OtherType_DoesNotAccumulateOrFlagTool() {
        var evt = ChatStreamEvent.Done();
        var (newChars, isToolCall) = AuditLogMiddleware.AccumulateEvent(evt, 50, 200);
        newChars.Should().Be(50);
        isToolCall.Should().BeFalse();
    }
}
