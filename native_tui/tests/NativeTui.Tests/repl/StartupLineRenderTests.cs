namespace NativeTui.Tests.Repl;

/// <summary>
/// 启动行渲染门禁（TASK108 P12）— 真机现象：首帧「引擎启动中…」被画成 theme.Error 红色，
/// 正常启动进度看起来像启动失败。门禁锁两类启动行语义分色：
/// 状态行（引擎启动中）dim 灰；诊断行（[diag] 前缀，仅 JCC_DEBUGLOG=1）Warning 黄。
/// </summary>
public sealed class StartupLineRenderTests
{
    [Fact]
    public void Render_EngineStartingLine_UsesDimNotError()
    {
        using var screen = new Screen(cols: 40, rows: 2);
        var item = new StartupLineItem(ReplShell.EngineStartingLine);

        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);

        var dimId = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var errorId = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Equal(dimId, screen[0, 0].StyleId);
        Assert.NotEqual(errorId, screen[0, 0].StyleId);
    }

    [Fact]
    public void Render_EngineStartingLine_TextUnchanged()
    {
        using var screen = new Screen(cols: 40, rows: 2);
        var item = new StartupLineItem(ReplShell.EngineStartingLine);

        item.Render(screen, new Rect(0, 0, 40, 2), Theme.Dark);

        Assert.Equal(ReplShell.EngineStartingLine, RowText.Of(screen, 0, 0, 40).TrimEnd());
    }

    [Fact]
    public void Render_DiagnosticLine_UsesWarningNotError()
    {
        using var screen = new Screen(cols: 60, rows: 2);
        var item = new StartupLineItem("[diag] 字形: Unicode=True", StartupLineKind.Diagnostic);

        item.Render(screen, new Rect(0, 0, 60, 2), Theme.Dark);

        var warnId = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Warning });
        var errorId = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Equal(warnId, screen[0, 0].StyleId);
        Assert.NotEqual(errorId, screen[0, 0].StyleId);
    }
}
