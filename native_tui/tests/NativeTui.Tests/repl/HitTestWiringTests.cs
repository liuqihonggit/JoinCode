namespace NativeTui.Tests.Repl;

/// <summary>
/// HitTest 接线测试（TASK066 板块 C 基建 C2 消费 — ReplView 挂载 + BackgroundTaskPanel 标注 +
/// ReplShell 单击分派）：
/// - ReplView 直渲染：面板级+行级标注 rect 断言（面板级先注册行级后注册 — 行级覆盖可见行、
///   面板级兜底溢出行，对齐基准 hit-test.ts:33-39 后绘制兄弟顶层优先）、帧首 Clear（二帧渲染
///   不累积 — 基准 nodeCache 每帧重填 :8-10）、消息项 HitTestId 注册（渲染窗口内）、
///   overscan 窗口外项不注册（基准 :12-13 未渲染项不在表内）；
/// - ReplShell 集成（Harness 对齐 MouseWiringTests — FakeReplEngine + 视口模式 80x24 +
///   选区注入）：单击面板行 → 选中态激活 + 索引对 + 单击直查挂详情（基准
///   CoordinatorAgentStatus.tsx:73-75 行 onClick 直接 enterTeammateView 单击即查看）；
///   终态行可点选查看（对齐基准 AgentLine 任意状态 onClick :74）；单击面板溢出行吞不锚选区；
///   单击消息区空白回落既有选区锚定链（零破坏）；修饰键点击回落选区链；详情/选中态模态
///   激活期间点击全吞（现有互斥不变）。
/// 基准差异注明：基准组件 click 于 Release 无拖拽单击时经 dispatchClick 触发（App.tsx:650-654），
/// w2 于 Press 即触发（单击语义等价 — 面板区域点击不残留选区 anchor）；基准 hover 高亮
///（AgentLine onMouseEnter/onMouseLeave）不在接线范围（w2 无 hover 追踪通道）。
/// </summary>
public sealed class HitTestWiringTests
{
    /// <summary>带命中 id 的测试消息项（高 2 行：margin 1 + 正文 1 — 布局形态对齐
    /// AssistantMessageItem；显式实现 HitTestId — 消息区注册路径数据源）。</summary>
    private sealed record HitItem(string Id, string Text) : IScrollItem
    {
        /// <summary>按可用宽度测量（margin 1 + 正文 1 恒 2 — 不随宽度折行）。</summary>
        public int MeasureHeight(int width) => 2;

        /// <summary>渲染正文到 margin 行下方（frame 已含滚动平移）。</summary>
        public void Render(Screen screen, Rect frame, Theme theme) =>
            screen.WriteText(frame.Y + 1, frame.X, Text, 0, frame.Right);

        /// <summary>命中 id（显式实现 — 经 IScrollItem 接口引用消费）。</summary>
        public string? HitTestId => Id;
    }

    /// <summary>测试 harness（形态对齐 MouseWiringTests — 视口模式 80x24 为鼠标跟踪真实场景）。</summary>
    private sealed class Harness
    {
        public readonly FakeReplEngine Engine = new();
        public readonly StringWriter Output = new();
        public readonly SelectionState Selection = new(static () => 0);
        public ReplShell Shell = null!;

        public static Harness Create(IReadOnlyList<IScrollItem>? seeds = null)
        {
            var harness = new Harness();
            harness.Shell = new ReplShell(
                harness.Engine,
                new BannerItem("1.0.0", "test-model", "按量计费", "/tmp"),
                harness.Output,
                inline: false,
                sizeSource: new FakeTerminalSizeSource(80, 24),
                seedMessages: seeds,
                selection: harness.Selection);
            harness.Shell.SeedContent();
            return harness;
        }
    }

    /// <summary>鼠标键注入 helper（KeyKind.Mouse + MouseEvent 载荷 — 形态对齐 MouseWiringTests）。</summary>
    private static KeyPress MouseKey(MouseKind kind, MouseButton button, int col, int row, bool shift = false) =>
        new(KeyKind.Mouse, string.Empty, Mouse: new MouseEvent(kind, button, col, row, Shift: shift, Meta: false, Ctrl: false));

    /// <summary>渲染帧上词定位（FrontBuffer 逐 cell 拼行文本动态查找 — 免维护渲染布局数字，
    /// 形态对齐 MouseWiringTests.LocateWord）。</summary>
    private static (int Row, int Col) LocateWord(Screen screen, string word)
    {
        for (var row = 0; row < screen.Rows; row++)
        {
            var line = new StringBuilder();
            for (var col = 0; col < screen.Cols; col++)
            {
                if (screen[row, col].Width == CellWidth.SpacerTail)
                {
                    continue;
                }

                var scalar = screen[row, col].Value.Value;
                line.Append(scalar > 0xFFFF ? ' ' : (char)scalar);
            }

            var index = line.ToString().IndexOf(word, StringComparison.Ordinal);
            if (index >= 0)
            {
                return (row, index);
            }
        }

        throw new InvalidOperationException($"渲染帧上未找到词 '{word}'（布局变化需核对面板渲染）");
    }

    /// <summary>面板渲染后注册表面板级+行级 rect（rows=10、无排队/建议/操作条布局：
    /// footer 1 + 输入 3 → 面板高 2 行贴输入框上方 — 行 4 = f-1、行 5 = tm-1；半开区间：
    /// 行内任意列（含 col 0 / col 79）命中行级 id — 行级后注册覆盖面板级）。</summary>
    [Fact]
    public void PanelRender_RegistersPanelAndRowRects()
    {
        using var screen = new Screen(cols: 80, rows: 10);
        var view = new ReplView();
        view.BackgroundTasks.Upsert("fork", "f-1", "任务1", "running");
        view.BackgroundTasks.Upsert("teammate", "tm-1", "审阅", "running");
        view.Render(screen, Theme.Dark);

        // 面板级 1 + 行级 2 = 3 条标注（消息项 HitTestId 全 DIM null 不注册）
        Assert.Equal(3, view.HitTests.Count);
        Assert.Equal("background-task:fork:f-1", view.HitTests.HitTest(row: 4, col: 0));
        Assert.Equal("background-task:fork:f-1", view.HitTests.HitTest(row: 4, col: 79));
        Assert.Equal("background-task:teammate:tm-1", view.HitTests.HitTest(row: 5, col: 40));
        // 面板上边界外（间隔行）与下边界外（输入框区）不命中 — 半开区间（基准 :26-29）
        Assert.Null(view.HitTests.HitTest(row: 3, col: 0));
        Assert.Null(view.HitTests.HitTest(row: 6, col: 0));
    }

    /// <summary>溢出行命中面板级 id（12 任务 → 可见 10 行 + "+2 more" 溢出行：可见行逐行行级标注、
    /// 溢出行无行级 rect — 面板级整块兜底命中；rows=20 布局：面板 11 行贴输入框上方）。</summary>
    [Fact]
    public void OverflowRow_HitsPanelId()
    {
        using var screen = new Screen(cols: 80, rows: 20);
        var view = new ReplView();
        for (var i = 1; i <= 12; i++)
        {
            view.BackgroundTasks.Upsert("fork", $"f-{i}", $"任务{i}", "running");
        }

        view.Render(screen, Theme.Dark);

        // 面板级 1 + 行级 10（可见窗口）= 11 条标注
        Assert.Equal(11, view.HitTests.Count);
        // rows=20：footer 1 + 输入 3 → inputFrame.Y=16、面板高 11 → 面板行 5..15
        //（行 5-14 任务行、行 15 溢出行）；行 5..14 命中行级 id、行 15 命中面板级
        Assert.Equal("background-task:fork:f-1", view.HitTests.HitTest(row: 5, col: 0));
        Assert.Equal("background-task:fork:f-10", view.HitTests.HitTest(row: 14, col: 40));
        Assert.Equal(BackgroundTaskPanel.PanelHitId, view.HitTests.HitTest(row: 15, col: 0));
        Assert.Equal(BackgroundTaskPanel.PanelHitId, view.HitTests.HitTest(row: 15, col: 79));
        // 面板上边界外（间隔行 4）不命中
        Assert.Null(view.HitTests.HitTest(row: 4, col: 0));
    }

    /// <summary>二帧渲染不累积 — 帧首 Clear 每帧重建（基准 nodeCache 每帧重填 :8-10）：
    /// 第二帧 Count 与首帧相等（无翻倍残留）、命中结果一致。</summary>
    [Fact]
    public void SecondRender_ClearsPreviousFrame()
    {
        using var screen = new Screen(cols: 80, rows: 10);
        var view = new ReplView();
        view.BackgroundTasks.Upsert("fork", "f-1", "任务1", "running");
        view.Render(screen, Theme.Dark);
        var firstFrameCount = view.HitTests.Count;
        Assert.True(firstFrameCount > 0, "前置：首帧已有面板标注");

        view.Render(screen, Theme.Dark);

        Assert.Equal(firstFrameCount, view.HitTests.Count);
        // 单任务面板贴输入框上方占 1 行（row 5 — 双任务才是 row 4 起）；二帧 Clear 后重建位置不变
        Assert.Equal("background-task:fork:f-1", view.HitTests.HitTest(row: 5, col: 0));
    }

    /// <summary>消息项 HitTestId 注册 — 渲染窗口内项注册（含 overscan 预渲染区）、窗口外项无
    /// rect 不注册（基准 :12-13）：90 项 × 2 行 = 180 行内容、消息区高 19、sticky 钉底
    /// scrollTop=161、overscan 窗口 [81, 260) → 项 40..89 渲染注册（50 rect）；窗口外项
    ///（msg:0..39，含屏上方 overscan 外）缺席；屏内首项 msg:81（Y=0）/ 末项 msg:89（Y=17）。</summary>
    [Fact]
    public void MessageItems_RegisterOnlyWithinOverscanWindow()
    {
        using var screen = new Screen(cols: 40, rows: 24);
        var view = new ReplView();
        for (var i = 0; i < 90; i++)
        {
            view.Messages.Add(new HitItem($"msg:{i}", $"内容{i}"));
        }

        view.Render(screen, Theme.Dark);

        // 窗口内 50 项（i ∈ [40, 90)）— 窗口外 40 项（msg:0..39）无 rect；空面板零标注
        Assert.Equal(50, view.HitTests.Count);
        // 屏内项命中：scrollTop=161（奇数）→ msg:80 平移后 Y=-1 高 2 覆盖行 0（上半越屏）；
        // msg:81 Y=1（行 1-2）、msg:89 Y=17（行 17-18，末屏内项）
        Assert.Equal("msg:80", view.HitTests.HitTest(row: 0, col: 0));
        Assert.Equal("msg:81", view.HitTests.HitTest(row: 1, col: 0));
        Assert.Equal("msg:89", view.HitTests.HitTest(row: 18, col: 0));
        // 消息区下边界外（行 19 间隔行）不命中
        Assert.Null(view.HitTests.HitTest(row: 19, col: 0));
    }

    /// <summary>单击面板行 → 选中态激活 + 索引对 + 单击直查挂详情（基准
    /// CoordinatorAgentStatus.tsx:73-75 onClick 直接 enterTeammateView — 单击即查看；
    /// 一次点击等价键位 Shift+Down 定位 + Enter 查看两步）；查看后保持选中态；
    /// 详情模态激活期间再点击另一行 → 现有互斥吞（索引不切换）。</summary>
    [Fact]
    public void ClickRunningRow_SelectsAndOpensDetail()
    {
        var harness = Harness.Create();
        harness.Shell.View.BackgroundTasks.Upsert("fork", "f-1", "任务甲", "running");
        harness.Shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅乙", "running");
        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "审阅乙");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row));

        Assert.True(harness.Shell.View.BackgroundTasks.IsSelectionActive);
        Assert.Equal(1, harness.Shell.View.BackgroundTasks.SelectedIndex);
        Assert.True(harness.Shell.IsBackgroundTaskDetailOpen);
        var dialog = harness.Shell.View.Messages.Items.OfType<BackgroundTaskDetailDialog>().Single();
        Assert.Equal("tm-1", dialog.Entry.TaskId);

        // 详情模态激活期间点击另一行（任务甲）— 现有模态互斥吞：选中索引不切换
        var (otherRow, otherCol) = LocateWord(harness.Shell.FrontBuffer!, "任务甲");
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, otherCol, otherRow));
        Assert.Equal(1, harness.Shell.View.BackgroundTasks.SelectedIndex);
    }

    /// <summary>终态行可点选查看 — 对齐基准 AgentLine 任意状态 onClick 均可查看
    ///（CoordinatorAgentStatus.tsx:74 — completed 行点击同样 enterTeammateView）：
    /// 终态行命中直设选中索引（不走 MoveSelection 跳终态 — 点击是直接定位）+ 详情挂载。</summary>
    [Fact]
    public void ClickTerminalRow_AlsoSelectsAndOpensDetail()
    {
        var harness = Harness.Create();
        harness.Shell.View.BackgroundTasks.Upsert("fork", "f-1", "完成丙", "completed");
        harness.Shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅乙", "running");
        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "完成丙");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row));

        Assert.True(harness.Shell.View.BackgroundTasks.IsSelectionActive);
        Assert.Equal(0, harness.Shell.View.BackgroundTasks.SelectedIndex);
        Assert.True(harness.Shell.IsBackgroundTaskDetailOpen);
        var dialog = harness.Shell.View.Messages.Items.OfType<BackgroundTaskDetailDialog>().Single();
        Assert.Equal("f-1", dialog.Entry.TaskId);
    }

    /// <summary>单击面板溢出行（面板级 id）— 吞：不进入选中态、不锚选区（面板 UI 区域
    /// 对齐基准 Box onClick 包裹形态 CoordinatorAgentStatus.tsx:72-75）、无详情挂载。</summary>
    [Fact]
    public void ClickOverflowRow_Swallowed_NoSelectionNoAnchor()
    {
        var harness = Harness.Create();
        for (var i = 1; i <= 12; i++)
        {
            harness.Shell.View.BackgroundTasks.Upsert("fork", $"f-{i}", $"任务{i}", "running");
        }

        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "+2 more");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row));

        Assert.False(harness.Shell.View.BackgroundTasks.IsSelectionActive);
        Assert.False(harness.Shell.IsBackgroundTaskDetailOpen);
        Assert.Null(harness.Selection.Anchor);
        Assert.False(harness.Selection.IsSelecting);
    }

    /// <summary>单击消息区空白 — 命中 null 回落既有选区锚定链（零破坏）：选区状态机激活
    ///（IsSelecting）、面板无选中态。</summary>
    [Fact]
    public void ClickMessageArea_FallsBackToSelectionAnchor()
    {
        var harness = Harness.Create(seeds: [new AssistantMessageItem("鼠标回落选区测试")]);
        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "回落");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row));

        Assert.False(harness.Shell.View.BackgroundTasks.IsSelectionActive);
        Assert.True(harness.Selection.IsSelecting);
    }

    /// <summary>修饰键点击面板行 — 回落选区链（无修饰门槛：Shift+Press 不走命中分派 —
    /// 对齐单击语义门槛，修饰点击保留既有选区行为）。</summary>
    [Fact]
    public void ShiftClickPanelRow_FallsBackToSelectionChain()
    {
        var harness = Harness.Create();
        harness.Shell.View.BackgroundTasks.Upsert("fork", "f-1", "任务甲", "running");
        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "任务甲");

        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row, shift: true));

        Assert.False(harness.Shell.View.BackgroundTasks.IsSelectionActive);
        Assert.True(harness.Selection.IsSelecting);
    }

    /// <summary>选中态激活期间（详情已关）点击另一行 — 现有模态互斥吞（防守卫不变）：
    /// 选中索引保持、点击无效果（Esc 退出选中态后才能再点击）。</summary>
    [Fact]
    public void SelectionActive_ClickSwallowed()
    {
        var harness = Harness.Create();
        harness.Shell.View.BackgroundTasks.Upsert("fork", "f-1", "任务甲", "running");
        harness.Shell.View.BackgroundTasks.Upsert("teammate", "tm-1", "审阅乙", "running");
        harness.Shell.RenderAndFlush();
        var (row, col) = LocateWord(harness.Shell.FrontBuffer!, "任务甲");
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, col, row));
        Assert.Equal(0, harness.Shell.View.BackgroundTasks.SelectedIndex);
        // Esc 关详情（回选中态 — 详情 Esc 路由 HandleBackgroundTaskDetailKey；keyChar '\0'
        // 形态对齐 ReplShellPanelNavigationTests.Press 先例）
        harness.Shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        Assert.True(harness.Shell.View.BackgroundTasks.IsSelectionActive);
        Assert.False(harness.Shell.IsBackgroundTaskDetailOpen);

        // 选中态激活期间点击另一行 — 互斥吞：索引不切换
        var (otherRow, otherCol) = LocateWord(harness.Shell.FrontBuffer!, "审阅乙");
        harness.Shell.HandleRawKeyPress(MouseKey(MouseKind.Press, MouseButton.Left, otherCol, otherRow));

        Assert.Equal(0, harness.Shell.View.BackgroundTasks.SelectedIndex);
    }
}
