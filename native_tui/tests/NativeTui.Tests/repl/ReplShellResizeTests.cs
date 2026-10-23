namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell resize 事件化测试 — ITerminalSizeSource 注入把终端尺寸从静态环境依赖（Console.Window*）
/// 解耦为可测事件源：inline 模式屏宽 = 尺寸源 cols（resize 敏感维度）、屏高 = 内容高（终端 rows 无关 — 
/// 唯 rows 不触发重建）；视口模式 cols/rows 均取自尺寸源；尺寸变化重建双缓冲并保留旧帧跨尺寸 diff
/// （_prevOverride = 旧 front，供 InlineComposer 增长 append / 宽度变 fullReset）。
/// </summary>
public sealed class ReplShellResizeTests
{
    /// <summary>构造测试 shell（inline 默认，StringWriter 收渲染输出，尺寸源可注入可控）。</summary>
    private static (ReplShell Shell, FakeTerminalSizeSource Size) CreateShell(bool inline = true)
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var size = new FakeTerminalSizeSource(80, 24);
        var shell = new ReplShell(engine, banner, new StringWriter(), inline: inline, sizeSource: size);
        return (shell, size);
    }

    /// <summary>尺寸注入 — inline 屏宽取尺寸源 cols、屏高取内容高（不读 Console.Window*）。</summary>
    [Fact]
    public void EnsureScreens_Inline_ColsFromSize_RowsFromContent()
    {
        var (shell, _) = CreateShell();
        shell.SeedContent();
        shell.EnsureScreens();

        Assert.Equal(80, shell.BackBuffer!.Cols);
        Assert.Equal(Math.Max(8, shell.View.MeasureContentHeight(80)), shell.BackBuffer!.Rows);
    }

    /// <summary>cols 变化 — 双缓冲按新宽重建（引用变化）+ 保留旧帧跨尺寸 diff（prevOverride = 旧屏）。</summary>
    [Fact]
    public void EnsureScreens_ColsChanged_RebuildsAndKeepsPrevFrame()
    {
        var (shell, size) = CreateShell();
        shell.SeedContent();
        shell.RenderAndFlush();
        var before = shell.BackBuffer!;

        size.Cols = 100;
        shell.EnsureScreens();

        Assert.Equal(100, shell.BackBuffer!.Cols);
        Assert.NotSame(before, shell.BackBuffer);
        Assert.NotNull(shell.PrevOverride);
    }

    /// <summary>尺寸不变 — 屏对象复用（不重建、无 prevOverride）。</summary>
    [Fact]
    public void EnsureScreens_SizeUnchanged_ReusesBuffers()
    {
        var (shell, _) = CreateShell();
        shell.SeedContent();
        shell.RenderAndFlush();
        var before = shell.BackBuffer!;

        shell.EnsureScreens();

        Assert.Same(before, shell.BackBuffer);
        Assert.Null(shell.PrevOverride);
    }

    /// <summary>inline 语义 — 终端 rows 变化不触发重建（屏高 = 内容高与终端行数无关，resize 敏感维度唯 cols）。</summary>
    [Fact]
    public void EnsureScreens_Inline_TerminalRowsChange_DoesNotRebuild()
    {
        var (shell, size) = CreateShell();
        shell.SeedContent();
        shell.RenderAndFlush();
        var before = shell.BackBuffer!;

        size.Rows = 40;
        shell.EnsureScreens();

        Assert.Same(before, shell.BackBuffer);
    }

    /// <summary>视口模式空帧零输出（6.13 — 基准 ink/terminal.ts diff.length===0 直接 return）：
    /// 帧间零变化且光标位未变 → 零字节输出（旧行为每帧恒发光标定位序列 ~8 字节背景流量）。</summary>
    [Fact]
    public void ViewportMode_NoChangeFrame_ZeroOutput()
    {
        var engine = new FakeReplEngine();
        var output = new StringWriter();
        var size = new FakeTerminalSizeSource(80, 24);
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), output, inline: false, sizeSource: size);
        shell.SeedContent();
        shell.RenderAndFlush();
        output.GetStringBuilder().Clear();

        shell.RenderAndFlush();

        Assert.Equal(0, output.ToString().Length);
    }

    /// <summary>inline 终端高度变化 — 物理屏已被终端 reflow 重排（相对光标模型失同步），
    /// 屏对象可复用但必须强制 fullReset 重绘（对齐基准 log-update.ts:142-147 高度变化无条件 fullReset；
    /// 缺判定则零输出不自愈 — 物理光标与内部帧状态漂移到下一帧有变更才偶然恢复）。</summary>
    [Fact]
    public void Inline_TerminalRowsChange_NextFrameForcesFullReset()
    {
        var engine = new FakeReplEngine();
        var output = new StringWriter();
        var size = new FakeTerminalSizeSource(80, 24);
        var shell = new ReplShell(engine, new BannerItem("1.0.0", "m", "b", "/tmp"), output, sizeSource: size);
        shell.SeedContent();
        shell.RenderAndFlush();
        output.GetStringBuilder().Clear();

        size.Rows = 12;
        shell.RenderAndFlush();

        Assert.Contains("\x1b[2J", output.ToString());
    }

    /// <summary>视口模式 — cols/rows 均取自尺寸源，任一变化触发重建。</summary>
    [Fact]
    public void EnsureScreens_Viewport_RowsFromSize_RebuildsOnChange()
    {
        var (shell, size) = CreateShell(inline: false);
        shell.SeedContent();
        shell.RenderAndFlush();
        var before = shell.BackBuffer!;

        Assert.Equal(24, before.Rows);

        size.Rows = 30;
        shell.EnsureScreens();

        Assert.Equal(30, shell.BackBuffer!.Rows);
        Assert.NotSame(before, shell.BackBuffer);
    }
}

/// <summary>测试用终端尺寸源（可变 Current — resize 场景确定性注入）。</summary>
internal sealed class FakeTerminalSizeSource(int cols, int rows) : ITerminalSizeSource
{
    public int Cols { get; set; } = cols;

    public int Rows { get; set; } = rows;

    public (int Cols, int Rows) Current => (Math.Max(20, Cols), Math.Max(8, Rows));
}
