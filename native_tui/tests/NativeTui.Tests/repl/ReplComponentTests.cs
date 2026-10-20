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

    [Fact]
    public void Render_GhostSuffixDimmedAfterText()
    {
        using var screen = new Screen(cols: 20, rows: 3);
        var box = new PromptInputBox();
        box.Render(screen, new Rect(0, 0, 20, 3), Theme.Dark, text: "run /comp", cursorCol: 9, ghostSuffix: "act");
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        // ghost "act" 自文本末尾列（2 前缀 + 9 文本 = 列 11）起；光标也在列 11 → 首字符 'a' 反色（基准光标覆盖 ghost 起点）
        Assert.Equal("act", RowText.Of(screen, 1, 11, 3));
        Assert.Equal(inverse, screen[1, 11].StyleId);
        // 后续 ghost 字符 dim 渲染
        Assert.Equal(dim, screen[1, 12].StyleId);
        Assert.Equal(dim, screen[1, 13].StyleId);
        // 正文保持默认样式
        Assert.Equal(0, screen[1, 2].StyleId);
    }

    [Fact]
    public void Render_GhostSuffixTruncatedToFrame()
    {
        using var screen = new Screen(cols: 8, rows: 3);
        var box = new PromptInputBox();
        // 文本 "hi"（占列 2-3），ghost "xyz" 从列 4 起，frame 右界 8 → 只渲染 "xyz" 完整放下前 4 列
        box.Render(screen, new Rect(0, 0, 8, 3), Theme.Dark, text: "hi", cursorCol: 2, ghostSuffix: "xyzw");
        Assert.Equal("xyzw", RowText.Of(screen, 1, 4, 4));
    }

    [Fact]
    public void Render_EmptyGhostSuffix_NoExtraCells()
    {
        using var screen = new Screen(cols: 12, rows: 3);
        var box = new PromptInputBox();
        box.Render(screen, new Rect(0, 0, 12, 3), Theme.Dark, text: "hi", cursorCol: 2, ghostSuffix: "");
        // 空 ghost：光标列（4）之外无渲染（列 5 为空白）
        Assert.Equal(Cell.Blank, screen[1, 5]);
    }
}

/// <summary>
/// 底部状态栏 golden 测试 — 像素级对齐基准 PromptInputFooter（文案汉化 + jcc 引擎模式集合）：
/// 行1 左侧（激活模式 `{symbol} {中文标题} 已开启` 模式色 + dim `（shift+tab 切换）`
/// + busy dim ` · esc 打断` / 非 busy agents 导航提示 dim ` · {AgentsHint}`）；
/// 标准模式（auto，引擎默认）dim `? 查看快捷键`（busy 时 `esc 打断`）；
/// 行1 右侧 Notifications 登录错误（error 色右对齐）；
/// 行2 右侧通知（dim 右对齐，Notification 非空时占行）。
/// </summary>
public sealed class FooterBarTests
{
    [Fact]
    public void PlanMode_SymbolTitleOnWithCycleHint()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState { Mode = PermissionMode.Plan });
        var modeStyle = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.PlanMode });
        var text = RowText.Of(screen, 0, 0, 60).TrimEnd();
        Assert.StartsWith("⏸ 计划模式 已开启", text);
        Assert.Contains("（shift+tab 切换）", text);
        Assert.Equal(modeStyle, screen[0, 0].StyleId);
    }

    [Fact]
    public void BypassMode_ErrorColorSymbolTitle()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState { Mode = PermissionMode.Bypass });
        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        var text = RowText.Of(screen, 0, 0, 60).TrimEnd();
        Assert.StartsWith("⏵⏵ 全放行 已开启", text);
        Assert.Equal(error, screen[0, 0].StyleId);
    }

    [Fact]
    public void AutoMode_ShortcutsHintDim()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState());
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("? 查看快捷键", RowText.Of(screen, 0, 0, 60).TrimEnd());
        Assert.Equal(dim, screen[0, 0].StyleId);
    }

    [Fact]
    public void AutoModeBusy_InterruptHintDim()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState { Busy = true });
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("esc 打断", RowText.Of(screen, 0, 0, 60).TrimEnd());
        Assert.Equal(dim, screen[0, 0].StyleId);
    }

    [Fact]
    public void BusyMode_AppendsInterruptHintWithMiddot()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState { Mode = PermissionMode.Ask, Busy = true });
        var text = RowText.Of(screen, 0, 0, 60).TrimEnd();
        Assert.StartsWith("⏵⏵ 询问模式 已开启", text);
        Assert.Contains(" · esc 打断", text);
    }

    [Fact]
    public void UnattendedMode_WarningColorAndAgentsHintSuffix()
    {
        using var screen = new Screen(cols: 80, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 80, 1), Theme.Dark, new FooterState { Mode = PermissionMode.Unattended, AgentsHint = "← agents" });
        var warning = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Warning });
        var text = RowText.Of(screen, 0, 0, 80).TrimEnd();
        Assert.Equal("⏵⏵ 无人值守 已开启（shift+tab 切换） · ← agents", text);
        Assert.Equal(warning, screen[0, 0].StyleId);
    }

    [Fact]
    public void LoginError_RightAlignedInErrorColor()
    {
        using var screen = new Screen(cols: 60, rows: 1);
        var footer = new FooterBar();
        footer.Render(screen, new Rect(0, 0, 60, 1), Theme.Dark, new FooterState { Mode = PermissionMode.Auto, LoginError = "未登录 · 运行 /login" });
        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        // 右对齐：文本 20 屏幕列（中文每字 2 列）→ 起始 col 40（Right=60）
        Assert.Equal("未登录 · 运行 /login", RowText.Of(screen, 0, 40, 20));
        Assert.Equal(error, screen[0, 40].StyleId);
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
    /// <summary>拼接指定行 [col, col+width) 的字符值（跳过 SpacerTail 占位列 — 宽字符只输出 rune 一次，与 Screen 渲染语义一致）。</summary>
    public static string Of(Screen screen, int row, int col, int width)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < width; i++)
        {
            if (screen[row, col + i].Width == CellWidth.SpacerTail)
            {
                continue;
            }

            builder.Append(screen[row, col + i].Value.ToString());
        }

        return builder.ToString();
    }
}
