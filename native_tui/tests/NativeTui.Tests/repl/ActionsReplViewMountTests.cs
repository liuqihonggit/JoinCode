namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplView 操作条挂载与投影通道测试（消息操作域）— 光标态（MessageActions 非空）时操作条渲染在
/// footer 上方（边框行 + 内容行，输入区上移，高度链 MeasureContentHeight/TailHeight 计入）；
/// verbose / 连续用户消息渲染前投影通道（IVerboseAware / IUserContinuationAware — item 域实现消费）。
/// </summary>
public sealed class ActionsReplViewMountTests
{
    /// <summary>投影通道假项（IVerboseAware + IUserContinuationAware — S2 接线前的通道验证载体）。</summary>
    private sealed class ProjectionItem : IScrollItem, IVerboseAware, IUserContinuationAware
    {
        /// <summary>verbose 投影结果（断言面）。</summary>
        public bool Verbose { get; set; }

        /// <summary>连续用户消息投影结果（断言面）。</summary>
        public bool IsContinuation { get; set; }

        /// <summary>内容高度（与消息项同形态 — 本测试不消费）。</summary>
        public int MeasureHeight(int width) => 2;

        /// <summary>不渲染（投影通道纯状态面）。</summary>
        public void Render(Screen screen, Rect frame, Theme theme)
        {
        }
    }

    /// <summary>光标态 — 操作条渲染在 footer 上方：边框行 + 内容行，输入区整体上移 2 行。</summary>
    [Fact]
    public void Render_CursorActive_ActionsBarAboveFooter()
    {
        using var screen = new Screen(cols: 60, rows: 10);
        var view = new ReplView
        {
            Input = "hi",
            CursorCol = 2,
            Footer = new FooterState { Mode = PermissionMode.Auto },
            MessageActions = new MessageActionsState(CanExpand: true, IsExpanded: false, PrimaryInputLabel: "path"),
        };
        view.Messages.Add(new UserMessageItem("hello"));
        view.Render(screen, Theme.Dark);

        // footer 行 9 / 操作条行 7-8（边框 7 全宽 dim ─、内容 8 从 col 2 起）
        Assert.StartsWith("? 查看快捷键", RowText.Of(screen, 9, 2, 20).TrimEnd());
        Assert.Equal(new string('─', 56), RowText.Of(screen, 7, 2, 56));
        Assert.Equal("enter 展开 · c 复制 · p 复制 path · ↑↓ 导航 · esc 返回", RowText.Of(screen, 8, 2, 56).TrimEnd());
        // 输入区上移：底边线 6 / 文本 5 / 顶边线 4
        Assert.Equal(new string('─', 60), RowText.Of(screen, 6, 0, 60));
        Assert.Equal("❯ hi", RowText.Of(screen, 5, 0, 4).TrimEnd());
        Assert.Equal(new string('─', 60), RowText.Of(screen, 4, 0, 60));
    }

    /// <summary>非光标态 — 操作条不渲染，布局与既有完全一致（footer 9 / 底边线 8 / 文本 7 / 顶边线 6）。</summary>
    [Fact]
    public void Render_CursorInactive_NoActionsBar()
    {
        using var screen = new Screen(cols: 60, rows: 10);
        var view = new ReplView { Input = "hi", CursorCol = 2, Footer = new FooterState { Mode = PermissionMode.Auto } };
        view.Messages.Add(new UserMessageItem("hello"));
        view.Render(screen, Theme.Dark);

        Assert.Equal(new string('─', 60), RowText.Of(screen, 8, 0, 60));
        Assert.Equal("❯ hi", RowText.Of(screen, 7, 0, 4).TrimEnd());
        Assert.Equal(new string('─', 60), RowText.Of(screen, 6, 0, 60));
    }

    /// <summary>MeasureContentHeight — 光标态计入操作条 2 行。</summary>
    [Fact]
    public void MeasureContentHeight_CursorActive_AddsTwoRows()
    {
        var view = new ReplView { Footer = new FooterState { Mode = PermissionMode.Auto } };
        view.Messages.Add(new UserMessageItem("hello"));
        var withoutBar = view.MeasureContentHeight(cols: 40);

        view.MessageActions = new MessageActionsState(CanExpand: false, IsExpanded: false, PrimaryInputLabel: null);
        Assert.Equal(withoutBar + 2, view.MeasureContentHeight(cols: 40));
    }

    /// <summary>TailHeight — 光标态计入操作条 2 行。</summary>
    [Fact]
    public void TailHeight_CursorActive_AddsTwoRows()
    {
        var view = new ReplView { Footer = new FooterState { Mode = PermissionMode.Auto } };
        var withoutBar = view.TailHeightOf(80);

        view.MessageActions = new MessageActionsState(CanExpand: false, IsExpanded: false, PrimaryInputLabel: null);
        Assert.Equal(withoutBar + 2, view.TailHeightOf(80));
    }

    /// <summary>verbose 投影通道 — SetVerboseMode 渲染前投影给全部 IVerboseAware 项。</summary>
    [Fact]
    public void Render_VerboseProjection_ReachesAwareItems()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var first = new ProjectionItem();
        var second = new ProjectionItem();
        var view = new ReplView();
        view.Messages.Add(first);
        view.Messages.Add(second);

        view.SetVerboseMode(true);
        view.Render(screen, Theme.Dark);
        Assert.True(first.Verbose);
        Assert.True(second.Verbose);

        view.SetVerboseMode(false);
        view.Render(screen, Theme.Dark);
        Assert.False(first.Verbose);
        Assert.False(second.Verbose);
    }

    /// <summary>连续用户消息投影通道 — 严格前驱判定投影给 IUserContinuationAware 项（装饰项介入断开）。</summary>
    [Fact]
    public void Render_ContinuationProjection_StrictPredecessor()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var first = new ProjectionItem();
        var second = new ProjectionItem();
        var view = new ReplView();
        view.Messages.Add(first);
        view.Messages.Add(new BannerItem("1.0.0", "m", "API", "/tmp"));
        view.Messages.Add(second);
        view.Render(screen, Theme.Dark);

        Assert.False(first.IsContinuation);
        Assert.False(second.IsContinuation);

        using var screen2 = new Screen(cols: 40, rows: 10);
        var adjacent = new ProjectionItem();
        var view2 = new ReplView();
        view2.Messages.Add(new UserMessageItem("q1"));
        view2.Messages.Add(adjacent);
        view2.Render(screen2, Theme.Dark);
        Assert.True(adjacent.IsContinuation);
    }
}
