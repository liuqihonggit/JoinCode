namespace NativeTui.Tests.Repl;

/// <summary>
/// REPL 整屏组装 golden 测试 — 对齐基准 REPL.tsx bottom slot 布局（自底向上）：
/// footer 1 行（paddingX=2）+ 输入底边线 1 行 + 输入文本 1 行 + 输入顶边线 1 行
/// + 间隔 1 行（输入框 marginTop=1），其余全部给消息滚动区（sticky 钉底，消息项 = marginTop 1 + 正文 1）。
/// </summary>
public sealed class ReplViewTests
{
    [Fact]
    public void Layout_MessagesGapInputFooter()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var view = new ReplView { Input = "hi", CursorCol = 2, Footer = new FooterState { Mode = PermissionMode.Ask } };
        view.Messages.Add(new UserMessageItem("hello world"));
        view.Render(screen, Theme.Dark);

        // footer：最后 1 行，col 2 起（paddingX=2）
        Assert.StartsWith("⏵⏵ 询问模式 已开启", RowText.Of(screen, 9, 2, 38).TrimEnd());
        // 输入顶边线：行 6（H-4）全宽纯 ─；底边线：行 8（H-2）
        Assert.Equal(new string('─', 40), RowText.Of(screen, 6, 0, 40));
        Assert.Equal(new string('─', 40), RowText.Of(screen, 8, 0, 40));
        // 输入文本行 7：❯ hi + 光标（offset 2 → 屏幕列 4 反色空格）
        Assert.Equal("❯ hi", RowText.Of(screen, 7, 0, 4).TrimEnd());
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        Assert.Equal(inverse, screen[7, 4].StyleId);
        // 间隔行 5（输入框 marginTop=1）
        Assert.Equal(Cell.Blank, screen[5, 0]);
        // 消息区行 0-4：margin 行 0 空白 + 灰底正文行 1
        Assert.Equal(Cell.Blank, screen[0, 0]);
        Assert.Equal("❯ hello world", RowText.Of(screen, 1, 0, 13));
        var bg = screen.Styles.GetOrAdd(new TextStyle { Background = Theme.Dark.UserMessageBackground });
        Assert.Equal(bg, screen[1, 13].StyleId);
    }

    [Fact]
    public void ManyMessages_StickyShowsLast()
    {
        using var screen = new Screen(cols: 20, rows: 24);
        var view = new ReplView();
        for (var i = 0; i < 15; i++)
        {
            view.Messages.Add(new UserMessageItem($"m{i}"));
        }

        view.Render(screen, Theme.Dark);
        // 消息区高 19（24-5），15 条×2 行 = 30 行，sticky → scrollTop=11，可见 m5..m14
        Assert.Equal("❯ m5", RowText.Of(screen, 0, 0, 5).TrimEnd());
        Assert.Equal("❯ m14", RowText.Of(screen, 18, 0, 6).TrimEnd());
        // 输入框/footer 不被消息挤占
        Assert.Equal('─', (char)screen[20, 0].Value.Value);
        Assert.StartsWith("? 查看快捷键", RowText.Of(screen, 23, 2, 18).TrimEnd());
    }

    [Fact]
    public void Busy_DimPointerAndInterruptHint()
    {
        using var screen = new Screen(cols: 80, rows: 10);
        var view = new ReplView { Input = "", CursorCol = 0, Footer = new FooterState { Busy = true, Mode = PermissionMode.Ask } };
        view.Render(screen, Theme.Dark);
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[7, 0].StyleId);
        Assert.Contains(" · esc 打断", RowText.Of(screen, 9, 2, 78).TrimEnd());
    }

    [Fact]
    public void FooterNotification_TwoRowFooterShiftsInputUp()
    {
        using var screen = new Screen(cols: 60, rows: 10);
        var view = new ReplView
        {
            Footer = new FooterState { Mode = PermissionMode.Plan, AgentsHint = "← agents", LoginError = "未登录 · 运行 /login", Notification = "◐ 中等 · /effort" },
        };
        view.Render(screen, Theme.Dark);
        // footer 两行（行 8-9）：行 8 左模式串 + 右登录错误，行 9 右通知
        // 右对齐基准 = footer frame.Right = 58（paddingX=2：X=2 宽 Cols-4）
        Assert.StartsWith("⏸ 计划模式 已开启", RowText.Of(screen, 8, 2, 40).TrimEnd());
        // 登录错误 20 屏幕列（中文每字 2 列）→ 起始 col 38（Right=58）
        Assert.Equal("未登录 · 运行 /login", RowText.Of(screen, 8, 38, 20));
        // 通知 16 屏幕列（◐ 1 + 空格 1 + 中等 4 + " · " 3 + /effort 7）→ 起始 col 42
        Assert.Equal("◐ 中等 · /effort", RowText.Of(screen, 9, 42, 16));
        // 输入区整体上移 1 行：底边线 7 / 文本 6 / 顶边线 5
        Assert.Equal(new string('─', 60), RowText.Of(screen, 5, 0, 60));
        Assert.Equal(new string('─', 60), RowText.Of(screen, 7, 0, 60));
        Assert.Equal('❯', (char)screen[6, 0].Value.Value);
    }

    [Fact]
    public void MixedMessages_AllItemKindsRender()
    {
        using var screen = new Screen(cols: 40, rows: 24);
        var view = new ReplView();
        view.Messages.Add(new UserMessageItem("question"));
        view.Messages.Add(new AssistantMessageItem("answer"));
        view.Messages.Add(new ToolCallItem("Read", "src/main.rs"));
        view.Messages.Add(new ToolResultItem("42 lines"));
        view.Render(screen, Theme.Dark);

        // 内容 8 行（4 条×2）< 消息区 20 行 → 从顶部渲染，正文在行 1/3/5/7
        Assert.Equal("❯ question", RowText.Of(screen, 1, 0, 10).TrimEnd());
        Assert.Equal($"{Figures.MessageBullet} answer", RowText.Of(screen, 3, 0, 8).TrimEnd());
        Assert.Equal($"{Figures.MessageBullet} Read(src/main.rs)", RowText.Of(screen, 5, 0, 20).TrimEnd());
        Assert.Equal($"{Figures.ToolResultIndent}42 lines", RowText.Of(screen, 7, 0, 13).TrimEnd());
    }

    [Fact]
    public void MeasureContentHeight_MessagesPlusGapInputFooter()
    {
        var view = new ReplView { Footer = new FooterState { Mode = PermissionMode.Auto } };
        view.Messages.Add(new UserMessageItem("hello"));
        view.Messages.Add(new UserMessageItem("world"));
        // 消息 2×2 + 间隔 1 + 输入区 3 + footer 1 = 9
        Assert.Equal(9, view.MeasureContentHeight(cols: 40));
    }

    [Fact]
    public void MeasureContentHeight_TwoRowFooter_AddsOneRow()
    {
        var view = new ReplView { Footer = new FooterState { Notification = "◐ medium · /effort" } };
        view.Messages.Add(new UserMessageItem("hello"));
        // 消息 2 + 间隔 1 + 输入区 3 + footer 2 = 8
        Assert.Equal(8, view.MeasureContentHeight(cols: 40));
    }
}
