namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell 鼠标事件接线测试（TASK066 板块 C 连线 C5+C6 — 滚轮滚动 + 选区交互 + 选中即复制）：
/// HandleKeyPress 顶部鼠标拦截吞掉不进按键链（对齐 FocusIn/Out 焦点拦截先例）；
/// 滚轮 WheelUp/WheelDown → 滚动区行滚动（C5 P3 加速度 ramp — 40ms 窗内连发乘数 +0.3 递增
/// cap max(6, base×2)、首事件/超窗回 base 1 — 基准 computeWheelStep :255-265；base =
/// readScrollSpeedBase 默认 1，ScrollKeybindingHandler.tsx:305-310；wheelup/wheeldown 经键位
/// 系统绑 scroll:lineUp/lineDown，defaultBindings.ts:200-201；离散测试事件序列乘数 floor 后
/// 前 4 事件仍 1 行 — 既有断言不破坏）；模态挂起期间鼠标全吞（权限挂起滚轮无效果）；
/// 左键拖拽选中 + Release 后选中即复制（基准 useCopyOnSelect.ts:44-80 — 选区 settled
///（isDragging=false &amp;&amp; hasSelection）才复制，拖拽中/无选区不复制，空白选区跳过 ：74-77）；
/// 双击词选中复制（多击判定 500ms/1cell — App.tsx:92-93，恒定时钟注入确定性）；
/// ESC 清选区（基准 ScrollKeybindingHandler.tsx:595-598）；无选区 Release 不复制；
/// copySelection 委托未注入降级（选区交互仍可用仅不复制）；transcript 激活时滚轮走
/// transcript 内滚动、其余鼠标吞掉。
/// Harness 形态对齐 TerminalStatusWiringTests（FakeReplEngine + FakeTerminalSizeSource +
/// StringWriter 捕获渲染输出 + 复制捕获通道）。
/// </summary>
public sealed class MouseWiringTests
{
    private sealed class Harness
    {
        public readonly FakeReplEngine Engine = new();
        public readonly StringWriter Output = new();
        public readonly List<string> Copies = [];
        public readonly SelectionState Selection = new(static () => 0);
        public ReplShell Shell = null!;

        public static Harness Create(bool inline = true, IReadOnlyList<IScrollItem>? seeds = null)
        {
            var harness = new Harness();
            harness.Shell = new ReplShell(
                harness.Engine,
                new BannerItem("1.0.0", "test-model", "按量计费", "/tmp"),
                harness.Output,
                inline: inline,
                sizeSource: new FakeTerminalSizeSource(80, 24),
                seedMessages: seeds,
                selection: harness.Selection,
                copySelection: harness.Copies.Add);
            harness.Shell.SeedContent();
            return harness;
        }
    }

    /// <summary>鼠标键注入 helper（KeyKind.Mouse + MouseEvent 载荷 — C1 通道直达）。</summary>
    private static KeyPress MouseKey(MouseKind kind, MouseButton button, int col, int row) =>
        new(KeyKind.Mouse, string.Empty, Mouse: new MouseEvent(kind, button, col, row, Shift: false, Meta: false, Ctrl: false));

    /// <summary>高内容种子（viewport 滚动测试 — 内容超出 24 行视口保证 maxScroll &gt; 0）。</summary>
    private static IReadOnlyList<IScrollItem> TallSeeds(int count) =>
        Enumerable.Range(1, count).Select(static i => (IScrollItem)new AssistantMessageItem($"第 {i} 条消息 — 鼠标滚动测试填充内容")).ToList();

    /// <summary>渲染帧上词定位（FrontBuffer 逐 cell 拼行文本动态查找 — 免维护渲染布局前缀/wrap 细节；
    /// 补充平面 scalar 投影空格与 FrontLineAt 投影约定一致）。</summary>
    private static (int Row, int Col) LocateWord(Screen screen, string word)
    {
        for (var row = 0; row < screen.Rows; row++)
        {
            var line = new StringBuilder();
            for (var col = 0; col < screen.Cols; col++)
            {
                var scalar = screen[row, col].Value.Value;
                line.Append(scalar > 0xFFFF ? ' ' : (char)scalar);
            }

            var index = line.ToString().IndexOf(word, StringComparison.Ordinal);
            if (index >= 0)
            {
                return (row, index);
            }
        }

        throw new InvalidOperationException($"渲染帧上未找到词 '{word}'（布局变化需核对 AssistantMessageItem 渲染）");
    }

    /// <summary>滚轮上 — 视口模式滚动区上滚 1 行（sticky 钉底态 ScrollTop 减 1；
    /// 首事件步长 base 1 — C5 P3 ramp 首事件重置回 base，基准 readScrollSpeedBase 默认 1
    /// 行/事件，ScrollKeybindingHandler.tsx:305-310 + computeWheelStep :255-265）。</summary>
    [Fact]
    public void WheelUp_ViewportMode_ScrollsUpOneRow()
    {
        var harness = Harness.Create(inline: false, seeds: TallSeeds(30));
        harness.Shell.RenderAndFlush();
        var before = harness.Shell.View.Messages.ScrollTop;
        Assert.True(before > 0, "前置：内容超出视口（sticky 钉底 ScrollTop > 0）");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.WheelUp, MouseButton.Left, 0, 0));

        Assert.Equal(before - 1, harness.Shell.View.Messages.ScrollTop);
    }

    /// <summary>滚轮下 — 上滚两行后下滚两行回补（P3 弹跳新语义：上滚×2 净 -2；下滚第一发
    /// 方向翻转 defer 返回 0 行（基准 :208-217 翻转判定窗），第二发提交真翻转回 base 1 行 —
    /// 净 -2 +1 = -1 对称回补；ramp 乘数 floor 后均 1 免疫）。</summary>
    [Fact]
    public void WheelDown_AfterWheelUp_ScrollsBackDown()
    {
        var harness = Harness.Create(inline: false, seeds: TallSeeds(30));
        harness.Shell.RenderAndFlush();
        var before = harness.Shell.View.Messages.ScrollTop;

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.WheelUp, MouseButton.Left, 0, 0));
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.WheelUp, MouseButton.Left, 0, 0));
        // 第一发下滚：翻转 defer（0 行 — 弹跳防误判）
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.WheelDown, MouseButton.Left, 0, 0));
        Assert.Equal(before - 2, harness.Shell.View.Messages.ScrollTop);
        // 第二发下滚：真翻转提交 base 1 行
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.WheelDown, MouseButton.Left, 0, 0));
        Assert.Equal(before - 1, harness.Shell.View.Messages.ScrollTop);
    }

    /// <summary>权限挂起 — 鼠标全吞（模态期间不可交互）：滚轮无滚动效果（模态互斥）。</summary>
    [Fact]
    public void PermissionPending_WheelEventsSwallowed()
    {
        var harness = Harness.Create(inline: false, seeds: TallSeeds(30));
        harness.Shell.RenderAndFlush();
        var before = harness.Shell.View.Messages.ScrollTop;

        harness.Shell.Permission.Begin(new PermissionRequest("Read", "a.cs 预览"));
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.WheelUp, MouseButton.Left, 0, 0));
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.WheelDown, MouseButton.Left, 0, 0));

        Assert.Equal(before, harness.Shell.View.Messages.ScrollTop);
    }

    /// <summary>左键拖拽选中 + Release 选中即复制 — 拖拽中不复制（基准 useCopyOnSelect.ts:49-51
    /// isDragging 等待）、Release 固化后复制（:58-79 settled 才触发）；复制文本 = 选区提取
    ///（5 cell 字符粒度选区 "hello"）。</summary>
    [Fact]
    public void DragSelect_CopiesSelectedTextOnRelease()
    {
        var harness = Harness.Create(seeds: [new AssistantMessageItem("hello world 鼠标选区测试")]);
        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "hello");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row));
        // 拖拽中 — 未 Release 不复制（基准 isDragging 等待语义）
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Drag, MouseButton.Left, col + 4, row));
        Assert.Empty(harness.Copies);

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Release, MouseButton.Left, col + 4, row));

        Assert.True(harness.Selection.HasSelection);
        Assert.Equal("hello", Assert.Single(harness.Copies));
    }

    /// <summary>双击词选中 + Release 复制 — 多击判定（恒定时钟注入 — 时间差 0 在 500ms 窗口内，
    /// 基准 App.tsx:92-93）+ 词粒度扩展（同类字符 run — 基准 selection.ts:240-254）。</summary>
    [Fact]
    public void DoubleClick_SelectsWordAndCopiesOnRelease()
    {
        var harness = Harness.Create(seeds: [new AssistantMessageItem("alpha beta gamma")]);
        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "beta");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col + 1, row));
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col + 1, row));
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Release, MouseButton.Left, col + 1, row));

        Assert.Equal("beta", Assert.Single(harness.Copies));
    }

    /// <summary>ESC 清选区 — 拖拽固化选区后 HandleEscape 清空（基准
    /// ScrollKeybindingHandler.tsx:595-598 escape → clearSelection）。</summary>
    [Fact]
    public void Escape_ClearsSelection()
    {
        var harness = Harness.Create(seeds: [new AssistantMessageItem("escape 清选区测试")]);
        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "escape");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row));
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Drag, MouseButton.Left, col + 5, row));
        Assert.True(harness.Selection.HasSelection);

        harness.Shell.HandleEscape();

        Assert.False(harness.Selection.HasSelection);
    }

    /// <summary>无选区 Release 不复制 — 单击（无拖动）focus 为 null → hasSelection=false
    ///（基准 useCopyOnSelect.ts:53-56 !has 重置不触发）。</summary>
    [Fact]
    public void ReleaseWithoutSelection_DoesNotCopy()
    {
        var harness = Harness.Create(seeds: [new AssistantMessageItem("单击无选区测试")]);
        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "单击");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row));
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Release, MouseButton.Left, col, row));

        Assert.False(harness.Selection.HasSelection);
        Assert.Empty(harness.Copies);
    }

    /// <summary>copySelection 未注入降级 — 选区交互仍可用仅不复制（null = 复制能力降级无操作，
    /// 对齐 clipboard 注入先例）。</summary>
    [Fact]
    public void CopySelectionNotInjected_StillSelectsWithoutCopy()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(
            engine,
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            seedMessages: [new AssistantMessageItem("null 降级测试")],
            selection: new SelectionState(static () => 0));
        shell.SeedContent();
        shell.RenderAndFlush();
        var (row, col) = LocateWord(shell.FrontBuffer!, "降级");

        shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row));
        shell.HandleRawKeyPress(MouseKey(MouseKind.Drag, MouseButton.Left, col + 1, row));
        shell.HandleRawKeyPress(MouseKey(MouseKind.Release, MouseButton.Left, col + 1, row));

        Assert.True(shell.Selection.HasSelection);
    }

    /// <summary>transcript 激活 — 滚轮走 transcript 内滚动（基准滚轮经键位系统路由 ScrollBox
    /// 行滚动，App.tsx:543-545 注释）、其余鼠标吞掉（全屏模态选区交互不在 C6 范围）。</summary>
    [Fact]
    public void TranscriptActive_WheelScrollsTranscriptAndOtherMouseSwallowed()
    {
        var harness = Harness.Create(seeds: TallSeeds(30));
        harness.Shell.HandleKey(new ConsoleKeyInfo('\x0f', ConsoleKey.O, false, false, false));
        Assert.True(harness.Shell.IsTranscriptMode);
        var offsetBefore = harness.Shell.Transcript.ScrollOffset;
        Assert.True(offsetBefore > 0, "前置：transcript 快照超出视口（进入时滚到底）");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.WheelUp, MouseButton.Left, 0, 0));
        Assert.Equal(offsetBefore - 1, harness.Shell.Transcript.ScrollOffset);

        // 其余鼠标（Press）吞掉 — 选区状态机不被激活（模态期间不可交互）
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, 0, 0));
        Assert.False(harness.Selection.IsSelecting);
        Assert.Empty(harness.Copies);
    }
}
