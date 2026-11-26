using NativeTui.Tests.Terminal;

namespace NativeTui.Tests.Repl;

/// <summary>
/// 启动行退休（"引擎启动中…" 撤除）门禁 —— 帧高收缩 1 行是残影高发点：
/// ① 内容整体上移一行，最后一行必须被擦掉（否则底部留旧 footer 残字）；
/// ② 撤除后视口内不得残留旧行文本（"引擎启动中…" 必须真的消失，而不是被覆盖一半）；
/// ③ 若整帧已高于终端（顶部行早滚入 scrollback、物理不可变），**不许撤除** ——
///    强撤会走 fullReset（2J+3J）清掉用户 scrollback，代价远大于多留一行提示。
/// 断言用 <see cref="Terminal.VirtualTerminal"/> 把渲染输出真的画出来，避免"看字符串猜画面"。
/// </summary>
public sealed class StartupLineRetireTests
{
    private const int Cols = 80;

    private static (ReplShell Shell, FakeTerminalSizeSource Size, StringWriter Output) CreateShell(int rows = 24, params string[] extraStartupLines)
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var size = new FakeTerminalSizeSource(Cols, rows);
        var output = new StringWriter();
        var shell = new ReplShell(engine, banner, output, inline: true, sizeSource: size);
        shell.AddStartupLine(ReplShell.EngineStartingLine);
        foreach (var line in extraStartupLines)
        {
            shell.AddStartupLine(line);
        }

        shell.SeedContent();
        return (shell, size, output);
    }

    /// <summary>退休成功 — 内容高度恰好减 1 行（帧高收缩的唯一来源）。</summary>
    [Fact]
    public void Retire_RemovesStartupLine_ShrinksContentHeightByOne()
    {
        var (shell, _, _) = CreateShell();
        var before = shell.View.MeasureContentHeight(Cols);

        Assert.True(shell.TryRetireStartupLine(ReplShell.EngineStartingLine));

        Assert.Equal(before - 1, shell.View.MeasureContentHeight(Cols));
        Assert.DoesNotContain(shell.View.Messages.Items, item => item is StartupLineItem);
    }

    /// <summary>幂等 — 已撤除再撤返回 false，不抛、不误删其他启动行。</summary>
    [Fact]
    public void Retire_Twice_SecondCallIsNoOp()
    {
        var (shell, _, _) = CreateShell(extraStartupLines: "[diag] 字形: Unicode=True 提示符=❯");

        Assert.True(shell.TryRetireStartupLine(ReplShell.EngineStartingLine));
        Assert.False(shell.TryRetireStartupLine(ReplShell.EngineStartingLine));
        Assert.Contains(shell.View.Messages.Items, item => item is StartupLineItem { Text: "[diag] 字形: Unicode=True 提示符=❯" });
    }

    /// <summary>
    /// 残影门禁（核心）— 两帧真的画到虚拟终端：撤除启动行后，
    /// 视口里"引擎启动中…"必须完全消失（连同其宽字符占位），底部不得留上一帧的残字。
    /// </summary>
    [Fact]
    public void Retire_SecondFrame_DrawsWithoutGhostText()
    {
        var (shell, _, output) = CreateShell();
        var terminal = new VirtualTerminal(Cols, 24);

        shell.RenderAndFlush();
        terminal.Feed(output.ToString());
        output.GetStringBuilder().Clear();

        Assert.True(terminal.FindInViewport(ReplShell.EngineStartingLine) >= 0);

        Assert.True(shell.TryRetireStartupLine(ReplShell.EngineStartingLine));
        shell.RenderAndFlush();
        var secondFrame = output.ToString();
        terminal.Feed(secondFrame);

        // ① 旧行文本彻底消失（视口 + scrollback 都不得残留）
        Assert.True(terminal.FindAnywhere(ReplShell.EngineStartingLine) < 0);
        // ② 收缩走差量路径（不是 fullReset —— 清 scrollback 会毁掉用户历史）
        Assert.DoesNotContain("\x1b[2J", secondFrame);
        Assert.DoesNotContain("\x1b[3J", secondFrame);
        // ③ 收缩必然擦底部被腾空的行（否则底部留上一帧残字）
        Assert.Contains("\x1b[2K", secondFrame);
        // ④ 帧内容与目标缓冲逐行一致（渲染没跑到别处 —— 失同步会让行错位）
        var target = ScreenLines(shell.FrontBuffer!);
        var viewport = terminal.Viewport();
        for (var row = 0; row < target.Length; row++)
        {
            Assert.Equal(target[row], viewport[row]);
        }
    }

    /// <summary>
    /// 已滚入 scrollback 时不许撤除 —— 顶部行物理不可变，强撤会 fullReset 清 scrollback。
    /// 帧高 &gt; 终端行数即"顶部行已在 scrollback"的判据。
    /// </summary>
    [Fact]
    public void Retire_FrameTallerThanTerminal_KeepsStartupLine()
    {
        var (shell, _, _) = CreateShell(rows: 5);
        var contentRows = shell.View.MeasureContentHeight(Cols);
        Assert.True(contentRows > 5, "前置条件：整帧必须高于终端（顶部行滚入 scrollback）");

        Assert.False(shell.TryRetireStartupLine(ReplShell.EngineStartingLine));
        Assert.Contains(shell.View.Messages.Items, item => item is StartupLineItem { Text: ReplShell.EngineStartingLine });
    }

    /// <summary>
    /// 临界（不留余量）：帧高恰好填满终端 → **拒绝撤除**。此时每行收尾的 CRLF 已把顶行顶进
    /// scrollback，撤除会试图改写不可达行（CUU 被钳制在视口顶）→ 行号错位残影；
    /// 且真机上用户本来也看不到那行，多留无害。
    /// </summary>
    [Fact]
    public void Retire_FrameFillsTerminalExactly_KeepsStartupLine()
    {
        var (shell, size, output) = CreateShell(rows: 24);
        var frameRows = shell.View.MeasureContentHeight(Cols);
        size.Rows = frameRows;
        var terminal = new VirtualTerminal(Cols, frameRows);

        shell.RenderAndFlush();
        terminal.Feed(output.ToString());
        output.GetStringBuilder().Clear();

        Assert.False(shell.TryRetireStartupLine(ReplShell.EngineStartingLine));
        Assert.Contains(shell.View.Messages.Items, item => item is StartupLineItem { Text: ReplShell.EngineStartingLine });

        shell.RenderAndFlush();
        var secondFrame = output.ToString();
        terminal.Feed(secondFrame);
        Assert.DoesNotContain("\x1b[2J", secondFrame);
        Assert.DoesNotContain("\x1b[3J", secondFrame);
    }

    /// <summary>
    /// 最紧的**可撤**边界：内容高 + 1 == 终端行数（只剩光标占的那一行空行）——
    /// 撤除仍不得残影，且帧内容与目标缓冲逐行一致（行号没错位）。
    /// </summary>
    [Fact]
    public void Retire_OneSlackLineOnly_NoGhost()
    {
        var (shell, size, output) = CreateShell(rows: 24);
        var frameRows = shell.View.MeasureContentHeight(Cols);
        size.Rows = frameRows + 1;
        var terminal = new VirtualTerminal(Cols, frameRows + 1);

        shell.RenderAndFlush();
        terminal.Feed(output.ToString());
        output.GetStringBuilder().Clear();
        Assert.True(terminal.FindInViewport(ReplShell.EngineStartingLine) >= 0);

        Assert.True(shell.TryRetireStartupLine(ReplShell.EngineStartingLine));
        shell.RenderAndFlush();
        var secondFrame = output.ToString();
        terminal.Feed(secondFrame);

        Assert.True(terminal.FindAnywhere(ReplShell.EngineStartingLine) < 0);
        Assert.DoesNotContain("\x1b[2J", secondFrame);
        Assert.DoesNotContain("\x1b[3J", secondFrame);
        var target = ScreenLines(shell.FrontBuffer!);
        var viewport = terminal.Viewport();
        for (var row = 0; row < target.Length; row++)
        {
            Assert.Equal(target[row], viewport[row]);
        }
    }

    /// <summary>帧内容 → 文本行（断言基准；行尾去空白，宽字符只取本体、跳过占位列）。</summary>
    private static string[] ScreenLines(Screen screen)
    {
        var lines = new string[screen.Rows];
        for (var row = 0; row < screen.Rows; row++)
        {
            var builder = new StringBuilder();
            for (var col = 0; col < screen.Cols; col++)
            {
                var cell = screen[row, col];
                if (cell.Width == CellWidth.SpacerTail)
                {
                    continue;
                }

                builder.Append(cell.Value.ToString());
            }

            lines[row] = builder.ToString().TrimEnd(' ');
        }

        return lines;
    }
}
