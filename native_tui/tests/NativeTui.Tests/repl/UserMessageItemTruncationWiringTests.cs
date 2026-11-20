namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK069-M6 接线验收 — 用户消息 10k 显示截断接到 UserMessageItem 渲染：
/// DisplayText 超长截断（head + 标记 + tail），Text 保留全文（复制/搜索/排队断言不受影响）。
/// 纯展示层缺口接线，不改算法（算法由 ActionsUserMessageTruncatorTests 覆盖）。
/// </summary>
public sealed class UserMessageItemTruncationWiringTests
{
    /// <summary>短消息 — DisplayText 与 Text 一致（不截断、不分配额外内容）。</summary>
    [Fact]
    public void DisplayText_ShortMessage_EqualsText() {
        var item = new UserMessageItem("短消息");
        Assert.Equal(item.Text, item.DisplayText);
    }

    /// <summary>超限 — DisplayText 截断为 head + 标记 + tail。</summary>
    [Fact]
    public void DisplayText_OverLimit_Truncated() {
        var text = new string('H', UserMessageTruncator.HeadChars)
            + new string('M', 6000)
            + new string('T', UserMessageTruncator.TailChars);
        var item = new UserMessageItem(text);

        Assert.Contains("…截断…", item.DisplayText);
        Assert.StartsWith(new string('H', UserMessageTruncator.HeadChars), item.DisplayText);
        Assert.EndsWith(new string('T', UserMessageTruncator.TailChars), item.DisplayText);
        Assert.True(item.DisplayText.Length < item.Text.Length);
    }

    /// <summary>复制/搜索面保真 — Text 仍是全文（截断只作用于显示）。</summary>
    [Fact]
    public void Text_RemainsFullContent_AfterDisplayTruncation() {
        var middle = new string('M', 6000);
        var text = new string('H', UserMessageTruncator.HeadChars) + middle + new string('T', UserMessageTruncator.TailChars);
        var item = new UserMessageItem(text);

        Assert.Equal(text, item.Text);
        Assert.Contains(middle, item.Text);
    }

    /// <summary>搜索面保真 — ContainsText 命中被截断掉的中间段（搜索基于 Text 全文）。</summary>
    [Fact]
    public void ContainsText_SearchesFullText_NotDisplayText() {
        // 总长须 > 10000 才触发截断：head 2500 + middle 6000（含标记）+ tail 2500
        var middle = new string('M', 6000 - 20) + "UNIQUE_MIDDLE_MARKER";
        var text = new string('H', UserMessageTruncator.HeadChars) + middle + new string('T', UserMessageTruncator.TailChars);
        var item = new UserMessageItem(text);

        Assert.True(item.ContainsText(middle));
        Assert.False(item.DisplayText.Contains(middle, StringComparison.Ordinal));
    }
}
