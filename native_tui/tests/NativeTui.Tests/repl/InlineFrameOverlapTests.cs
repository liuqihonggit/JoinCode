using System.Text;
using NativeTui.Tests.Terminal;

namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK109 P20 — inline 主屏帧重叠回归：真画出画布断言「输入框顶边框行」不被上方内容侵占。
/// 用户真机现象：「有时候，尤其是输入栏和上面一些文字重叠了」——历史/帮助面板/软折行把
/// 输入区挤上去时，输入框与上方文本落在同一物理行。
/// 判定不靠截图：把 ReplShell 的渲染流喂进 <see cref="VirtualTerminal"/>（CUU 钳制/滚屏语义
/// 与真终端一致），再看视口里提示符上一行是否仍是纯框线。
/// </summary>
public sealed class InlineFrameOverlapTests
{
    /// <summary>把 ReplShell 的输出直接喂进虚拟终端（逐次 Write 即一次 Feed）。</summary>
    private sealed class TerminalWriter(VirtualTerminal terminal) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => terminal.Feed(value.ToString());

        public override void Write(string? value) => terminal.Feed(value ?? string.Empty);
    }

    private static (ReplShell Shell, VirtualTerminal Terminal, FakeTerminalSizeSource Size) Create(int cols = 80, int rows = 24, int seeds = 30)
    {
        var terminal = new VirtualTerminal(cols, rows);
        var size = new FakeTerminalSizeSource(cols, rows);
        var shell = new ReplShell(
            new FakeReplEngine(),
            new BannerItem("1.0.0", "test-model", "按量计费", "/tmp"),
            new TerminalWriter(terminal),
            inline: true,
            sizeSource: size,
            seedMessages: Enumerable.Range(1, seeds)
                .Select(static i => (IScrollItem)new AssistantMessageItem($"第 {i} 条消息 — 帧重叠回归填充内容"))
                .ToList());
        shell.SeedContent();
        return (shell, terminal, size);
    }

    /// <summary>输入框完整性判定：最后一条提示符行的上一行必须是纯框线（─）。</summary>
    private static void AssertInputBoxIntact(VirtualTerminal terminal, string scenario)
    {
        var lines = terminal.Viewport();
        var dump = string.Join("\n", lines);
        var promptRow = -1;
        for (var row = lines.Length - 1; row >= 0; row--)
        {
            if (lines[row].Contains(Figures.Pointer, StringComparison.Ordinal))
            {
                promptRow = row;
                break;
            }
        }

        Assert.True(promptRow > 0, $"[{scenario}] 视口里找不到输入框提示符 '{Figures.Pointer}'：\n{dump}");

        var border = lines[promptRow - 1].TrimEnd();
        Assert.True(border.Length > 0 && border.All(static c => c == '─'),
            $"[{scenario}] 输入框顶边框行被上方内容侵占（重叠）：'{lines[promptRow - 1]}'\n{dump}");
    }

    /// <summary>历史超出视口 + 打开帮助面板（-11 行挤压消息区）→ 输入框仍完整。</summary>
    [Fact]
    public void HelpPanelOpen_WithTallHistory_KeepsInputBoxIntact()
    {
        var (shell, terminal, _) = Create();

        shell.RenderAndFlush();
        shell.RenderAndFlush();
        shell.HandleKey(new ConsoleKeyInfo('?', ConsoleKey.Oem2, false, false, false));
        shell.RenderAndFlush();
        shell.RenderAndFlush();

        AssertInputBoxIntact(terminal, "help + 高历史");
    }

    /// <summary>软折行长输入（CJK 双宽）→ 输入框实际行数与布局高度一致（不得压到上方内容）。</summary>
    [Fact]
    public void MultilineCjkInput_KeepsInputBoxIntact()
    {
        var (shell, terminal, _) = Create();
        shell.RenderAndFlush();

        foreach (var ch in "这是一段很长的中文输入用来触发输入框软折行并检验高度计算是否与渲染一致")
        {
            shell.HandleKey(new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false));
        }

        shell.RenderAndFlush();
        shell.RenderAndFlush();

        AssertInputBoxIntact(terminal, "CJK 软折行");
    }

    /// <summary>终端变窄（80→40）后软折行行数增长 → 输入框仍完整，且不与消息行叠写。</summary>
    [Fact]
    public void NarrowResize_WithTallHistory_KeepsInputBoxIntact()
    {
        var (shell, terminal, size) = Create();
        shell.RenderAndFlush();

        size.Cols = 40;
        shell.EnsureScreens();
        shell.RenderAndFlush();
        shell.RenderAndFlush();

        AssertInputBoxIntact(terminal, "窄终端");
    }
}
