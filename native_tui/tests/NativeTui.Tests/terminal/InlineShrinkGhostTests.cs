using NativeTui.Tests.Terminal;

namespace NativeTui.Tests.Terminal;

/// <summary>
/// inline 收缩残影门禁（组装器层）—— 帧高变矮时旧内容必须真的消失：
/// ① 有富余视口：走差量收缩（擦底行 + 重写移位行），不得 fullReset；
/// ② 恰好填满视口：顶行已被每行收尾的 CRLF 顶进 scrollback（物理不可变），
///    任何"改写该行"的尝试都会被终端钳制在视口顶 → 必须 fullReset 重锚（<see cref="InlineComposer"/> 契约）。
/// 断言用 <see cref="VirtualTerminal"/> 真画出来，而不是看字符串猜画面。
/// </summary>
public sealed class InlineShrinkGhostTests
{
    private static readonly CursorPos TopLeft = new(0, 0);

    /// <summary>有富余视口 — 收缩只擦底部腾空行，旧顶行内容彻底消失且不整屏清。</summary>
    [Fact]
    public void Shrink_WithViewportSlack_ErasesBottomRowAndLeavesNoGhost()
    {
        using var prev = new Screen(10, 3);
        prev.WriteText(0, 0, "AAA");
        prev.WriteText(1, 0, "BBB");
        prev.WriteText(2, 0, "CCC");

        var terminal = new VirtualTerminal(10, 6);
        var composer = new InlineComposer(hyperlinksSupported: false);
        terminal.Feed(composer.Compose(null, prev, viewportRows: 6, TopLeft, new CursorPos(0, 2), out var cursor));

        using var next = new Screen(10, 2);
        next.WriteText(0, 0, "BBB");
        next.WriteText(1, 0, "CCC");
        var shrink = composer.Compose(prev, next, viewportRows: 6, cursor, new CursorPos(0, 1), out _);
        terminal.Feed(shrink);

        Assert.DoesNotContain("\x1b[2J", shrink);
        Assert.DoesNotContain("\x1b[3J", shrink);
        Assert.Contains("\x1b[2K", shrink);
        Assert.True(terminal.FindAnywhere("AAA") < 0);
        var viewport = terminal.Viewport();
        Assert.Equal("BBB", viewport[0]);
        Assert.Equal("CCC", viewport[1]);
        Assert.Equal(string.Empty, viewport[2]);
    }

    /// <summary>
    /// 恰好填满视口（无富余行）— 顶行已在 scrollback，改它 = 相对 CUU 被钳制 → 行错位残影；
    /// 正确处理是 fullReset（2J+3J 绝对锚定整帧重绘）。
    /// </summary>
    [Fact]
    public void Shrink_ViewportExactlyFitsFrame_FullResetsInsteadOfCorrupting()
    {
        using var prev = new Screen(10, 3);
        prev.WriteText(0, 0, "AAA");
        prev.WriteText(1, 0, "BBB");
        prev.WriteText(2, 0, "CCC");

        var terminal = new VirtualTerminal(10, 3);
        var composer = new InlineComposer(hyperlinksSupported: false);
        terminal.Feed(composer.Compose(null, prev, viewportRows: 3, TopLeft, new CursorPos(0, 2), out var cursor));

        using var next = new Screen(10, 2);
        next.WriteText(0, 0, "BBB");
        next.WriteText(1, 0, "CCC");
        var shrink = composer.Compose(prev, next, viewportRows: 3, cursor, new CursorPos(0, 1), out _);

        Assert.Contains("\x1b[2J", shrink);
        terminal.Feed(shrink);
        var viewport = terminal.Viewport();
        Assert.Equal("BBB", viewport[0]);
        Assert.Equal("CCC", viewport[1]);
        Assert.True(terminal.FindAnywhere("AAA") < 0);
    }

    /// <summary>增长方向不受影响 — 追加行不擦不清（回归护栏，防收缩门禁误伤）。</summary>
    [Fact]
    public void Growth_StillAppendsWithoutClearing()
    {
        using var prev = new Screen(10, 1);
        prev.WriteText(0, 0, "AAA");

        var terminal = new VirtualTerminal(10, 6);
        var composer = new InlineComposer(hyperlinksSupported: false);
        terminal.Feed(composer.Compose(null, prev, viewportRows: 6, TopLeft, new CursorPos(0, 0), out var cursor));

        using var next = new Screen(10, 2);
        next.WriteText(0, 0, "AAA");
        next.WriteText(1, 0, "DDD");
        var grow = composer.Compose(prev, next, viewportRows: 6, cursor, new CursorPos(0, 1), out _);
        terminal.Feed(grow);

        Assert.DoesNotContain("\x1b[2J", grow);
        var viewport = terminal.Viewport();
        Assert.Equal("AAA", viewport[0]);
        Assert.Equal("DDD", viewport[1]);
    }
}
