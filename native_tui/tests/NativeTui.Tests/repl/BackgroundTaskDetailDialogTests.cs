namespace NativeTui.Tests.Repl;

/// <summary>
/// 后台任务详情对话框测试（M8-8 登记遗留收口 — 基准 BackgroundTasksDialog.tsx:230-348 详情视图）：
/// 快照构造（Entry + nowMs 参数化注入防墙钟不确定断言）、字段行组渲染（dim 标签 + 值，
/// 标签恒 4 汉字 8 显示列 → 值列恒 col 11）、任务状态汉化复用面板单一映射源、
/// 终态时间相对秒数（FinalizedAtMs&gt;0）/未终态 —、描述超宽折行（PermissionTextWrap 复用，
/// 续行缩进对齐值列）、底部提示行 "esc 返回 · left 关闭" dim。
/// </summary>
public sealed class BackgroundTaskDetailDialogTests
{
    /// <summary>字段行组渲染 — 固定行序（margin/边框/标题/空行/类别/标识/描述/状态/终态时间/空行/提示）
    /// + 快照 Entry 字段直通 + 值列恒 col 11 对齐 + MeasureHeight 单行描述 = 11。</summary>
    [Fact]
    public void Render_FieldRows_KindTaskIdDescriptionState()
    {
        using var screen = new Screen(cols: 40, rows: 11);
        var dialog = new BackgroundTaskDetailDialog(
            new BackgroundTaskEntry("teammate", "tm-1", "审阅PR", "running"), nowMs: 10_000);

        Assert.Equal(11, dialog.MeasureHeight(40));
        dialog.Render(screen, new Rect(0, 0, 40, 11), Theme.Dark);

        // 行序：margin0 / 边框1 / 标题2 / 空3 / 类别4 / 标识5 / 描述6 / 状态7 / 终态8 / 空9 / 提示10
        Assert.Equal(new string('─', 40), RowText.Of(screen, 1, 0, 40));
        Assert.Equal(" 后台任务详情", RowText.Of(screen, 2, 0, 40).TrimEnd());
        Assert.Equal(" 任务类别  teammate", RowText.Of(screen, 4, 0, 40).TrimEnd());
        Assert.Equal(" 任务标识  tm-1", RowText.Of(screen, 5, 0, 40).TrimEnd());
        Assert.Equal(" 任务描述  审阅PR", RowText.Of(screen, 6, 0, 40).TrimEnd());
        Assert.Equal(" 任务状态  运行中", RowText.Of(screen, 7, 0, 40).TrimEnd());
        Assert.Equal(" 终态时间  —", RowText.Of(screen, 8, 0, 40).TrimEnd());

        // 标签恒 4 汉字 = 显示恒 8 列 + 2 间隔 — 值列恒 col 11 起（快照字段直通）
        Assert.Equal("teammate", RowText.Of(screen, 4, 11, 8).TrimEnd());
        Assert.Equal("tm-1", RowText.Of(screen, 5, 11, 4).TrimEnd());
    }

    /// <summary>任务状态汉化复用面板映射源 — completed 已完成；未知值宽容原样（协议演进）。</summary>
    [Fact]
    public void Render_State_UsesPanelChineseMapping()
    {
        using var screen = new Screen(cols: 40, rows: 11);
        var dialog = new BackgroundTaskDetailDialog(
            new BackgroundTaskEntry("fork", "f-1", "构建", "completed"), nowMs: 10_000);

        dialog.Render(screen, new Rect(0, 0, 40, 11), Theme.Dark);

        Assert.Equal(" 任务状态  已完成", RowText.Of(screen, 7, 0, 40).TrimEnd());

        // 未知状态原样显示 — 对齐面板 StatusText 宽容语义（单一映射源不双维护）
        using var unknownScreen = new Screen(cols: 40, rows: 11);
        var unknown = new BackgroundTaskDetailDialog(
            new BackgroundTaskEntry("fork", "f-2", "探测", "paused"), nowMs: 10_000);

        unknown.Render(unknownScreen, new Rect(0, 0, 40, 11), Theme.Dark);

        Assert.Equal(" 任务状态  paused", RowText.Of(unknownScreen, 7, 0, 40).TrimEnd());
    }

    /// <summary>终态时间 — FinalizedAtMs&gt;0 相对秒数 "N 秒前"（nowMs 构造注入可控防墙钟不确定）；
    /// 未终态（FinalizedAtMs=0）显示 —。</summary>
    [Fact]
    public void Render_FinalizedAt_RelativeSeconds_OrDash()
    {
        using var screen = new Screen(cols: 40, rows: 11);
        var dialog = new BackgroundTaskDetailDialog(
            new BackgroundTaskEntry("fork", "f-1", "构建", "failed", FinalizedAtMs: 8_000), nowMs: 10_000);

        dialog.Render(screen, new Rect(0, 0, 40, 11), Theme.Dark);

        // (10_000 - 8_000) / 1000 = 2 秒前 — 构造时刻快照不随墙钟漂移
        Assert.Equal(" 终态时间  2 秒前", RowText.Of(screen, 8, 0, 40).TrimEnd());

        using var runningScreen = new Screen(cols: 40, rows: 11);
        var running = new BackgroundTaskDetailDialog(
            new BackgroundTaskEntry("fork", "f-2", "测试", "running"), nowMs: 10_000);

        running.Render(runningScreen, new Rect(0, 0, 40, 11), Theme.Dark);

        Assert.Equal(" 终态时间  —", RowText.Of(runningScreen, 8, 0, 40).TrimEnd());
    }

    /// <summary>描述超宽折行 — 词边界折行（PermissionTextWrap 复用，显示列宽计量）续行缩进对齐值列 col 11 +
    /// MeasureHeight 随折行数增长（MeasureHeight/Render 同参数防行数漂移）。
    /// 描述用汉字词组 — 值区宽 13 显示列："检查构建" 8 列收首行，
    /// "验证发布" 8+1+8=17 &gt; 13 换行 → 描述 2 行（宽字符 2 cell 语义下折行正确不超宽）。</summary>
    [Fact]
    public void Render_LongDescription_WrapsAtValueColumn()
    {
        using var screen = new Screen(cols: 24, rows: 12);
        var dialog = new BackgroundTaskDetailDialog(
            new BackgroundTaskEntry("fork", "f-1", "检查构建 验证发布", "running"), nowMs: 10_000);

        // 值区宽 = 24 - 11 = 13 显示列：第一行收 "检查构建"（8 列），"验证发布"（8+1+8=17 > 13）换行 → 描述 2 行
        Assert.Equal(12, dialog.MeasureHeight(24));
        dialog.Render(screen, new Rect(0, 0, 24, 12), Theme.Dark);

        Assert.Equal(" 任务描述  检查构建", RowText.Of(screen, 6, 0, 24).TrimEnd());
        // 续行缩进对齐值列（col 11 起 — 与字段值共用起点，8 列 ≤ 13 不被 Screen 截断）
        Assert.Equal("验证发布", RowText.Of(screen, 7, 11, 13).TrimEnd());
        // 折行后状态/终态/提示行随之下移一行（row 8/9/11）
        Assert.Equal(" 任务状态  运行中", RowText.Of(screen, 8, 0, 24).TrimEnd());
        Assert.Equal(" esc 返回 · left 关闭", RowText.Of(screen, 11, 0, 24).TrimEnd());
    }

    /// <summary>底部提示行 — "esc 返回 · left 关闭" dim（Byline 形态对齐 RenderModelShortcuts）；
    /// 字段行标签 dim / 值默认样式分色断言。</summary>
    [Fact]
    public void Render_HintRow_EscReturnLeftClose_Dim()
    {
        using var screen = new Screen(cols: 40, rows: 11);
        var dialog = new BackgroundTaskDetailDialog(
            new BackgroundTaskEntry("teammate", "tm-1", "审阅", "running"), nowMs: 1_000);

        dialog.Render(screen, new Rect(0, 0, 40, 11), Theme.Dark);

        Assert.Equal(" esc 返回 · left 关闭", RowText.Of(screen, 10, 0, 40).TrimEnd());
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[10, 1].StyleId);
        // 字段行：标签 dim / 值默认样式（screen[4, 1] = "任" 字位置，screen[4, 11] = 值首字符）
        Assert.Equal(dim, screen[4, 1].StyleId);
        Assert.Equal(0, screen[4, 11].StyleId);
    }
}
