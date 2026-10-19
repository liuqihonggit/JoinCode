namespace NativeTui.Tests.Repl;

/// <summary>
/// 消息渲染 golden 测试 — 像素级对齐基准消息组件：
/// UserPromptMessage（整行灰底 + ❯ subtle 色 + 正文 text 色 + paddingRight=1，无 paddingLeft）、
/// AssistantTextMessage（● text 色 + minWidth 2 + 正文）、
/// AssistantToolUseMessage（● + bold 工具名 + 默认样式 (参数摘要)）、
/// MessageResponse（"  ⎿  " 前缀整体 dim + 默认正文）。
/// </summary>
public sealed class MessageRendererTests
{
    [Fact]
    public void UserMessage_FullRowGraySubtlePointerTextBody()
    {
        using var screen = new Screen(cols: 20, rows: 1);
        MessageRenderer.RenderUser(screen, new Rect(0, 0, 20, 1), Theme.Dark, "hi");
        var bg = screen.Styles.GetOrAdd(new TextStyle { Background = Theme.Dark.UserMessageBackground });
        var pointer = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Subtle, Background = Theme.Dark.UserMessageBackground });
        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text, Background = Theme.Dark.UserMessageBackground });
        // ❯ 从 col 0 开始（基准 Box 无 paddingLeft），subtle 色 + 后跟 1 空格同 span
        Assert.Equal(new Rune(Figures.Pointer[0]), screen[0, 0].Value);
        Assert.Equal(pointer, screen[0, 0].StyleId);
        Assert.Equal(new Rune(' '), screen[0, 1].Value);
        Assert.Equal(pointer, screen[0, 1].StyleId);
        // 正文 text 色
        Assert.Equal(new Rune('h'), screen[0, 2].Value);
        Assert.Equal(body, screen[0, 2].StyleId);
        // 右 padding 1 空格 + 灰底填充整行（Box stretch 满行宽）
        Assert.Equal(bg, screen[0, 4].StyleId);
        Assert.Equal(bg, screen[0, 6].StyleId);
        Assert.Equal(bg, screen[0, 19].StyleId);
        Assert.Equal($"{Figures.Pointer} hi ", RowText.Of(screen, 0, 0, 5));
    }

    [Fact]
    public void AssistantMessage_BulletAndBodyInTextColor()
    {
        using var screen = new Screen(cols: 20, rows: 1);
        MessageRenderer.RenderAssistant(screen, new Rect(0, 0, 20, 1), Theme.Dark, "hello");
        var text = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text });
        Assert.Equal(new Rune(Figures.MessageBullet[0]), screen[0, 0].Value);
        Assert.Equal(text, screen[0, 0].StyleId);
        Assert.Equal(new Rune(' '), screen[0, 1].Value);
        Assert.Equal(new Rune('h'), screen[0, 2].Value);
        Assert.Equal(text, screen[0, 2].StyleId);
        Assert.Equal($"{Figures.MessageBullet} hello", RowText.Of(screen, 0, 0, 7));
    }

    [Fact]
    public void ToolCall_BoldToolNameAndDefaultArgs()
    {
        using var screen = new Screen(cols: 40, rows: 1);
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        MessageRenderer.RenderToolCall(screen, new Rect(0, 0, 40, 1), Theme.Dark, "Read", "src/main.rs");
        Assert.Equal(new Rune(Figures.MessageBullet[0]), screen[0, 0].Value);
        Assert.Equal(bold, screen[0, 2].StyleId);
        Assert.Equal("Read", RowText.Of(screen, 0, 2, 4));
        // 参数摘要紧跟 bold 段之后（无间隔），默认样式
        Assert.Equal(0, screen[0, 6].StyleId);
        Assert.Equal(new Rune('('), screen[0, 6].Value);
    }

    [Fact]
    public void ToolResult_DimIndentPrefix()
    {
        using var screen = new Screen(cols: 40, rows: 1);
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        MessageRenderer.RenderToolResult(screen, new Rect(0, 0, 40, 1), Theme.Dark, "42 lines");
        // "  ⎿  " 前缀整体 dim（基准 MessageResponse: dimColor 的单一 Text span），正文默认
        Assert.Equal(new Rune(' '), screen[0, 0].Value);
        Assert.Equal(new Rune('⎿'), screen[0, 2].Value);
        Assert.Equal(dim, screen[0, 2].StyleId);
        Assert.Equal(dim, screen[0, 4].StyleId);
        Assert.Equal(0, screen[0, 5].StyleId);
        Assert.Equal(new Rune('4'), screen[0, 5].Value);
    }

    [Fact]
    public void UserMessage_TruncatesToFrame()
    {
        using var screen = new Screen(cols: 5, rows: 1);
        MessageRenderer.RenderUser(screen, new Rect(0, 0, 5, 1), Theme.Dark, "long text here");
        // 灰底仍是整行 5 格（paddingRight=1，正文区 width-3）
        var bg = screen.Styles.GetOrAdd(new TextStyle { Background = Theme.Dark.UserMessageBackground });
        Assert.Equal(bg, screen[0, 4].StyleId);
    }
}

/// <summary>
/// 输入框 golden 测试 — 像素级对齐基准 PromptInput.tsx 标志性样式：
/// 仅顶部圆角单边框（PromptBorder 灰）+ ❯ 提示符（busy 时 dim）+
/// 光标（文本 offset 处字符反色，行尾为反色空格块，屏幕列 = 前缀 2 + offset）。
/// </summary>
public sealed class PromptInputBoxTests
{
    [Fact]
    public void Render_TopBorderWithPromptBorderGray()
    {
        using var screen = new Screen(cols: 12, rows: 3);
        var box = new PromptInputBox();
        box.Render(screen, new Rect(0, 0, 12, 3), Theme.Dark, text: "hi", cursorCol: 2);
        var border = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.PromptBorder });
        // 顶边 ╭──────────╮（仅顶边，cli-boxes round）
        Assert.Equal("╭──────────╮", RowText.Of(screen, 0, 0, 12));
        Assert.Equal(border, screen[0, 0].StyleId);
        // 第二行 ❯ hi（❯ 默认色，非 busy 不 dim）
        Assert.Equal(0, screen[1, 0].StyleId);
        Assert.Equal($"{Figures.Pointer} hi", RowText.Of(screen, 1, 0, 4).TrimEnd());
    }

    [Fact]
    public void Render_CursorAtEndIsInverseSpace()
    {
        using var screen = new Screen(cols: 12, rows: 3);
        var box = new PromptInputBox();
        box.Render(screen, new Rect(0, 0, 12, 3), Theme.Dark, text: "hi", cursorCol: 2);
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        // 光标在文本 offset 2（"hi" 之后）→ 屏幕列 = 前缀 2 + 2 = 4，反色空格块
        Assert.Equal(new Rune(' '), screen[1, 4].Value);
        Assert.Equal(inverse, screen[1, 4].StyleId);
    }

    [Fact]
    public void Render_CursorMidTextInvertsCharUnderCursor()
    {
        using var screen = new Screen(cols: 12, rows: 3);
        var box = new PromptInputBox();
        box.Render(screen, new Rect(0, 0, 12, 3), Theme.Dark, text: "hi", cursorCol: 1);
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        // 光标在 'h' 与 'i' 之间 → 'i'（屏幕列 3）反色
        Assert.Equal(new Rune('i'), screen[1, 3].Value);
        Assert.Equal(inverse, screen[1, 3].StyleId);
    }

    [Fact]
    public void Render_DimPointerWhenBusy()
    {
        using var screen = new Screen(cols: 12, rows: 3);
        var box = new PromptInputBox();
        box.Render(screen, new Rect(0, 0, 12, 3), Theme.Dark, text: "", cursorCol: 0, busy: true);
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(new Rune(Figures.Pointer[0]), screen[1, 0].Value);
        Assert.Equal(dim, screen[1, 0].StyleId);
    }
}

/// <summary>
/// 底部状态栏 golden 测试 — 像素级对齐基准 PromptInputFooterLeftSide：
/// 激活模式 `{symbol} {title} on`（模式色）+ dim ` (shift+tab to cycle)`；
/// busy 追加 dim ` · esc to interrupt`（分隔符为 U+00B7 middot）；
/// 默认模式 dim `? for shortcuts`（busy 时 `esc to interrupt`）。
/// spinner 属于输入框上方的独立行组件（基准 REPL.tsx scrollable 区），不在 footer。
/// </summary>
public sealed class FooterBarTests
{
    [Fact]
    public void AcceptEditsMode_SymbolTitleOnWithCycleHint()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, PermissionMode.AcceptEdits, busy: false);
        var modeStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.AutoAccept });
        var text = RowText.Of(screen, 0, 0, 60).TrimEnd();
        Assert.StartsWith("⏵⏵ accept edits on", text);
        Assert.Contains("(shift+tab to cycle)", text);
        Assert.Equal(modeStyle, screen[0, 0].StyleId);
    }

    [Fact]
    public void DefaultMode_ShortcutsHintDim()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, PermissionMode.Default, busy: false);
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("? for shortcuts", RowText.Of(screen, 0, 0, 60).TrimEnd());
        Assert.Equal(dim, screen[0, 0].StyleId);
    }

    [Fact]
    public void BusyMode_AppendsInterruptHintWithMiddot()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, PermissionMode.AcceptEdits, busy: true);
        var text = RowText.Of(screen, 0, 0, 60).TrimEnd();
        Assert.StartsWith("⏵⏵ accept edits on", text);
        Assert.Contains(" · esc to interrupt", text);
    }
}

/// <summary>行文本提取辅助（golden 断言用）。</summary>
internal static class RowText
{
    /// <summary>拼接指定行 [col, col+width) 的字符值。</summary>
    public static string Of(Screen screen, int row, int col, int width)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < width; i++)
        {
            builder.Append(screen[row, col + i].Value.ToString());
        }

        return builder.ToString();
    }
}
