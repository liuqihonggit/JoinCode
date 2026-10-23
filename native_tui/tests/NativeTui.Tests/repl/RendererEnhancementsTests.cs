namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK066 板块 F 渲染细节三件套测试 —
/// F4 spinner lastToolInfo（基准 AgentTool/UI.tsx:788-868 extractLastToolInfo +
/// AgentProgressLine.tsx:117 `⎿  {name}: {summary}` dim 下一行形态）；
/// F3 小终端塌缩（基准 PromptInputFooter.tsx:110 isShort = rows &lt; 24 — footer 附加行丢弃；
/// AgentTool/UI.tsx:469-470 rows &lt; N*9+7 工具进度塌缩在 Driver 域既有实现）；
/// F6 虚拟滚动 overscan（基准 useVirtualScroll.ts:24 OVERSCAN_ROWS=80 视口±N 行窗口 +
/// OffscreenFreeze.tsx 离屏冻结=渲染跳过高度保留 + ink 输出层视口裁剪防写穿）。
/// </summary>

/// <summary>F4 — spinner 最近完成工具信息行：记录（ToolCall pending）/消费（ToolResult →
/// spinner.LastToolInfo）/中断标记（TurnInterrupted 追加"（已中断）"）/渲染形态
///（spinner 行下方 dim `⎿  ` 缩进）/塌缩抑制（Compact）。</summary>
public sealed class LastToolInfoTests
{
    [Fact]
    public void Spinner_LastToolInfo_RendersDimIndentedRow()
    {
        using var screen = new Screen(cols: 80, rows: 5);
        var spinner = new SpinnerItem { Message = "读取中", LastToolInfo = "Read: a.cs" };

        Assert.Equal(3, spinner.MeasureHeight(80));

        spinner.Render(screen, new Rect(0, 0, 80, 5), Theme.Dark);

        // lastToolInfo 行（spinner 行下方 — 基准 AgentProgressLine.tsx:117 paddingLeft 3 + dim ⎿ 前缀）
        Assert.Equal("  ⎿  Read: a.cs", RowText.Of(screen, 2, 0, 16).TrimEnd());
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[2, 0].StyleId);
    }

    [Fact]
    public void Spinner_LastToolInfo_Null_KeepsTwoRows()
    {
        var spinner = new SpinnerItem { Message = "读取中" };

        Assert.Equal(2, spinner.MeasureHeight(80));
    }

    [Fact]
    public void Spinner_LastToolInfo_Compact_Suppressed()
    {
        using var screen = new Screen(cols: 80, rows: 5);
        var spinner = new SpinnerItem { Message = "读取中", LastToolInfo = "Read: a.cs", Compact = true };

        // 塌缩抑制（TASK066 F3 — 基准 condensed mode 进度细节不渲染）
        Assert.Equal(2, spinner.MeasureHeight(80));

        spinner.Render(screen, new Rect(0, 0, 80, 5), Theme.Dark);

        Assert.Equal(Cell.Blank, screen[2, 0]);
    }

    [Fact]
    public void Spinner_LastToolInfo_TipRowShiftedBelow()
    {
        using var screen = new Screen(cols: 80, rows: 6);
        var spinner = new SpinnerItem
        {
            Message = "读取中",
            LastToolInfo = "Read: a.cs",
            SpinnerTip = "试试方向键",
        };

        // margin 1 + spinner 行 1 + lastToolInfo 1 + tip 1 = 4
        Assert.Equal(4, spinner.MeasureHeight(80));

        spinner.Render(screen, new Rect(0, 0, 80, 6), Theme.Dark);

        Assert.Equal("  ⎿  Read: a.cs", RowText.Of(screen, 2, 0, 16).TrimEnd());
        Assert.Equal("  ⎿  提示: 试试方向键", RowText.Of(screen, 3, 0, 22).TrimEnd());
    }

    [Fact]
    public void ToolTitleFormatter_FormatToolInfo_FirstLineAndEditDerivation()
    {
        // Bash 多行命令取首行（单行化 — AgentProgressLine 单行形态；JSON \\n 转义解析后真实换行）
        Assert.Equal("Bash: cd x", ToolTitleFormatter.FormatToolInfo("Bash", "{\"command\": \"cd x\\nls\"}"));
        // Edit old_string 空 → Create 派生（FileEditTool/UI.tsx userFacingName）
        Assert.Equal("Create: a.cs", ToolTitleFormatter.FormatToolInfo("Edit", "{\"file_path\": \"a.cs\", \"old_string\": \"\"}"));
        // 纯文本降级原文
        Assert.Equal("Read: a.cs", ToolTitleFormatter.FormatToolInfo("Read", "a.cs"));
    }

    [Fact]
    public void ToolTitleFormatter_FormatToolInfo_TruncatesUnknownToolLongArgs()
    {
        var longArgs = new string('x', 200);

        var info = ToolTitleFormatter.FormatToolInfo("Unknown", longArgs);

        Assert.Equal($"Unknown: {new string('x', 160)}…", info);
    }
}

/// <summary>F4 数据源 — Driver 工具事件路由记录/消费/中断标记/回合重置。</summary>
public sealed class LastToolInfoDriverTests
{
    private readonly ReplView _view = new();
    private readonly SpinnerItem _spinner = new();
    private readonly ReplBridgeDriver _driver;

    public LastToolInfoDriverTests()
    {
        _driver = new ReplBridgeDriver(_view, _spinner, new BannerItem("0.0.0", "占位模型", "API 按量计费", @"C:\placeholder"));
        _view.Messages.Add(_spinner);
    }

    [Fact]
    public void ToolCallThenResult_RecordsLastToolInfo()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));

        // 进行中不显示（基准 extractLastToolInfo = 最近 tool_result 对应的 tool_use）
        Assert.Null(_spinner.LastToolInfo);

        _driver.Apply(new BridgeEvent.ToolResult("42 lines", null, null, 0));

        Assert.Equal("Read: a.cs", _spinner.LastToolInfo);
    }

    [Fact]
    public void ToolCallPending_AggregatedPathAlsoRecords()
    {
        // 聚合组吸收路径（Read/Grep/Glob 连续同类不落逐条 ToolCallItem）同样记录 pending
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));
        _driver.Apply(new BridgeEvent.ToolCall("Read", "b.cs"));
        _driver.Apply(new BridgeEvent.ToolResult("1", null, null, 0));

        Assert.Equal("Read: b.cs", _spinner.LastToolInfo);
    }

    [Fact]
    public void InterruptedPendingTool_Marked()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "{\"command\": \"npm test\"}"));

        _driver.Apply(new BridgeEvent.TurnInterrupted());

        Assert.Equal("Bash: npm test（已中断）", _spinner.LastToolInfo);
    }

    [Fact]
    public void CompletedWithoutPending_Unmarked()
    {
        // 正常完成（引擎漏发 result 的收尾）— 不追加中断标记，pending 清引用
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));
        _driver.Apply(new BridgeEvent.ToolResult("ok", null, null, 0));

        _driver.Apply(new BridgeEvent.TurnCompleted());

        Assert.Equal("Read: a.cs", _spinner.LastToolInfo);
    }

    [Fact]
    public void ResultWithoutPending_KeepsLastValue()
    {
        // 异常序（无 pending 的 result）— 保持原值不崩
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolResult("orphan", null, null, 0));

        Assert.Null(_spinner.LastToolInfo);
    }

    [Fact]
    public void BeginTurn_ResetsLastToolInfo()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));
        _driver.Apply(new BridgeEvent.ToolResult("ok", null, null, 0));
        _driver.Apply(new BridgeEvent.TurnCompleted());

        _driver.Apply(new BridgeEvent.TurnStarted());

        Assert.Null(_spinner.LastToolInfo);
    }
}

/// <summary>F3 — 小终端塌缩：阈值上零破坏（正常渲染逐字节不变）/阈值下 footer 单行化
///（基准 PromptInputFooter.tsx:110 isShort = isFullscreen &amp;&amp; rows &lt; 24 塌 StatusLine →
/// C# footer Row2 通知/告警行丢弃）+ spinner lastToolInfo 行抑制（ICompactAware 投影）+
/// inline 门控（ViewportRender=false 不塌 — 基准非全屏 scrollback 吸收溢出语义）。</summary>
public sealed class CompactRenderTests
{
    private static FooterState CompactFooterState() => new()
    {
        Mode = PermissionMode.Plan,
        Notification = "◐ 中等 · /effort",
    };

    [Fact]
    public void RowsAboveThreshold_FooterTwoRows_ZeroBreakage()
    {
        using var screen = new Screen(cols: 60, rows: 30);
        var view = new ReplView { ViewportRender = true, Footer = CompactFooterState() };
        view.Render(screen, Theme.Dark);

        // footer 两行（行 28-29）：行 29 右侧通知（正常路径零破坏）
        Assert.Equal("◐ 中等 · /effort", RowText.Of(screen, 29, 42, 16));
    }

    [Fact]
    public void RowsAtThreshold24_IsNormal()
    {
        // rows < 24 严格小于 — 24 本身走正常路径
        using var screen = new Screen(cols: 60, rows: 24);
        var view = new ReplView { ViewportRender = true, Footer = CompactFooterState() };
        view.Render(screen, Theme.Dark);

        Assert.Equal("◐ 中等 · /effort", RowText.Of(screen, 23, 42, 16));
    }

    [Fact]
    public void InlineMode_SmallScreen_NeverCollapses()
    {
        // inline 门控（基准 PromptInputFooter.tsx:106-110 注释：非全屏有终端 scrollback
        // 吸收溢出，永不隐藏 StatusLine）：ViewportRender=false 时 rows=10 也不塌 —
        // footer 2 行完整保留（既有 ReplViewTests rows=10 golden 同语义回归）
        using var screen = new Screen(cols: 60, rows: 10);
        var view = new ReplView { Footer = CompactFooterState() };
        view.Render(screen, Theme.Dark);

        Assert.Equal("◐ 中等 · /effort", RowText.Of(screen, 9, 42, 16));
    }

    [Fact]
    public void RowsBelowThreshold_FooterSingleRow()
    {
        using var screen = new Screen(cols: 60, rows: 20);
        var view = new ReplView { ViewportRender = true, Footer = CompactFooterState() };
        view.Render(screen, Theme.Dark);

        // footer 钳单行（行 19）：模式串保留（行 1 左侧）
        Assert.StartsWith("⏸ 计划模式 已开启", RowText.Of(screen, 19, 2, 30).TrimEnd());
        // Row2 通知被丢弃（footer 上方是输入底边线行 18 — 通知不出现在任何行）
        Assert.Equal(new string('─', 60), RowText.Of(screen, 18, 0, 60));
        Assert.Equal(string.Empty, RowText.Of(screen, 19, 42, 16).TrimEnd());
    }

    [Fact]
    public void RowsBelowThreshold_InputShiftsDownOneRow()
    {
        using var screen = new Screen(cols: 60, rows: 20);
        var view = new ReplView { ViewportRender = true, Footer = CompactFooterState(), Input = "hi", CursorCol = 2 };
        view.Render(screen, Theme.Dark);

        // 塌缩省出 1 行给消息区（footer 2→1 行，输入区随 footer 顶下移 1 行）：
        // 顶边线 16 / 文本 17 / 底边线 18（非塌缩时为 15/16/17）
        Assert.Equal(new string('─', 60), RowText.Of(screen, 16, 0, 60));
        Assert.Equal("❯ hi", RowText.Of(screen, 17, 0, 4).TrimEnd());
        Assert.Equal(new string('─', 60), RowText.Of(screen, 18, 0, 60));
    }

    [Fact]
    public void RowsBelowThreshold_SpinnerCompactProjected()
    {
        using var compactScreen = new Screen(cols: 60, rows: 20);
        var compactView = new ReplView { ViewportRender = true, Footer = CompactFooterState() };
        var spinner = new SpinnerItem { Message = "读取中", LastToolInfo = "Read: a.cs" };
        compactView.Messages.Add(spinner);
        compactView.Render(compactScreen, Theme.Dark);

        // ICompactAware 投影：rows < 24 → spinner.Compact = true + lastToolInfo 行抑制（行 2 无工具信息）
        Assert.True(spinner.Compact);
        Assert.DoesNotContain("Read: a.cs", RowText.Of(compactScreen, 2, 0, 60));

        using var normalScreen = new Screen(cols: 60, rows: 30);
        var normalView = new ReplView { ViewportRender = true, Footer = CompactFooterState() };
        var normalSpinner = new SpinnerItem { Message = "读取中", LastToolInfo = "Read: a.cs" };
        normalView.Messages.Add(normalSpinner);
        normalView.Render(normalScreen, Theme.Dark);

        // 阈值上不投影（零破坏）+ lastToolInfo 行正常渲染（行 2 ⎿ 缩进 dim 形态）
        Assert.False(normalSpinner.Compact);
        Assert.Contains("Read: a.cs", RowText.Of(normalScreen, 2, 0, 60));
    }

    [Fact]
    public void RowsBelowThreshold_SpinnerLastToolInfoRowSuppressed()
    {
        using var screen = new Screen(cols: 60, rows: 20);
        var view = new ReplView { ViewportRender = true, Footer = CompactFooterState() };
        var spinner = new SpinnerItem { Message = "读取中", LastToolInfo = "Read: a.cs" };
        view.Messages.Add(spinner);
        view.Render(screen, Theme.Dark);

        // 塌缩态整屏消息区无 lastToolInfo 文本（Compact 抑制 — spinner 行下方无 ⎿ 工具信息行；
        // 消息区 0-14 行、spinner margin 行 0 + 动画行 1）
        for (var row = 2; row < 15; row++)
        {
            Assert.DoesNotContain("Read: a.cs", RowText.Of(screen, row, 0, 60));
        }
    }
}

/// <summary>F6 — 虚拟滚动 overscan：窗口保留（视口外 ±N 行项仍 Render）/远项冻结
///（渲染跳过高度保留）/写入 clip 防写穿（对齐基准 ink 输出层裁剪）/默认 0 零破坏。</summary>
public sealed class OverscanScrollTests
{
    /// <summary>可控桩项 — 固定高度 + 计数 Render 调用（冻结断言面）。</summary>
    private sealed class StubItem(int height, string marker) : IScrollItem
    {
        public int RenderCount;

        public int MeasureHeight(int width) => height;

        public void Render(Screen screen, Rect frame, Theme theme)
        {
            RenderCount++;
            for (var row = frame.Y; row < frame.Y + height; row++)
            {
                screen.WriteText(row, frame.X, marker, 0, frame.Right);
            }
        }
    }

    private static List<StubItem> AddItems(ScrollBox box, int count, int height = 2)
    {
        var items = new List<StubItem>();
        for (var i = 0; i < count; i++)
        {
            var item = new StubItem(height, $"m{i:x}");
            box.Add(item);
            items.Add(item);
        }

        return items;
    }

    [Fact]
    public void OverscanZero_RendersExactlyIntersecting()
    {
        // 默认 0 = 精确视口 culling 既有语义零破坏：scrollTop=10 视口 [10,15) →
        // item5（10-12）/ item6（12-14）/ item7（14-16 部分相交）= 3 项
        using var screen = new Screen(cols: 40, rows: 20);
        var box = new ScrollBox();
        AddItems(box, 10);

        box.ScrollBy(10);
        box.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal(3, box.LastRenderedCount);
    }

    [Fact]
    public void OverscanWindow_KeepsOffscreenItemsRendered()
    {
        // overscan=4：窗口 [scrollTop-4, windowBottom+4) = [6, 19) → item3..item9 渲染
        //（item3/item9 在精确视口 [10,15) 外 — 视口外 ±N 行仍保留 Render 调用）
        using var screen = new Screen(cols: 40, rows: 20);
        var box = new ScrollBox { OverscanRows = 4 };
        var items = AddItems(box, 10);

        box.ScrollBy(10);
        box.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal(7, box.LastRenderedCount);
        Assert.Equal(1, items[3].RenderCount);
        Assert.Equal(1, items[9].RenderCount);
    }

    [Fact]
    public void FarItems_FrozenNotRendered_HeightReserved()
    {
        // 离屏冻结：窗口外更远项跳过 Render（item0..item2、无）；高度仍全量计高
        //（MeasureContentHeight 全量 — 滚动位置语义不变，对齐基准 OffscreenFreeze 高度保留）
        using var screen = new Screen(cols: 40, rows: 20);
        var box = new ScrollBox { OverscanRows = 4 };
        var items = AddItems(box, 10);

        box.ScrollBy(10);
        box.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        Assert.Equal(0, items[0].RenderCount);
        Assert.Equal(0, items[2].RenderCount);
        // 高度占位保留：10 项 × 2 行 = 20（冻结不减高度 — maxScroll 语义不变）
        Assert.Equal(20, box.MeasureContentHeight(40));
    }

    [Fact]
    public void OverscanWrites_ClippedToFrame()
    {
        // 写入 clip：overscan 项平移后 Y 越出 frame 的写入被裁（不写穿滚动区边界 —
        // 对齐基准 ink 输出层视口裁剪；Render 副作用保留）
        using var screen = new Screen(cols: 40, rows: 20);
        var box = new ScrollBox { OverscanRows = 4 };
        AddItems(box, 10);

        box.ScrollBy(10);
        box.Render(screen, new Rect(0, 0, 40, 5), Theme.Dark);

        // 视口内照常：item5（top 10 → Y=0，行 0-1）marker 落屏
        Assert.Equal("m5", RowText.Of(screen, 0, 0, 2));
        // 视口内部分相交项 item7（top 14 → Y=4，行 4-5）：行 4 落屏、行 5 被 clip 裁
        Assert.Equal("m7", RowText.Of(screen, 4, 0, 2));
        Assert.Equal(Cell.Blank, screen[5, 0]);
        // 视口外 overscan 项（item8 top 16 → Y=6 行 6-7 / item9 top 18 → Y=8 行 8-9）：
        // Render 调用保留（窗口内）但写入被 clip — 不写穿 frame（行 5-19 全空）
        Assert.Equal(Cell.Blank, screen[6, 0]);
        Assert.Equal(Cell.Blank, screen[8, 0]);
        Assert.Equal(Cell.Blank, screen[9, 0]);
    }

    [Fact]
    public void ReplView_Messages_UseDefaultOverscan()
    {
        // ReplView 消息滚动区接线基准值 80（useVirtualScroll.ts:24）
        var view = new ReplView();

        Assert.Equal(ScrollBox.DefaultOverscanRows, view.Messages.OverscanRows);
        Assert.Equal(80, ScrollBox.DefaultOverscanRows);
    }

    [Fact]
    public void ClipScope_NestedPopAndEmptyStackSafe()
    {
        // using 配对结构性保障：嵌套作用域逐层弹出；空栈 Dispose 幂等防御
        using var screen = new Screen(cols: 10, rows: 10);
        using (screen.ClipScope(new Rect(0, 0, 10, 5)))
        {
            screen.WriteText(7, 0, "x", 0, 10);
            Assert.Equal(Cell.Blank, screen[7, 0]);

            using (screen.ClipScope(new Rect(0, 0, 10, 10)))
            {
                screen.WriteText(7, 0, "y", 0, 10);
                Assert.Equal('y', (char)screen[7, 0].Value.Value);
            }

            // 内层弹出后回到外层约束
            screen.WriteText(7, 1, "z", 0, 10);
            Assert.Equal(Cell.Blank, screen[7, 1]);
        }

        // 全部弹出后恢复全屏可写
        screen.WriteText(7, 0, "w", 0, 10);
        Assert.Equal('w', (char)screen[7, 0].Value.Value);
    }
}
