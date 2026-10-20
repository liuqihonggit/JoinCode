namespace NativeTui.Tests.Repl;

/// <summary>
/// 子代理分组测试（M7-7）— 对齐基准 AgentTool/UI.tsx 分组渲染形态：
/// SubagentGroupCoordinator 状态机（AddAgent 追加未完成条目 + RecordToolUse 累计工具数/令牌/最后活动 +
/// FinishAgent 定格 + FinalizePending 回合收尾 + 窗口 min(10,n) 活动跟随滚动）+ 组头三态文案
/// （Running N {type} agents… / N {type} agents finished，全同型带类型名）+ SubagentGroupItem 渲染
/// （● 组头三态色 + 逐代理两行式树形 ├─/└─ + │ ⎿ 活动行 + Initializing…/Done 状态 + "and N more…" 溢出 +
/// 运行中整行 dim + 八色板类型色）+ ReplBridgeDriver 路由（SubagentStarted 惰性建组落位 + 事件更新组 +
/// TurnCompleted 定格脱离 + 多回合多组共存）。
/// </summary>
public sealed class SubagentGroupTests
{
    /// <summary>构造三代理混合类型组（1 个有活动、1 个完成、1 个待初始化）。</summary>
    private static SubagentGroupCoordinator CreateMixedGroup()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索渲染链");
        coordinator.RecordToolUse("a1", "Read", "file.cs", 1200);
        coordinator.RecordToolUse("a1", "Grep", "pattern", 50);
        coordinator.AddAgent("a2", "reviewer", "审查补丁");
        coordinator.FinishAgent("a2", isError: false);
        coordinator.AddAgent("a3", "builder", "编译卫星");
        return coordinator;
    }

    // ---- SubagentGroupCoordinator ----

    /// <summary>AddAgent — 追加未完成条目 + AnyUnresolved 聚合。</summary>
    [Fact]
    public void AddAgent_AppendsPendingEntry()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");

        Assert.Equal(1, coordinator.Count);
        Assert.Equal("a1", coordinator.Agents[0].Id);
        Assert.Equal("explore", coordinator.Agents[0].AgentType);
        Assert.False(coordinator.Agents[0].IsDone);
        Assert.True(coordinator.AnyUnresolved);
        Assert.False(coordinator.AllComplete);
    }

    /// <summary>RecordToolUse — 工具数/令牌累计 + 最后活动更新。</summary>
    [Fact]
    public void RecordToolUse_AccumulatesCountsAndActivity()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.RecordToolUse("a1", "Read", "file.cs", 1200);
        coordinator.RecordToolUse("a1", "Grep", "pattern", 50);

        var agent = coordinator.Agents[0];
        Assert.Equal(2, agent.ToolUseCount);
        Assert.Equal(1250, agent.TokenCount);
        Assert.Equal("Grep", agent.LastToolName);
        Assert.Equal("pattern", agent.LastToolSummary);
    }

    /// <summary>FinishAgent — 定格完成 + 错误聚合 + AllComplete。</summary>
    [Fact]
    public void FinishAgent_MarksDoneAndAggregatesError()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.AddAgent("a2", "reviewer", "审查");
        coordinator.FinishAgent("a1", isError: true);
        coordinator.FinishAgent("a2", isError: false);

        Assert.True(coordinator.Agents[0].IsError);
        Assert.True(coordinator.AnyError);
        Assert.True(coordinator.AllComplete);
        Assert.False(coordinator.AnyUnresolved);
    }

    /// <summary>FinalizePending — 回合收尾把未完成条目定格为非错误完成。</summary>
    [Fact]
    public void FinalizePending_CompletesUnfinishedAsNonError()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.AddAgent("a2", "reviewer", "审查");
        coordinator.FinishAgent("a2", isError: false);

        coordinator.FinalizePending();

        Assert.True(coordinator.AllComplete);
        Assert.True(coordinator.Agents[0].IsDone);
        Assert.False(coordinator.Agents[0].IsError);
    }

    /// <summary>组头文案 — 运行中 "Running N agents…"，完成 "N agents finished"（混合类型不带类型名）。</summary>
    [Fact]
    public void HeaderText_MixedTypesRunningAndFinished()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.AddAgent("a2", "reviewer", "审查");
        Assert.Equal("Running 2 agents…", coordinator.HeaderText);

        coordinator.FinalizePending();
        Assert.Equal("2 agents finished", coordinator.HeaderText);
    }

    /// <summary>组头文案 — 全同类型带类型名（基准 commonType 拼接）。</summary>
    [Fact]
    public void HeaderText_SameTypeIncludesTypeName()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.AddAgent("a2", "explore", "深挖");
        Assert.Equal("Running 2 explore agents…", coordinator.HeaderText);

        coordinator.FinalizePending();
        Assert.Equal("2 explore agents finished", coordinator.HeaderText);
    }

    /// <summary>可见窗口 — min(10, n) 可见 + HiddenCount 溢出计数。</summary>
    [Fact]
    public void VisibleCount_CapsAtTenAndCountsHidden()
    {
        var coordinator = new SubagentGroupCoordinator();
        for (var i = 0; i < 13; i++)
        {
            coordinator.AddAgent($"a{i}", "explore", $"任务{i}");
        }

        Assert.Equal(10, coordinator.VisibleCount);
        Assert.Equal(3, coordinator.HiddenCount);
    }

    /// <summary>窗口活动跟随 — 第 11 个代理追加后窗口下移保证其可见。</summary>
    [Fact]
    public void WindowFollowsLatestActivity()
    {
        var coordinator = new SubagentGroupCoordinator();
        for (var i = 0; i < 10; i++)
        {
            coordinator.AddAgent($"a{i}", "explore", $"任务{i}");
        }

        Assert.Equal(0, coordinator.WindowStart);
        coordinator.AddAgent("a10", "explore", "新任务");
        Assert.Equal(1, coordinator.WindowStart);
    }

    /// <summary>未知代理 id — 断言（开心路径：Driver 保证先 Started 后进度）。</summary>
    [Fact]
    public void RecordToolUse_UnknownAgent_Throws()
    {
        var coordinator = new SubagentGroupCoordinator();

        Assert.Throws<KeyNotFoundException>(() => coordinator.RecordToolUse("ghost", "Read", "f", 1));
        Assert.Throws<KeyNotFoundException>(() => coordinator.FinishAgent("ghost", isError: false));
    }

    // ---- SubagentGroupItem 渲染 ----

    /// <summary>渲染组头 — ● 三态色（运行中 Suggestion）+ "Running N agents…" 文案。</summary>
    [Fact]
    public void Render_HeaderBulletAndRunningText()
    {
        using var screen = new Screen(cols: 60, rows: 10);
        var coordinator = CreateMixedGroup();
        new SubagentGroupItem(coordinator).Render(screen, new Rect(0, 0, 60, 10), Theme.Dark);

        Assert.StartsWith("● Running 3 agents…", RowText.Of(screen, 1, 0, 60));
        var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Suggestion });
        Assert.Equal(suggestion, screen[1, 0].StyleId);
    }

    /// <summary>渲染组头完成态 — ● Success 绿 + "N agents finished"。</summary>
    [Fact]
    public void Render_HeaderDoneGreen()
    {
        using var screen = new Screen(cols: 60, rows: 10);
        var coordinator = CreateMixedGroup();
        coordinator.FinalizePending();
        new SubagentGroupItem(coordinator).Render(screen, new Rect(0, 0, 60, 10), Theme.Dark);

        Assert.StartsWith("● 3 agents finished", RowText.Of(screen, 1, 0, 60));
        var success = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Success });
        Assert.Equal(success, screen[1, 0].StyleId);
    }

    /// <summary>渲染组头错误态 — 任一错误 ● Error 红（基准 anyError loader 变红）。</summary>
    [Fact]
    public void Render_HeaderErrorRed()
    {
        using var screen = new Screen(cols: 60, rows: 10);
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.FinishAgent("a1", isError: true);
        new SubagentGroupItem(coordinator).Render(screen, new Rect(0, 0, 60, 10), Theme.Dark);

        var error = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Error });
        Assert.Equal(error, screen[1, 0].StyleId);
    }

    /// <summary>渲染状态行 — 树形 ├─/└─ + 八色类型名 + (描述) + 统计 dim（基准 AgentProgressLine）。</summary>
    [Fact]
    public void Render_StatusRowTreeTypeAndStats()
    {
        using var screen = new Screen(cols: 60, rows: 10);
        var coordinator = CreateMixedGroup();
        new SubagentGroupItem(coordinator).Render(screen, new Rect(0, 0, 60, 10), Theme.Dark);

        // row2 = 首代理（运行中 dim）：├─ explore (搜索渲染链) · 2 tool uses · 1.3k tokens（1250 → 1.3k 四舍五入）
        Assert.StartsWith("├─ explore (搜索渲染链) · 2 tool uses · 1.3k tokens", RowText.Of(screen, 2, 0, 60));
        var typeColor = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.SubagentColor("explore"), Dim = true });
        Assert.Equal(typeColor, screen[2, 3].StyleId);

        // row6 = 末代理（└─ 收口树形）
        Assert.StartsWith("└─ builder (编译卫星)", RowText.Of(screen, 6, 0, 60));
    }

    /// <summary>渲染活动行 — "│  ⎿ 活动"（中间）/"   ⎿ Done"（末条目完成收口）。</summary>
    [Fact]
    public void Render_ActivityRowIndentAndStates()
    {
        using var screen = new Screen(cols: 60, rows: 10);
        var coordinator = CreateMixedGroup();
        new SubagentGroupItem(coordinator).Render(screen, new Rect(0, 0, 60, 10), Theme.Dark);

        // row3 = 首代理活动行：│  ⎿ Grep: pattern
        Assert.StartsWith("│  ⎿ Grep: pattern", RowText.Of(screen, 3, 0, 60));
        // row5 = 次代理（已完成）：│  ⎿ Done
        Assert.StartsWith("│  ⎿ Done", RowText.Of(screen, 5, 0, 60));
        // row7 = 末代理（无活动）：树形收口前缀 "   ⎿ Initializing…"
        Assert.StartsWith("   ⎿ Initializing…", RowText.Of(screen, 7, 0, 60));
    }

    /// <summary>渲染溢出行 — 超出 10 条可见折叠 "and N more…"（复用模型选择器溢出形态）。</summary>
    [Fact]
    public void Render_OverflowRowWhenHidden()
    {
        var coordinator = new SubagentGroupCoordinator();
        for (var i = 0; i < 13; i++)
        {
            coordinator.AddAgent($"a{i}", "explore", $"任务{i}");
        }

        using var screen = new Screen(cols: 60, rows: 30);
        new SubagentGroupItem(coordinator).Render(screen, new Rect(0, 0, 60, 30), Theme.Dark);

        // margin1 + 组头1 + 可见10×2 = 溢出行在 row22
        Assert.StartsWith(" and 3 more…", RowText.Of(screen, 22, 0, 60));
    }

    /// <summary>MeasureHeight — margin 1 + 组头 1 + 可见 N×2 + 溢出 0/1。</summary>
    [Fact]
    public void MeasureHeight_FollowsWindowState()
    {
        var small = new SubagentGroupItem(CreateMixedGroup());
        Assert.Equal(8, small.MeasureHeight(60));

        var coordinator = new SubagentGroupCoordinator();
        for (var i = 0; i < 13; i++)
        {
            coordinator.AddAgent($"a{i}", "explore", $"任务{i}");
        }

        Assert.Equal(23, new SubagentGroupItem(coordinator).MeasureHeight(60));
    }

    /// <summary>运行中行 dim / 完成行不 dim（基准 AgentProgressLine 运行中整行 dim）。</summary>
    [Fact]
    public void Render_RunningRowsDimDoneRowsNot()
    {
        using var screen = new Screen(cols: 60, rows: 10);
        var coordinator = CreateMixedGroup();
        new SubagentGroupItem(coordinator).Render(screen, new Rect(0, 0, 60, 10), Theme.Dark);

        // row2 运行中代理：类型色带 Dim
        var dimType = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.SubagentColor("explore"), Dim = true });
        Assert.Equal(dimType, screen[2, 3].StyleId);
        // row4 已完成代理：类型色无 Dim
        var plainType = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.SubagentColor("reviewer") });
        Assert.Equal(plainType, screen[4, 3].StyleId);
    }

    /// <summary>全同型组 — 行内隐藏类型名只显示描述（基准 hideType 去重）。</summary>
    [Fact]
    public void Render_SameTypeHidesTypeInRows()
    {
        var coordinator = new SubagentGroupCoordinator();
        coordinator.AddAgent("a1", "explore", "搜索");
        coordinator.AddAgent("a2", "explore", "深挖");

        using var screen = new Screen(cols: 60, rows: 8);
        new SubagentGroupItem(coordinator).Render(screen, new Rect(0, 0, 60, 8), Theme.Dark);

        Assert.StartsWith("├─ 搜索", RowText.Of(screen, 2, 0, 60));
        Assert.StartsWith("└─ 深挖", RowText.Of(screen, 4, 0, 60));
    }

    // ---- ReplBridgeDriver 路由 ----

    /// <summary>构造测试 Driver（对齐 ReplBridgeDriverTests 模式）。</summary>
    private static (ReplBridgeDriver Driver, ReplView View, SpinnerItem Spinner) CreateDriver()
    {
        var view = new ReplView();
        var spinner = new SpinnerItem();
        var banner = new BannerItem("0.0.0", "占位模型", "API 按量计费", @"C:\placeholder");
        var driver = new ReplBridgeDriver(view, spinner, banner);
        view.Messages.Add(banner);
        view.Messages.Add(spinner);
        return (driver, view, spinner);
    }

    /// <summary>SubagentStarted — 惰性建组：组项落位滚动区 + spinner 切 ToolUse。</summary>
    [Fact]
    public void Driver_SubagentStarted_CreatesGroupItem()
    {
        var (driver, view, spinner) = CreateDriver();

        driver.Apply(new BridgeEvent.SubagentStarted("a1", "explore", "搜索"));

        Assert.Contains(view.Messages.Items, m => m is SubagentGroupItem);
        Assert.Equal(SpinnerMode.ToolUse, spinner.Mode);
        Assert.True(spinner.HasActiveTools);
    }

    /// <summary>子代理事件链 — 进度/完成路由到组条目（不逐条刷屏）。</summary>
    [Fact]
    public void Driver_SubagentEvents_UpdateGroupInPlace()
    {
        var (driver, view, _) = CreateDriver();

        driver.Apply(new BridgeEvent.SubagentStarted("a1", "explore", "搜索"));
        driver.Apply(new BridgeEvent.SubagentToolUse("a1", "Read", "file.cs", 100));
        driver.Apply(new BridgeEvent.SubagentFinished("a1", IsError: false));

        var group = view.Messages.Items.OfType<SubagentGroupItem>().Single();
        Assert.Equal(1, group.Coordinator.Count);
        Assert.Equal(1, group.Coordinator.Agents[0].ToolUseCount);
        Assert.True(group.Coordinator.AllComplete);
        // 一个代理 = 组项 1 个（无逐条 ToolCallItem/ToolResultItem 刷屏）
        Assert.DoesNotContain(view.Messages.Items, m => m is ToolCallItem);
    }

    /// <summary>TurnCompleted — 组定格脱离 + 新回合新组（两组共存滚动区）。</summary>
    [Fact]
    public void Driver_TurnCompleted_FinalizesAndNewTurnCreatesNewGroup()
    {
        var (driver, view, _) = CreateDriver();

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.SubagentStarted("a1", "explore", "搜索"));
        driver.Apply(new BridgeEvent.TurnCompleted());
        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.SubagentStarted("b1", "reviewer", "审查"));

        var groups = view.Messages.Items.OfType<SubagentGroupItem>().ToList();
        Assert.Equal(2, groups.Count);
        Assert.True(groups[0].Coordinator.AllComplete);
        Assert.Equal("b1", groups[1].Coordinator.Agents[0].Id);
    }

    /// <summary>无组子代理事件 — 容忍忽略（Finish/ToolUse 先于 Started 的健壮性）。</summary>
    [Fact]
    public void Driver_SubagentEventsWithoutGroup_Ignored()
    {
        var (driver, view, _) = CreateDriver();

        driver.Apply(new BridgeEvent.SubagentToolUse("ghost", "Read", "f", 1));
        driver.Apply(new BridgeEvent.SubagentFinished("ghost", IsError: false));

        Assert.DoesNotContain(view.Messages.Items, m => m is SubagentGroupItem);
    }
}
