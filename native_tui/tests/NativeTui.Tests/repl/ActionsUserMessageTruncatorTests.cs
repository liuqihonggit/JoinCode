namespace NativeTui.Tests.Repl;

/// <summary>
/// 用户消息 10k 截断测试（消息操作域）— 对齐基准 UserPromptMessage.tsx 常量（10_000 / 2_500 / 2_500）：
/// ≤ 上限原样返回；超限 head + 中间 `…截断…` 标记 + tail（Span 切片保真前后缀）。
/// 渲染接线在用户消息 item 域（S2，见未决问题）— 本文件验证纯函数。
/// </summary>
public sealed class ActionsUserMessageTruncatorTests
{
    /// <summary>短文本 — 原样返回（不截断）。</summary>
    [Fact]
    public void Truncate_ShortText_ReturnsAsIs()
    {
        Assert.Equal("短消息", UserMessageTruncator.Truncate("短消息"));
        Assert.Equal(string.Empty, UserMessageTruncator.Truncate(string.Empty));
    }

    /// <summary>恰好上限 — 原样返回（边界含 10_000）。</summary>
    [Fact]
    public void Truncate_ExactlyAtLimit_ReturnsAsIs()
    {
        var text = new string('a', UserMessageTruncator.MaxDisplayChars);
        Assert.Equal(text, UserMessageTruncator.Truncate(text));
    }

    /// <summary>超限 1 字符 — head 2500 + 标记 + tail 2500（总长 = 常量推导）。</summary>
    [Fact]
    public void Truncate_OneOverLimit_HeadMarkerTail()
    {
        var text = new string('a', UserMessageTruncator.MaxDisplayChars + 1);
        var truncated = UserMessageTruncator.Truncate(text);
        var expectedLength = UserMessageTruncator.HeadChars + UserMessageTruncator.TruncationMarker.Length + UserMessageTruncator.TailChars;
        Assert.Equal(expectedLength, truncated.Length);
        Assert.Equal(new string('a', UserMessageTruncator.HeadChars) + UserMessageTruncator.TruncationMarker + new string('a', UserMessageTruncator.TailChars), truncated);
    }

    /// <summary>超限 — head/tail 前后缀保真（头部与尾部字符可辨识）。</summary>
    [Fact]
    public void Truncate_PreservesHeadAndTailContent()
    {
        var head = new string('H', UserMessageTruncator.HeadChars);
        var middle = new string('M', 6000);
        var tail = new string('T', UserMessageTruncator.TailChars);
        var truncated = UserMessageTruncator.Truncate(head + middle + tail);

        Assert.StartsWith(new string('H', UserMessageTruncator.HeadChars), truncated);
        Assert.EndsWith(new string('T', UserMessageTruncator.TailChars), truncated);
        Assert.Contains("…截断…", truncated);
    }

    /// <summary>标记形态 — 独立两行边界（\n…截断…\n）。</summary>
    [Fact]
    public void TruncationMarker_StandaloneLine()
    {
        Assert.Equal("\n…截断…\n", UserMessageTruncator.TruncationMarker);
    }

    /// <summary>常量 — 对齐基准（10k / 2500 / 2500）。</summary>
    [Fact]
    public void Constants_MatchBaseline()
    {
        Assert.Equal(10_000, UserMessageTruncator.MaxDisplayChars);
        Assert.Equal(2_500, UserMessageTruncator.HeadChars);
        Assert.Equal(2_500, UserMessageTruncator.TailChars);
    }
}
