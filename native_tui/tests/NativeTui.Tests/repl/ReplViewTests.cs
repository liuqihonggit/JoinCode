namespace NativeTui.Tests.Repl;

/// <summary>
/// REPL 整屏组装 golden 测试 — 对齐基准 REPL.tsx bottom slot 布局（自底向上）：
/// footer 1 行（paddingX=2）+ 输入文本 1 行 + 输入顶边框 1 行 + 间隔 1 行（marginTop），
/// 其余全部给消息滚动区（sticky 钉底，消息项 = marginTop 1 + 正文 1）。
/// </summary>
public sealed class ReplViewTests
{
    [Fact]
    public void Layout_MessagesGapInputFooter()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var view = new ReplView { Input = "hi", CursorCol = 2, Mode = PermissionMode.AcceptEdits };
        view.Messages.Add(new UserMessageItem("hello world"));
        view.Render(screen, Theme.Dark);

        // footer：最后 1 行，col 2 起（paddingX=2）
        Assert.StartsWith("⏵⏵ accept edits on", RowText.Of(screen, 9, 2, 38).TrimEnd());
        // 输入顶边框：行 7（H-3）全宽
        Assert.Equal($"╭{new string('─', 38)}╮", RowText.Of(screen, 7, 0, 40));
        // 输入文本行 8：❯ hi + 光标（offset 2 → 屏幕列 4 反色空格）
        Assert.Equal("❯ hi", RowText.Of(screen, 8, 0, 4).TrimEnd());
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        Assert.Equal(inverse, screen[8, 4].StyleId);
        // 间隔行 6（输入框 marginTop=1）
        Assert.Equal(Cell.Blank, screen[6, 0]);
        // 消息区行 0-5：margin 行 0 空白 + 灰底正文行 1
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
        // 消息区高 20（24-4），15 条×2 行 = 30 行，sticky → scrollTop=10，可见第 6..15 条
        Assert.Equal("❯ m5", RowText.Of(screen, 1, 0, 5).TrimEnd());
        Assert.Equal("❯ m14", RowText.Of(screen, 19, 0, 6).TrimEnd());
        // 输入框/footer 不被消息挤占
        Assert.Equal('╭', (char)screen[21, 0].Value.Value);
        Assert.StartsWith("? for shortcuts", RowText.Of(screen, 23, 2, 18).TrimEnd());
    }

    [Fact]
    public void Busy_DimPointerAndInterruptHint()
    {
        using var screen = new Screen(cols: 80, rows: 10);
        var view = new ReplView { Input = "", CursorCol = 0, Busy = true, Mode = PermissionMode.AcceptEdits };
        view.Render(screen, Theme.Dark);
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[8, 0].StyleId);
        Assert.Contains(" · esc to interrupt", RowText.Of(screen, 9, 2, 78).TrimEnd());
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
}
