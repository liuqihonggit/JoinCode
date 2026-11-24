namespace Core.Tests.Hooks.Events;

/// <summary>
/// HookSystemMessageChannel 单元测试（TASK135）— 捕获/嵌套/释放/空白/事件排除/并行隔离/异常传播。
/// </summary>
public sealed class HookSystemMessageChannelTests {
    /// <summary>有捕获时投递到回调，并返回 true。</summary>
    [Fact]
    public void Publish_WithCapture_DeliversToCallback() {
        var messages = new List<string>();
        using var capture = HookSystemMessageChannel.BeginCapture(messages.Add);

        var delivered = HookSystemMessageChannel.Publish(HookEvent.PreToolUse, "工具前旁白");

        delivered.Should().BeTrue();
        messages.Should().ContainSingle().Which.Should().Be("工具前旁白");
    }

    /// <summary>无捕获时返回 false 且不抛（回合外的后台 hook 属预期边界）。</summary>
    [Fact]
    public void Publish_WithoutCapture_ReturnsFalseWithoutThrowing() {
        var delivered = HookSystemMessageChannel.Publish(HookEvent.Stop, "回合外旁白");

        delivered.Should().BeFalse();
    }

    /// <summary>null/空白不投递，也不调用回调。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Publish_BlankMessage_IsIgnored(string? message) {
        var messages = new List<string>();
        using var capture = HookSystemMessageChannel.BeginCapture(messages.Add);

        var delivered = HookSystemMessageChannel.Publish(HookEvent.PostToolUse, message);

        delivered.Should().BeFalse();
        messages.Should().BeEmpty();
    }

    /// <summary>UserPromptSubmit 由既有两条出口处理，通道必须跳过以避免重复上屏。</summary>
    [Fact]
    public void Publish_UserPromptSubmit_IsSkipped() {
        var messages = new List<string>();
        using var capture = HookSystemMessageChannel.BeginCapture(messages.Add);

        var delivered = HookSystemMessageChannel.Publish(HookEvent.UserPromptSubmit, "不应重复的旁白");

        delivered.Should().BeFalse();
        messages.Should().BeEmpty();
    }

    /// <summary>嵌套捕获：内层只收内层回调；内层释放后外层恢复接收。</summary>
    [Fact]
    public void NestedCaptures_RestorePreviousScope() {
        var outer = new List<string>();
        var inner = new List<string>();

        using (HookSystemMessageChannel.BeginCapture(outer.Add)) {
            using (HookSystemMessageChannel.BeginCapture(inner.Add)) {
                HookSystemMessageChannel.Publish(HookEvent.PreToolUse, "内层").Should().BeTrue();
            }

            HookSystemMessageChannel.Publish(HookEvent.PreToolUse, "外层").Should().BeTrue();
        }

        inner.Should().ContainSingle().Which.Should().Be("内层");
        outer.Should().ContainSingle().Which.Should().Be("外层");
    }

    /// <summary>释放后不再投递（防 fire-and-forget 回调打到已结束回合），且 Dispose 幂等。</summary>
    [Fact]
    public void Dispose_StopsDeliveryAndIsIdempotent() {
        var messages = new List<string>();
        var capture = HookSystemMessageChannel.BeginCapture(messages.Add);

        capture.Dispose();
        capture.Dispose();

        HookSystemMessageChannel.Publish(HookEvent.PreToolUse, "释放后").Should().BeFalse();
        messages.Should().BeEmpty();
    }

    /// <summary>并行异步上下文互不串台：每个任务只收到自己捕获的旁白。</summary>
    [Fact]
    public async Task Publish_InParallelContexts_IsIsolated() {
        var first = new List<string>();
        var second = new List<string>();

        var firstTask = Task.Run(async () => {
            using var capture = HookSystemMessageChannel.BeginCapture(first.Add);
            await Task.Delay(50);
            HookSystemMessageChannel.Publish(HookEvent.Stop, "first").Should().BeTrue();
        });

        var secondTask = Task.Run(async () => {
            using var capture = HookSystemMessageChannel.BeginCapture(second.Add);
            await Task.Delay(50);
            HookSystemMessageChannel.Publish(HookEvent.Stop, "second").Should().BeTrue();
        });

        await Task.WhenAll(firstTask, secondTask);

        first.Should().ContainSingle().Which.Should().Be("first");
        second.Should().ContainSingle().Which.Should().Be("second");
    }

    /// <summary>通道不做异常吞噬 — 消费者异常由编排器统一 fail-soft 记录（契约锁定）。</summary>
    [Fact]
    public void Publish_ConsumerThrows_PropagatesToCaller() {
        using var capture = HookSystemMessageChannel.BeginCapture(_ => throw new InvalidOperationException("consumer boom"));

        var act = () => HookSystemMessageChannel.Publish(HookEvent.Notification, "boom");

        act.Should().Throw<InvalidOperationException>().WithMessage("consumer boom");
    }
}
