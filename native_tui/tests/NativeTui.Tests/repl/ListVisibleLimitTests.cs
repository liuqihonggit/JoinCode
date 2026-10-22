namespace NativeTui.Tests.Repl;

/// <summary>
/// 渲染项数上限测试（消息列表域）— 对齐基准 Messages.tsx MAX_MESSAGES_WITHOUT_VIRTUALIZATION=200 +
/// MESSAGE_CAP_STEP=50 量化推进：≤ 200+50 不触发；超过触发裁最旧至上限 + 头部挂载折叠提示行
/// `… 更早消息已折叠`；光标态（MessageActions 激活）跳过裁剪防选中索引移位。
/// </summary>
public sealed class ListVisibleLimitTests
{
    /// <summary>折叠提示渲染 — 整行 dim `… 更早消息已折叠`。</summary>
    [Fact]
    public void FoldedNotice_RendersDimLine()
    {
        using var screen = new Screen(cols: 40, rows: 2);
        new FoldedNoticeItem().Render(screen, new Rect(0, 0, 40, 1), Theme.Dark);

        Assert.Equal("… 更早消息已折叠", RowText.Of(screen, 0, 0, 40).TrimEnd());
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[0, 0].StyleId);
    }

    /// <summary>折叠提示高度 — 1 行（无 margin 头部紧凑指示）。</summary>
    [Fact]
    public void FoldedNotice_HeightOne()
    {
        Assert.Equal(1, new FoldedNoticeItem().MeasureHeight(width: 40));
    }

    /// <summary>上限内 — 不触发裁剪（200 条全量保留）。</summary>
    [Fact]
    public void Render_AtLimit_NoTrim()
    {
        using var screen = new Screen(cols: 40, rows: 24);
        var view = CreateView(ReplView.VisibleItemLimit);
        view.Render(screen, Theme.Dark);

        Assert.Equal(ReplView.VisibleItemLimit, view.Messages.Count);
        Assert.DoesNotContain(view.Messages.Items, static item => item is FoldedNoticeItem);
    }

    /// <summary>触发线内 — 200+50 条不触发（步长量化 — 减少每帧头部位移抖动）。</summary>
    [Fact]
    public void Render_UpToLimitPlusStep_NoTrim()
    {
        using var screen = new Screen(cols: 40, rows: 24);
        var view = CreateView(ReplView.VisibleItemLimit + 50);
        view.Render(screen, Theme.Dark);

        Assert.Equal(ReplView.VisibleItemLimit + 50, view.Messages.Count);
        Assert.DoesNotContain(view.Messages.Items, static item => item is FoldedNoticeItem);
    }

    /// <summary>超过触发线 — 裁最旧至上限 + 头部挂载折叠提示（199 内容 + 1 提示 = 200）。</summary>
    [Fact]
    public void Render_OverLimit_TrimsOldestAndNotices()
    {
        using var screen = new Screen(cols: 40, rows: 24);
        var view = CreateView(ReplView.VisibleItemLimit + 51);
        view.Render(screen, Theme.Dark);

        Assert.Equal(ReplView.VisibleItemLimit, view.Messages.Count);
        Assert.IsType<FoldedNoticeItem>(view.Messages.Items[0]);
        // 最旧 52 条被裁（m0..m51 消失），保留 m52..m250
        Assert.Equal("m52", Assert.IsType<UserMessageItem>(view.Messages.Items[1]).Text);
        Assert.Equal($"m{ReplView.VisibleItemLimit + 50}", Assert.IsType<UserMessageItem>(view.Messages.Items[^1]).Text);
    }

    /// <summary>再增长 — 二次触发前不重复裁剪（200..250 稳定窗口）。</summary>
    [Fact]
    public void Render_SteadyWindowBetweenFolds()
    {
        using var screen = new Screen(cols: 40, rows: 24);
        var view = CreateView(ReplView.VisibleItemLimit + 51);
        view.Render(screen, Theme.Dark);
        Assert.Equal(ReplView.VisibleItemLimit, view.Messages.Count);

        for (var i = 0; i < 10; i++)
        {
            view.Messages.Add(new UserMessageItem($"extra{i}"));
        }

        view.Render(screen, Theme.Dark);
        // 210 ≤ 200+50 — 不触发（提示在头部占位，总项数稳定 210）
        Assert.Equal(ReplView.VisibleItemLimit + 10, view.Messages.Count);
        Assert.IsType<FoldedNoticeItem>(view.Messages.Items[0]);
    }

    /// <summary>光标态激活 — 跳过裁剪（防 MessageCursor.SelectedIndex 移位；退出后下一帧补裁）。</summary>
    [Fact]
    public void Render_CursorActive_SkipsTrim()
    {
        using var screen = new Screen(cols: 40, rows: 24);
        var view = CreateView(ReplView.VisibleItemLimit + 51);
        view.MessageActions = new MessageActionsState(CanExpand: false, IsExpanded: false, PrimaryInputLabel: null);
        view.Render(screen, Theme.Dark);

        Assert.Equal(ReplView.VisibleItemLimit + 51, view.Messages.Count);
        Assert.DoesNotContain(view.Messages.Items, static item => item is FoldedNoticeItem);
    }

    /// <summary>构造含 N 条用户消息的视图（每条 MeasureHeight 2 — 高度与真实场景一致走 viewport culling）。</summary>
    private static ReplView CreateView(int count)
    {
        var view = new ReplView();
        for (var i = 0; i < count; i++)
        {
            view.Messages.Add(new UserMessageItem($"m{i}"));
        }

        return view;
    }
}
