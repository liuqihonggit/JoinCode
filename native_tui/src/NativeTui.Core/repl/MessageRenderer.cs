namespace NativeTui.Core.Repl;

/// <summary>
/// 消息渲染器 — 像素级对齐基准消息组件（M5 REPL 主界面）：
/// RenderUser → UserPromptMessage（整行灰底 + ❯ subtle 色 + 正文 text 色 + paddingRight=1）；
/// RenderAssistant → AssistantTextMessage（● text 色 + minWidth 2 + 正文）；
/// RenderToolCall → AssistantToolUseMessage（● + bold 工具名 + 默认样式 (参数摘要)）；
/// RenderToolResult → MessageResponse（"  ⎿  " 前缀整体 dim + 默认正文）。
/// </summary>
public static class MessageRenderer
{
    /// <summary>
    /// 渲染用户消息：灰底满行宽（Box stretch），❯ 前缀 subtle 色（无 paddingLeft），
    /// 正文 text 色，paddingRight=1（正文区右边界 = frame.Right - 1）。
    /// </summary>
    public static void RenderUser(Screen screen, Rect frame, Theme theme, string text)
    {
        var background = theme.UserMessageBackground;
        var fill = screen.Styles.GetOrAdd(new TextStyle { Background = background });
        for (var col = frame.X; col < frame.Right; col++)
        {
            screen[frame.Y, col] = new Cell(new Rune(' '), fill, CellWidth.Default);
        }

        var pointer = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Subtle, Background = background });
        var body = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text, Background = background });
        var textRight = frame.Right - 1;
        screen.WriteText(frame.Y, frame.X, $"{Figures.Pointer} ", pointer, textRight);
        screen.WriteText(frame.Y, frame.X + 2, text, body, textRight);
    }

    /// <summary>渲染助手文本消息：● + 空格（minWidth 2）+ 正文，全部 text 色。</summary>
    public static void RenderAssistant(Screen screen, Rect frame, Theme theme, string text)
    {
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text });
        screen.WriteText(frame.Y, frame.X, $"{Figures.MessageBullet} ", style, frame.Right);
        screen.WriteText(frame.Y, frame.X + 2, text, style, frame.Right);
    }

    /// <summary>渲染工具调用消息：● + 空格 + bold 工具名 + 默认样式 (参数摘要)，名字与摘要间无间隔。</summary>
    public static void RenderToolCall(Screen screen, Rect frame, Theme theme, string toolName, string args)
    {
        var bullet = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Text });
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(frame.Y, frame.X, $"{Figures.MessageBullet} ", bullet, frame.Right);
        var col = screen.WriteText(frame.Y, frame.X + 2, toolName, bold, frame.Right);
        screen.WriteText(frame.Y, col, $"({args})", 0, frame.Right);
    }

    /// <summary>渲染工具结果消息："  ⎿  " 前缀整体 dim（单一 Text span），正文默认样式。</summary>
    public static void RenderToolResult(Screen screen, Rect frame, Theme theme, string text)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(frame.Y, frame.X, Figures.ToolResultIndent, dim, frame.Right);
        screen.WriteText(frame.Y, frame.X + Figures.ToolResultIndent.Length, text, 0, frame.Right);
    }
}
