namespace NativeTui.Tests.Repl;

/// <summary>
/// 用户消息换行符渲染测试（TASK069 B-3）：正文含 \n 时不得被 WriteText 零宽吞并
/// （"a\nb" 曾渲染为 "ab" 无分隔 — '\n' 宽度 0 被跳过）；1 行高度语义保持下
/// 换行渲染为空格分隔（多行完整支持属 TASK069 差异项 M1 待决策）。
/// </summary>
public sealed class UserMessageNewlineRenderTests
{
    /// <summary>换行符渲染为空格分隔 — "a\nb" 屏上为 "a b" 非 "ab"。</summary>
    [Fact]
    public void NewlineInText_RendersAsSpaceSeparator()
    {
        using var screen = new Screen(cols: 30, rows: 5);
        var item = new UserMessageItem("a\nb");
        item.Render(screen, new Rect(0, 1, 30, 2), Theme.Dark);

        // 正文起点 col 2（❯ + 空格后）：'a' 空格 'b'
        Assert.Equal(new Rune('a'), screen[2, 2].Value);
        Assert.Equal(new Rune(' '), screen[2, 3].Value);
        Assert.Equal(new Rune('b'), screen[2, 4].Value);
    }

    /// <summary>CRLF 归一 — "a\r\nb" 渲染为 "a b"（\r\n 折叠单空格，非双空格非吞并）。</summary>
    [Fact]
    public void CrlfInText_RendersAsSingleSpaceSeparator()
    {
        using var screen = new Screen(cols: 30, rows: 5);
        var item = new UserMessageItem("a\r\nb");
        item.Render(screen, new Rect(0, 1, 30, 2), Theme.Dark);

        Assert.Equal(new Rune('a'), screen[2, 2].Value);
        Assert.Equal(new Rune(' '), screen[2, 3].Value);
        Assert.Equal(new Rune('b'), screen[2, 4].Value);
    }
}
