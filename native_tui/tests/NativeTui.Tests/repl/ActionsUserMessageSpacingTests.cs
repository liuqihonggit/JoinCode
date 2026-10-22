namespace NativeTui.Tests.Repl;

/// <summary>
/// 连续用户消息紧凑判定测试（消息操作域）— 对齐基准 Message.tsx isUserContinuation：
/// 严格前驱亦为用户消息 → true；装饰项/banner/其他消息类型介入即断开 false。
/// item 渲染接线在用户消息 item 域（S2 经 IUserContinuationAware 投影，见未决问题）— 本文件验证纯判定。
/// </summary>
public sealed class ActionsUserMessageSpacingTests
{
    /// <summary>连续用户消息 — 第二条起为延续（margin 压缩依据）。</summary>
    [Fact]
    public void IsUserContinuation_ConsecutiveUserMessages_True()
    {
        var items = new IScrollItem[] { new UserMessageItem("q1"), new UserMessageItem("q2"), new UserMessageItem("q3") };
        Assert.False(UserMessageSpacing.IsUserContinuation(items, 0));
        Assert.True(UserMessageSpacing.IsUserContinuation(items, 1));
        Assert.True(UserMessageSpacing.IsUserContinuation(items, 2));
    }

    /// <summary>用户-助手-用户交替 — 均非延续。</summary>
    [Fact]
    public void IsUserContinuation_InterleavedWithAssistant_False()
    {
        var items = new IScrollItem[]
        {
            new UserMessageItem("q1"),
            new AssistantMessageItem("a1"),
            new UserMessageItem("q2"),
        };
        Assert.False(UserMessageSpacing.IsUserContinuation(items, 0));
        Assert.False(UserMessageSpacing.IsUserContinuation(items, 2));
    }

    /// <summary>banner 介入 — 连续性断开（严格前驱判定）。</summary>
    [Fact]
    public void IsUserContinuation_BannerBreaksChain_False()
    {
        var items = new IScrollItem[]
        {
            new BannerItem("1.0.0", "m", "API", "/tmp"),
            new UserMessageItem("q1"),
            new UserMessageItem("q2"),
        };
        Assert.False(UserMessageSpacing.IsUserContinuation(items, 1));
        Assert.True(UserMessageSpacing.IsUserContinuation(items, 2));
    }

    /// <summary>非用户消息处 — 恒 false。</summary>
    [Fact]
    public void IsUserContinuation_NonUserItem_False()
    {
        var items = new IScrollItem[] { new UserMessageItem("q1"), new AssistantMessageItem("a1") };
        Assert.False(UserMessageSpacing.IsUserContinuation(items, 1));
    }

    /// <summary>越界索引 — 安全 false。</summary>
    [Fact]
    public void IsUserContinuation_OutOfRange_False()
    {
        var items = new IScrollItem[] { new UserMessageItem("q1") };
        Assert.False(UserMessageSpacing.IsUserContinuation(items, -1));
        Assert.False(UserMessageSpacing.IsUserContinuation(items, 5));
    }
}
