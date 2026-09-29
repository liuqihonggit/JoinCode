namespace Host.Tests.App;

/// <summary>
/// AuditLogMiddleware 确定性测试 — 验证消息截断与流事件累积的纯计算逻辑
/// </summary>
public sealed class AuditLogMiddlewareTests {
    [Fact]
    public void TruncateForAudit_EmptyString_ShouldReturnEmpty() {
        var result = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.TruncateForAudit(string.Empty, 200);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void TruncateForAudit_ExactMaxLen_ShouldReturnOriginal() {
        const int maxLen = 200;
        var message = new string('a', maxLen);

        var result = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.TruncateForAudit(message, maxLen);

        Assert.Equal(message, result);
        Assert.Equal(maxLen, result.Length);
    }

    [Fact]
    public void TruncateForAudit_ExceedsMaxLen_ShouldTruncateWithEllipsis() {
        const int maxLen = 200;
        var message = new string('a', maxLen + 10);

        var result = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.TruncateForAudit(message, maxLen);

        Assert.Equal(new string('a', maxLen) + "...", result);
        Assert.Equal(maxLen + 3, result.Length);
    }

    [Fact]
    public void TruncateForAudit_ShorterThanMaxLen_ShouldReturnOriginal() {
        const int maxLen = 200;
        var message = "short message";

        var result = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.TruncateForAudit(message, maxLen);

        Assert.Equal(message, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(199)]
    public void TruncateForAudit_BelowOrEqualThreshold_ShouldNeverAppendEllipsis(int len) {
        const int maxLen = 200;
        var message = new string('x', len);

        var result = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.TruncateForAudit(message, maxLen);

        Assert.DoesNotContain("...", result);
        Assert.Equal(len, result.Length);
    }

    [Fact]
    public void TruncateForAudit_SpanConcat_PreservesContentExactly() {
        const int maxLen = 5;
        var message = "HelloWorld";

        var result = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.TruncateForAudit(message, maxLen);

        Assert.Equal("Hello...", result);
    }

    [Fact]
    public void AccumulateEvent_ContentEvent_ShouldAddContentLength() {
        var evt = ChatStreamEvent.Text("hello");

        var (newChars, isToolCall) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt, 0, 200);

        Assert.Equal(5, newChars);
        Assert.False(isToolCall);
    }

    [Fact]
    public void AccumulateEvent_ContentEvent_PartialTakeNearLimit_ShouldClampToMax() {
        var evt = ChatStreamEvent.Text("hello"); // 5 chars
        const int currentChars = 198;
        const int maxLen = 200;

        var (newChars, isToolCall) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt, currentChars, maxLen);

        Assert.Equal(200, newChars);
        Assert.False(isToolCall);
    }

    [Fact]
    public void AccumulateEvent_ContentEvent_WhenAlreadyAtMax_ShouldNotAccumulate() {
        var evt = ChatStreamEvent.Text("hello");
        const int currentChars = 200;
        const int maxLen = 200;

        var (newChars, isToolCall) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt, currentChars, maxLen);

        Assert.Equal(200, newChars);
        Assert.False(isToolCall);
    }

    [Fact]
    public void AccumulateEvent_ContentEvent_WithNullContent_ShouldNotAccumulate() {
        var evt = new ChatStreamEvent { Type = ChatStreamEventType.Content, Content = null };

        var (newChars, isToolCall) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt, 10, 200);

        Assert.Equal(10, newChars);
        Assert.False(isToolCall);
    }

    [Fact]
    public void AccumulateEvent_ContentEvent_WithEmptyContent_ShouldNotIncreaseChars() {
        var evt = ChatStreamEvent.Text(string.Empty);

        var (newChars, isToolCall) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt, 10, 200);

        Assert.Equal(10, newChars);
        Assert.False(isToolCall);
    }

    [Fact]
    public void AccumulateEvent_ToolCallStart_ShouldFlagToolCallWithoutChangingChars() {
        var evt = ChatStreamEvent.ToolStart("bash", "call-1", "{}");

        var (newChars, isToolCall) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt, 42, 200);

        Assert.Equal(42, newChars);
        Assert.True(isToolCall);
    }

    [Fact]
    public void AccumulateEvent_CompleteEvent_ShouldNotChangeState() {
        var evt = ChatStreamEvent.Done();

        var (newChars, isToolCall) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt, 42, 200);

        Assert.Equal(42, newChars);
        Assert.False(isToolCall);
    }

    [Fact]
    public void AccumulateEvent_ThinkingEvent_ShouldNotChangeState() {
        var evt = ChatStreamEvent.Thinking("pondering");

        var (newChars, isToolCall) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt, 42, 200);

        Assert.Equal(42, newChars);
        Assert.False(isToolCall);
    }

    [Fact]
    public void AccumulateEvent_ToolCallEnd_ShouldNotChangeState() {
        var evt = ChatStreamEvent.ToolEnd("bash", "done");

        var (newChars, isToolCall) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt, 42, 200);

        Assert.Equal(42, newChars);
        Assert.False(isToolCall);
    }

    [Fact]
    public void AccumulateEvent_MultipleContentEvents_AccumulateProgressively() {
        const int maxLen = 200;
        var evt1 = ChatStreamEvent.Text("hello"); // 5
        var evt2 = ChatStreamEvent.Text("world"); // 5

        var (chars1, _) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt1, 0, maxLen);
        var (chars2, _) = JoinCode.Pipelines.Middlewares.AuditLogMiddleware.AccumulateEvent(evt2, chars1, maxLen);

        Assert.Equal(10, chars2);
    }
}
