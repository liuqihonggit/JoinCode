namespace NativeTui.Tests.Repl;

/// <summary>
/// 未读分隔线测试（消息列表域）— 对齐基准 Messages.tsx unseen divider + FullscreenLayout useUnseenDivider：
/// UnreadDividerItem 渲染形态（全宽 ─ dim + `── N 条新消息 ──` 居中）+ ReplView 分界通道
/// （sticky 滚动跟随状态自动快照/清除、divider 惰性挂载、新消息计数动态刷新、
/// 驱动层 Insert 到 spinner 之前天然落在 divider 之后）。
/// </summary>
public sealed class ListUnreadDividerTests
{
    /// <summary>渲染形态 — 全宽 ─ dim + 标题 `── 3 条新消息 ──` 居中（宽字符显示宽居中计算）。</summary>
    [Fact]
    public void Render_FullWidthLineWithCenteredTitle()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        new UnreadDividerItem(3).Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);

        // margin 行留空
        Assert.Equal(Cell.Blank, screen[0, 0]);
        // 标题显示宽 16（──2 + 空格1 + 3=1 + 空格1 + 条新消息8 + 空格1 + ──2）→ 起始 col (40-16)/2 = 12
        Assert.Equal("── 3 条新消息 ──", RowText.Of(screen, 1, 12, 16));
        // 两侧 ─ 填充
        Assert.Equal(new string('─', 12), RowText.Of(screen, 1, 0, 12));
        Assert.Equal(new string('─', 12), RowText.Of(screen, 1, 28, 12));
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[1, 12].StyleId);
    }

    /// <summary>奇数宽 — 居中左偏 1 列容忍（Math.Floor 语义）。</summary>
    [Fact]
    public void Render_OddWidth_CentersWithinTolerance()
    {
        using var screen = new Screen(cols: 41, rows: 4);
        new UnreadDividerItem(1).Render(screen, new Rect(0, 0, 41, 2), Theme.Dark);

        // 标题显示宽 14（──2 + 空格1 + 1=1 + 空格1 + 条新消息8 + 空格1 + ──2）→ 起始 col (41-14)/2 = 13
        Assert.Contains("── 1 条新消息 ──", RowText.Of(screen, 1, 0, 41));
    }

    /// <summary>内容高度 — margin 1 + 线 1（对齐消息项 addMargin 模式）。</summary>
    [Fact]
    public void MeasureHeight_MarginPlusLine()
    {
        Assert.Equal(2, new UnreadDividerItem(0).MeasureHeight(width: 40));
    }

    /// <summary>钉底态 — Render 不建分界（无 divider、计数 0）。</summary>
    [Fact]
    public void Render_Sticky_NoDivider()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var view = new ReplView();
        view.Messages.Add(new UserMessageItem("m1"));
        view.Messages.Add(new UserMessageItem("m2"));
        view.Render(screen, Theme.Dark);

        Assert.Equal(0, view.UnreadCount);
        Assert.DoesNotContain(view.Messages.Items, static item => item is UnreadDividerItem);
    }

    /// <summary>离开底部（ScrollBy 打破 sticky）— Render 自动快照分界；无新消息不挂 divider。</summary>
    [Fact]
    public void Render_ScrollAway_SnapshotsBoundaryWithoutDivider()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var view = new ReplView();
        view.Messages.Add(new UserMessageItem("m1"));
        view.Messages.Add(new UserMessageItem("m2"));
        view.Messages.ScrollBy(-3);
        view.Render(screen, Theme.Dark);

        Assert.Equal(0, view.UnreadCount);
        Assert.DoesNotContain(view.Messages.Items, static item => item is UnreadDividerItem);
    }

    /// <summary>分界后新消息到达 — divider 惰性挂载于分界位置 + 计数。</summary>
    [Fact]
    public void Render_NewMessagesAfterBoundary_MountsDividerAtBoundary()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var view = new ReplView();
        view.Messages.Add(new UserMessageItem("m1"));
        view.Messages.Add(new UserMessageItem("m2"));
        view.Messages.ScrollBy(-3);
        view.Render(screen, Theme.Dark);

        view.Messages.Add(new UserMessageItem("m3"));
        view.Render(screen, Theme.Dark);

        Assert.Equal(1, view.UnreadCount);
        var divider = Assert.IsType<UnreadDividerItem>(view.Messages.Items[2]);
        Assert.Equal(1, divider.Count);
        Assert.IsType<UserMessageItem>(view.Messages.Items[3]);
        Assert.Equal("m3", Assert.IsType<UserMessageItem>(view.Messages.Items[3]).Text);
    }

    /// <summary>计数动态刷新 — 同 divider 实例随新消息增长。</summary>
    [Fact]
    public void Render_CountGrowsWithNewMessages()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var view = new ReplView();
        view.Messages.Add(new UserMessageItem("m1"));
        view.Messages.ScrollBy(-1);
        view.Render(screen, Theme.Dark);

        view.Messages.Add(new UserMessageItem("m2"));
        view.Render(screen, Theme.Dark);
        var divider = Assert.IsType<UnreadDividerItem>(view.Messages.Items[1]);
        Assert.Equal(1, divider.Count);

        view.Messages.Add(new UserMessageItem("m3"));
        view.Render(screen, Theme.Dark);
        Assert.Equal(2, view.UnreadCount);
        Assert.Same(divider, view.Messages.Items.OfType<UnreadDividerItem>().Single());
        Assert.Equal(2, divider.Count);
    }

    /// <summary>驱动层 Insert 到 spinner 之前 — 新消息天然落在 divider 之后（常驻尾项共存）。</summary>
    [Fact]
    public void Render_InsertBeforeSpinner_LandsAfterDivider()
    {
        // 消息区视口 = rows12 - footer1 - 输入3 - 间隔4 = 7 行；5 消息 + spinner = 12 行 > 7
        // （内容超视口 — ScrollBy 才真正离开底部；内容不满屏时 ScrollBox 内容增长自动钉底会清分界）
        using var screen = new Screen(cols: 40, rows: 12);
        var view = new ReplView();
        for (var i = 1; i <= 5; i++)
        {
            view.Messages.Add(new UserMessageItem($"m{i}"));
        }

        view.Messages.Add(new SpinnerItem { Message = string.Empty });
        view.Messages.ScrollBy(-3);
        view.Render(screen, Theme.Dark);

        view.Messages.Insert(view.Messages.Count - 1, new UserMessageItem("m6"));
        view.Render(screen, Theme.Dark);
        Assert.Equal(1, view.UnreadCount);
        Assert.IsType<UnreadDividerItem>(view.Messages.Items[6]);

        view.Messages.Insert(view.Messages.Count - 1, new UserMessageItem("m7"));
        view.Render(screen, Theme.Dark);
        Assert.Equal(2, view.UnreadCount);
        // items = [m1..m5, m6, divider(6), m7(7), spinner(8)] — 新消息天然落在 divider 之后
        Assert.Equal(6, view.Messages.IndexOf(view.Messages.Items.OfType<UnreadDividerItem>().Single()));
        Assert.Equal("m7", Assert.IsType<UserMessageItem>(view.Messages.Items[7]).Text);
    }

    /// <summary>钉底恢复（ScrollToBottom）— Render 自动清除 divider + 计数归零。</summary>
    [Fact]
    public void Render_ScrollBackToBottom_ClearsDivider()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var view = new ReplView();
        view.Messages.Add(new UserMessageItem("m1"));
        view.Messages.ScrollBy(-1);
        view.Render(screen, Theme.Dark);
        view.Messages.Add(new UserMessageItem("m2"));
        view.Render(screen, Theme.Dark);
        Assert.Equal(1, view.UnreadCount);

        view.Messages.ScrollToBottom();
        view.Render(screen, Theme.Dark);

        Assert.Equal(0, view.UnreadCount);
        Assert.DoesNotContain(view.Messages.Items, static item => item is UnreadDividerItem);
    }

    /// <summary>MarkUnreadBoundary — 幂等（已有快照保持，不随消息增长刷新）。</summary>
    [Fact]
    public void MarkUnreadBoundary_IdempotentKeepsFirstSnapshot()
    {
        var view = new ReplView();
        view.Messages.Add(new UserMessageItem("m1"));
        view.MarkUnreadBoundary();
        view.Messages.Add(new UserMessageItem("m2"));
        view.MarkUnreadBoundary();

        Assert.Equal(1, view.UnreadCount);
    }

    /// <summary>ClearUnreadBoundary — 钉底态无 divider 时 no-op（幂等）。</summary>
    [Fact]
    public void ClearUnreadBoundary_WithoutDivider_NoOp()
    {
        var view = new ReplView();
        view.MarkUnreadBoundary();
        view.ClearUnreadBoundary();
        view.ClearUnreadBoundary();

        Assert.Equal(0, view.UnreadCount);
    }
}
