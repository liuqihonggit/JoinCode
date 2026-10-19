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
/// 上下全宽纯 ─ 分隔线（borderStyle=round 但 borderLeft/Right=false → 无角字符）
/// + ❯ 提示符（busy 时 dim）+ 光标（文本 offset 处字符反色，行尾为反色空格块，屏幕列 = 前缀 2 + offset）。
/// </summary>
public sealed class PromptInputBoxTests
{
    [Fact]
    public void Render_TopAndBottomFullWidthRules()
    {
        using var screen = new Screen(cols: 12, rows: 3);
        var box = new PromptInputBox();
        box.Render(screen, new Rect(0, 0, 12, 3), Theme.Dark, text: "hi", cursorCol: 2);
        var border = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.PromptBorder });
        // 顶边与底边均为全宽纯 ─（render-border borderLeft/Right=false 时角字符为空串）
        Assert.Equal("────────────", RowText.Of(screen, 0, 0, 12));
        Assert.Equal("────────────", RowText.Of(screen, 2, 0, 12));
        Assert.Equal(border, screen[0, 0].StyleId);
        Assert.Equal(border, screen[2, 11].StyleId);
        // 中间行 ❯ hi（❯ 默认色，非 busy 不 dim）
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
/// 底部状态栏 golden 测试 — 像素级对齐基准 PromptInputFooter：
/// 行1 左侧 PromptInputFooterLeftSide（激活模式 `{symbol} {title} on` 模式色 + dim ` (shift+tab to cycle)`
/// + busy dim ` · esc to interrupt` / 非 busy agents 导航提示 dim ` · ← for agents`（v2.1.303 实测）；
/// 默认模式 dim `? for shortcuts`）；行1 右侧 Notifications 登录错误（error 色右对齐）；
/// 行2 右侧通知（如 effort `◐ medium · /effort`，dim 右对齐，Notification 非空时占行）。
/// </summary>
public sealed class FooterBarTests
{
    [Fact]
    public void AcceptEditsMode_SymbolTitleOnWithCycleHint()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState { Mode = PermissionMode.AcceptEdits });
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
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState());
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("? for shortcuts", RowText.Of(screen, 0, 0, 60).TrimEnd());
        Assert.Equal(dim, screen[0, 0].StyleId);
    }

    [Fact]
    public void BusyMode_AppendsInterruptHintWithMiddot()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState { Mode = PermissionMode.AcceptEdits, Busy = true });
        var text = RowText.Of(screen, 0, 0, 60).TrimEnd();
        Assert.StartsWith("⏵⏵ accept edits on", text);
        Assert.Contains(" · esc to interrupt", text);
    }

    [Fact]
    public void AutoMode_WarningColorAndAgentsHintSuffix()
    {
        using var screen = new Screen(cols: 80, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 80, 1), Theme.Dark, new FooterState { Mode = PermissionMode.Auto, AgentsHint = "← for agents" });
        var warning = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Warning });
        var text = RowText.Of(screen, 0, 0, 80).TrimEnd();
        // v2.1.303 实测：auto mode on (shift+tab to cycle) · ← for agents
        Assert.Equal("⏵⏵ auto mode on (shift+tab to cycle) · ← for agents", text);
        Assert.Equal(warning, screen[0, 0].StyleId);
    }

    [Fact]
    public void LoginError_RightAlignedInErrorColor()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState { Mode = PermissionMode.Auto, LoginError = "Not logged in · Run /login" });
        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        // 右对齐：右端贴 frame.Right（60），文本 26 列 → 起始 col 34
        Assert.Equal("Not logged in · Run /login", RowText.Of(screen, 0, 34, 26));
        Assert.Equal(error, screen[0, 34].StyleId);
        Assert.Equal(error, screen[0, 59].StyleId);
    }

    [Fact]
    public void Notification_SecondRowRightAlignedDim()
    {
        using var screen = new Screen(cols: 60, rows: 2);
        var footer = new FooterBar();
        var state = new FooterState { Mode = PermissionMode.Auto, Notification = "◐ medium · /effort" };
        footer.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark, state);
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        // 行2 右对齐：文本 18 列 → 起始 col 42（Right=60）
        Assert.Equal("◐ medium · /effort", RowText.Of(screen, 1, 42, 18));
        Assert.Equal(dim, screen[1, 42].StyleId);
    }

    [Fact]
    public void MeasureHeight_TwoRowsOnlyWithNotification()
    {
        var footer = new FooterBar();
        Assert.Equal(1, footer.MeasureHeight(new FooterState { Mode = PermissionMode.Auto }));
        Assert.Equal(2, footer.MeasureHeight(new FooterState { Notification = "◐ medium · /effort" }));
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
