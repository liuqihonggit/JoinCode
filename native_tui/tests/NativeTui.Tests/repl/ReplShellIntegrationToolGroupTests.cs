namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplBridgeDriver 工具聚合接线集成测试（S6 任务 7 — S1 域产出消费）：
/// 连续同类 Read/Grep/Glob 聚合为 CollapsedToolGroupItem（不落逐条 ToolCallItem，
/// ToolResult 亦被组吸收）；非 Accepts 工具中断 → 收组定格（IsActive=false）+ 中断工具逐条落位；
/// 回合边界（TurnCompleted/TurnStarted）收组；小终端塌缩（ShouldCondense）→
/// CondensedToolProgressItem 单行替代（计数归零移除）；大终端不塌缩。
/// </summary>
public sealed class ReplShellIntegrationToolGroupTests
{
    /// <summary>构造 Driver + 视图（尺寸源可注入 — 塌缩路由输入）。</summary>
    private static (ReplBridgeDriver Driver, ReplView View) CreateDriver(int rows = 80)
    {
        var view = new ReplView();
        var spinner = new SpinnerItem();
        var banner = new BannerItem("1.0.0", "m", "b", "/tmp");
        view.Messages.Add(banner);
        view.Messages.Add(spinner);
        var driver = new ReplBridgeDriver(view, spinner, banner, sizeSource: new FakeTerminalSizeSource(80, rows));
        return (driver, view);
    }

    private static IReadOnlyList<IScrollItem> ItemsOf(ReplView view) => view.Messages.Items;

    /// <summary>连续 2 条 Read ToolCall → 无逐条 ToolCallItem + 单个聚合组项（计数 2）。</summary>
    [Fact]
    public void ConsecutiveReads_AggregateToSingleGroup()
    {
        var (driver, view) = CreateDriver();

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "a.cs"}"""));
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "b.cs"}"""));

        Assert.Empty(ItemsOf(view).OfType<ToolCallItem>());
        var group = Assert.Single(ItemsOf(view).OfType<CollapsedToolGroupItem>());
        Assert.Equal(2, group.Aggregator.ReadCount);
        Assert.True(group.IsActive);
    }

    /// <summary>聚合组内 ToolResult 被组吸收 — 不落逐条 ToolResultItem。</summary>
    [Fact]
    public void GroupedToolResult_Absorbed_NoPerItemResult()
    {
        var (driver, view) = CreateDriver();

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "a.cs"}"""));
        driver.Apply(new BridgeEvent.ToolResult("ok", null, null, 0));

        Assert.Empty(ItemsOf(view).OfType<ToolResultItem>());
        var group = Assert.Single(ItemsOf(view).OfType<CollapsedToolGroupItem>());
        Assert.False(group.Aggregator.HasPendingToolCall);
    }

    /// <summary>非 Accepts 工具中断（Bash）→ 收组定格（IsActive=false）+ Bash 逐条落位。</summary>
    [Fact]
    public void NonAcceptsTool_ClosesGroup_AndAddsPerItem()
    {
        var (driver, view) = CreateDriver();

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "a.cs"}"""));
        driver.Apply(new BridgeEvent.ToolCall("Bash", """{"command": "ls"}"""));

        var group = Assert.Single(ItemsOf(view).OfType<CollapsedToolGroupItem>());
        Assert.False(group.IsActive);
        var bash = Assert.Single(ItemsOf(view).OfType<ToolCallItem>());
        Assert.Equal("Bash", bash.ToolName);
    }

    /// <summary>收组后再来同类工具 → 开新组（连续同类中断后新序列独立聚合）。</summary>
    [Fact]
    public void AfterInterruption_NewSequenceOpensNewGroup()
    {
        var (driver, view) = CreateDriver();

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "a.cs"}"""));
        driver.Apply(new BridgeEvent.ToolCall("Bash", """{"command": "ls"}"""));
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "b.cs"}"""));

        Assert.Equal(2, ItemsOf(view).OfType<CollapsedToolGroupItem>().Count());
        Assert.Single(ItemsOf(view).OfType<ToolCallItem>());
    }

    /// <summary>回合边界（TurnCompleted）→ 收组定格（IsActive=false）。</summary>
    [Fact]
    public void TurnCompleted_ClosesActiveGroup()
    {
        var (driver, view) = CreateDriver();

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.ToolCall("Grep", """{"pattern": "foo"}"""));
        driver.Apply(new BridgeEvent.TurnCompleted());

        var group = Assert.Single(ItemsOf(view).OfType<CollapsedToolGroupItem>());
        Assert.False(group.IsActive);
    }

    /// <summary>小终端塌缩 — 计数达到阈值（rows < N*9+7）→ CondensedToolProgressItem 单行替代（不建组项）。</summary>
    [Fact]
    public void SmallTerminal_CondensesToSingleLine()
    {
        var (driver, view) = CreateDriver(rows: 20);

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "a.cs"}"""));
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "b.cs"}"""));

        Assert.Empty(ItemsOf(view).OfType<ToolCallItem>());
        Assert.Empty(ItemsOf(view).OfType<CollapsedToolGroupItem>());
        var condensed = Assert.Single(ItemsOf(view).OfType<CondensedToolProgressItem>());
        Assert.Equal(2, condensed.ToolUses);
    }

    /// <summary>塌缩态 ToolResult 计数递减 — 归零时塌缩项移除。</summary>
    [Fact]
    public void CondensedMode_CountReachesZero_RemovesItem()
    {
        var (driver, view) = CreateDriver(rows: 20);

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "a.cs"}"""));
        driver.Apply(new BridgeEvent.ToolResult("ok", null, null, 0));

        Assert.Empty(ItemsOf(view).OfType<CondensedToolProgressItem>());
    }

    /// <summary>大终端不塌缩 — 常规聚合路由（ShouldCondense false）。</summary>
    [Fact]
    public void LargeTerminal_DoesNotCondense()
    {
        var (driver, view) = CreateDriver(rows: 80);

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "a.cs"}"""));
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "b.cs"}"""));

        Assert.Empty(ItemsOf(view).OfType<CondensedToolProgressItem>());
        Assert.Single(ItemsOf(view).OfType<CollapsedToolGroupItem>());
    }

    /// <summary>未注入尺寸源的 Driver — 恒不塌缩（聚合/逐条路由不受影响）。</summary>
    [Fact]
    public void NoSizeSource_NeverCondenses()
    {
        var view = new ReplView();
        var spinner = new SpinnerItem();
        var banner = new BannerItem("1.0.0", "m", "b", "/tmp");
        view.Messages.Add(banner);
        view.Messages.Add(spinner);
        var driver = new ReplBridgeDriver(view, spinner, banner);

        driver.Apply(new BridgeEvent.TurnStarted());
        driver.Apply(new BridgeEvent.ToolCall("Read", """{"file_path": "a.cs"}"""));

        Assert.Empty(view.Messages.Items.OfType<CondensedToolProgressItem>());
    }
}
