namespace NativeTui.Tests.terminal;

/// <summary>
/// TASK077 — TerminalViewportProbe 坐标转换数学测试。
/// inline 模式下 SGR 鼠标坐标（终端视口坐标）与 Screen 内容坐标的偏移转换。
/// </summary>
public class TerminalViewportProbeTests
{
    /// <summary>
    /// 场景：应用首帧从终端 scrollback row 7 起画（用户在 shell 输入了命令），
    /// 渲染 4 行后光标在 scrollback row 10（contentCursorRow=3）。
    /// 终端视口顶部在 scrollback row 0（无滚动）。
    /// 用户点击 SGR row=10（视口坐标），应映射到 Screen row=3（PromptContextItem）。
    /// </summary>
    [Fact]
    public void ComputeViewportOffset_FirstFrameFromScrollbackRow7_ClickRow10_MapsToScreenRow3()
    {
        var cursorScrollbackRow = 10;
        var viewportTopScrollbackRow = 0;
        var contentCursorRow = 3;

        var offset = TerminalViewportProbe.ComputeViewportOffset(cursorScrollbackRow, viewportTopScrollbackRow, contentCursorRow);
        var screenRow = TerminalViewportProbe.ScreenRowFromSgr(sgrRow: 10, offset);

        Assert.Equal(3, screenRow);
        Assert.Equal(7, offset);
    }

    /// <summary>
    /// 场景：应用内容超过视口，部分内容滚入 scrollback。
    /// 光标在 scrollback row 30，contentCursorRow=25（Screen 25 行），视口顶在 scrollback row 5。
    /// Screen row 0 对应 scrollback row 5（30-25=5），视口偏移 = 5-5 = 0。
    /// SGR row=0（视口顶）映射到 Screen row=0。
    /// </summary>
    [Fact]
    public void ComputeViewportOffset_ContentExceedsViewport_ScrollbackAligned_ZeroOffset()
    {
        var cursorScrollbackRow = 30;
        var viewportTopScrollbackRow = 5;
        var contentCursorRow = 25;

        var offset = TerminalViewportProbe.ComputeViewportOffset(cursorScrollbackRow, viewportTopScrollbackRow, contentCursorRow);
        var screenRow = TerminalViewportProbe.ScreenRowFromSgr(sgrRow: 0, offset);

        Assert.Equal(0, offset);
        Assert.Equal(0, screenRow);
    }

    /// <summary>
    /// 场景：用户向上滚动视口查看历史。
    /// 光标在 scrollback row 30，contentCursorRow=25，视口顶在 scrollback row 0（滚到顶）。
    /// Screen row 0 对应 scrollback row 5（30-25=5），视口偏移 = 5-0 = 5。
    /// SGR row=0（视口顶）映射到 Screen row=-5（scrollback 中，不在当前帧 — HitTest 返回 null）。
    /// </summary>
    [Fact]
    public void ComputeViewportOffset_UserScrolledUp_ScreenRowNegative_HitTestMisses()
    {
        var cursorScrollbackRow = 30;
        var viewportTopScrollbackRow = 0;
        var contentCursorRow = 25;

        var offset = TerminalViewportProbe.ComputeViewportOffset(cursorScrollbackRow, viewportTopScrollbackRow, contentCursorRow);
        var screenRow = TerminalViewportProbe.ScreenRowFromSgr(sgrRow: 0, offset);

        Assert.Equal(5, offset);
        Assert.Equal(-5, screenRow);
    }

    /// <summary>
    /// 场景：视口模式（非 inline）— 首帧清屏归位，Screen row 0 = 视口 row 0。
    /// 光标在视口 row 3，contentCursorRow=3，视口顶 scrollback row 0。
    /// 偏移 = (3-3) - 0 = 0。SGR row=3 映射到 Screen row=3。
    /// </summary>
    [Fact]
    public void ComputeViewportOffset_ViewportMode_ZeroOffset()
    {
        var cursorScrollbackRow = 3;
        var viewportTopScrollbackRow = 0;
        var contentCursorRow = 3;

        var offset = TerminalViewportProbe.ComputeViewportOffset(cursorScrollbackRow, viewportTopScrollbackRow, contentCursorRow);
        var screenRow = TerminalViewportProbe.ScreenRowFromSgr(sgrRow: 3, offset);

        Assert.Equal(0, offset);
        Assert.Equal(3, screenRow);
    }

    /// <summary>
    /// 场景：fullReset 后（清屏 + 归位），Screen row 0 = 视口 row 0。
    /// 光标在 scrollback row 4，contentCursorRow=4，视口顶 scrollback row 0。
    /// 偏移 = (4-4) - 0 = 0。
    /// </summary>
    [Fact]
    public void ComputeViewportOffset_AfterFullReset_ZeroOffset()
    {
        var cursorScrollbackRow = 4;
        var viewportTopScrollbackRow = 0;
        var contentCursorRow = 4;

        var offset = TerminalViewportProbe.ComputeViewportOffset(cursorScrollbackRow, viewportTopScrollbackRow, contentCursorRow);

        Assert.Equal(0, offset);
    }

    /// <summary>
    /// 场景：调试数据复现 — PromptContextItem 在 Screen row 3，用户点击 SGR row 10。
    /// 偏移应为 7（应用首帧从终端 row 7 起画）。
    /// </summary>
    [Fact]
    public void ComputeViewportOffset_DebugRepro_PromptContextItemAtScreenRow3_ClickSgrRow10()
    {
        var cursorScrollbackRow = 11;
        var viewportTopScrollbackRow = 0;
        var contentCursorRow = 4;

        var offset = TerminalViewportProbe.ComputeViewportOffset(cursorScrollbackRow, viewportTopScrollbackRow, contentCursorRow);
        var screenRow = TerminalViewportProbe.ScreenRowFromSgr(sgrRow: 10, offset);

        Assert.Equal(7, offset);
        Assert.Equal(3, screenRow);
    }
}
