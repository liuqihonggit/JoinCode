// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace NativeTui.Tests.Repl;

/// <summary>
/// G2 组合验证（TASK066 板块 G — C7 备用屏×鼠标组合，跨板块收口）：
/// transcript 备用屏激活（AlternateScreenController 非 null + transcript 激活 = 真备用屏模式）
/// 下滚轮走 transcript 内滚动、Press/Drag 吞掉不泄漏、退出恢复主屏后鼠标跟踪模式关闭；
/// 选区在备用屏进入时清空（防主屏残留坐标错位选区）。
/// </summary>
public sealed class AlternateScreenMouseComboTests
{
    private sealed class Harness
    {
        public readonly FakeReplEngine Engine = new();
        public readonly StringWriter ShellOutput = new();
        public readonly StringWriter AltOutput = new();
        public readonly List<string> Copies = [];
        public ReplShell Shell = null!;

        public static Harness Create()
        {
            var harness = new Harness();
            var controller = new AlternateScreenController(harness.AltOutput.Write);
            var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
            harness.Shell = new ReplShell(
                harness.Engine, banner, harness.ShellOutput,
                seedMessages: TallSeeds(30),
                sizeSource: new FakeTerminalSizeSource(80, 24),
                alternateScreen: controller,
                copySelection: harness.Copies.Add);
            harness.Shell.SeedContent();
            return harness;
        }

        public string AltSequences => AltOutput.ToString();
    }

    /// <summary>高内容种子（transcript 滚动测试 — 内容超出 24 行视口）。</summary>
    private static IReadOnlyList<IScrollItem> TallSeeds(int count) =>
        Enumerable.Range(1, count).Select(static i => (IScrollItem)new AssistantMessageItem($"第 {i} 条 — G2 组合验证填充")).ToList();

    private static KeyPress Wheel(bool down) =>
        new(KeyKind.Mouse, string.Empty, Mouse: new MouseEvent(
            down ? MouseKind.WheelDown : MouseKind.WheelUp, MouseButton.Left, 10, 5, false, false, false));

    private static KeyPress Click(int col, int row) =>
        new(KeyKind.Mouse, string.Empty, Mouse: new MouseEvent(MouseKind.Press, MouseButton.Left, col, row, false, false, false));

    private static KeyPress Drag(int col, int row) =>
        new(KeyKind.Mouse, string.Empty, Mouse: new MouseEvent(MouseKind.Drag, MouseButton.Left, col, row, false, false, false));

    private static void EnterTranscript(ReplShell shell)
    {
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "o", Ctrl: true));
        shell.RenderAndFlush();
    }

    private static void ExitTranscript(ReplShell shell)
    {
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "o", Ctrl: true));
        shell.RenderAndFlush();
    }

    [Fact]
    public void AltscreenActive_WheelScrollsTranscript()
    {
        // 备用屏激活（1049h + 鼠标跟踪组合已发）时滚轮 → transcript 滚动（内容滚动不变式）
        var harness = Harness.Create();
        var shell = harness.Shell;
        shell.RenderAndFlush();
        EnterTranscript(shell);

        var scrollBefore = shell.View.Transcript!.ScrollOffset;
        shell.HandleRawKeyPress(Wheel(down: true));
        shell.RenderAndFlush();
        var scrollAfter = shell.View.Transcript!.ScrollOffset;

        Assert.True(scrollAfter > scrollBefore, $"滚轮应推进 transcript 滚动: {scrollBefore} -> {scrollAfter}");
        // 备用屏进入序列已发射（1049h + 四鼠标模式）
        Assert.Contains("\x1b[?1049h", harness.AltSequences);
        Assert.Contains("\x1b[?1006h", harness.AltSequences);
    }

    [Fact]
    public void AltscreenActive_ClickAndDragSwallowedNoSelectionLeak()
    {
        // 备用屏内点击/拖拽吞掉 — 不产生选区（transcript 态鼠标仅滚轮可用）+ 无复制触发
        var harness = Harness.Create();
        var shell = harness.Shell;
        shell.RenderAndFlush();
        EnterTranscript(shell);

        shell.HandleRawKeyPress(Click(3, 2));
        shell.HandleRawKeyPress(Drag(8, 4));
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Mouse, string.Empty, Mouse: new MouseEvent(MouseKind.Release, MouseButton.Left, 8, 4, false, false, false)));
        shell.RenderAndFlush();

        // 无选区固化（transcript 态 Press/Drag 吞掉 — 不进入选区状态机锚定）
        Assert.Empty(harness.Copies);
    }

    [Fact]
    public void ExitAltscreen_MouseTrackingDisabled()
    {
        // 退出 transcript（备用屏退出）— 严格反序：鼠标跟踪先关再退屏（1049l 最后）
        var harness = Harness.Create();
        var shell = harness.Shell;
        shell.RenderAndFlush();
        EnterTranscript(shell);
        ExitTranscript(shell);

        var sequences = harness.AltSequences;
        // 退出序列完整（反序：1006l...1000l → 1049l）
        Assert.Contains("\x1b[?1000l", sequences);
        Assert.Contains("\x1b[?1049l", sequences);
        // 顺序断言：1049l 在全部鼠标 Disable 之后
        var mouseOffIndex = sequences.IndexOf("\x1b[?1000l");
        var altOffIndex = sequences.IndexOf("\x1b[?1049l");
        Assert.True(mouseOffIndex >= 0 && altOffIndex > mouseOffIndex, "鼠标跟踪关闭必须先于备用屏退出（进入严格反序）");
    }

    [Fact]
    public void NullController_TranscriptStillWorks_ZeroAltscreenOutput()
    {
        // 无备用屏控制器（inline 模式）— transcript 照常 + 零备用屏序列（既有契约兜底）
        var engine = new FakeReplEngine();
        var banner = new BannerItem("0.0.0", "m", "b", "C:\\");
        var shell = new ReplShell(
            engine, banner, new StringWriter(),
            seedMessages: TallSeeds(30),
            sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        shell.RenderAndFlush();

        EnterTranscript(shell);
        Assert.True(shell.View.Transcript is not null);
        shell.HandleRawKeyPress(Wheel(down: true));
        shell.RenderAndFlush();
        Assert.True(shell.View.Transcript!.ScrollOffset > 0, "inline 模式 transcript 滚轮照常");

        ExitTranscript(shell);
        Assert.True(shell.View.Transcript is null);
    }
}
