namespace NativeTui.Tests.Bridge;

/// <summary>
/// Driver 新增路由测试 — TokenUsage 事件转交（S4 契约 TokenUsageTracker.RecordUsage）与
/// ToolResult 扩展字段透传（S2 契约 ToolResultItem(Summary, Patch, RawOutput, HiddenLines) 统一结果项）：
/// tracker 未注入时 token_usage 静默容忍（纯视图场景）；tool_result 的 patch/rawOutput/hiddenLines
/// 全量透传渲染层（Driver 只路由不解释）。带 tracker 的 RecordUsage 调用断言归 S4 集成后补充
///（tracker 构造契约归 S4，本侧仅锁定 null 容忍与路由落位）。
/// </summary>
public sealed class ReplBridgeDriverExtensionTests
{
    private readonly ReplView _view = new();
    private readonly SpinnerItem _spinner = new();
    private readonly BannerItem _banner = new("0.0.0", "占位模型", "API 按量计费", @"C:\placeholder");
    private readonly ReplBridgeDriver _driver;

    public ReplBridgeDriverExtensionTests()
    {
        _driver = new ReplBridgeDriver(_view, _spinner, _banner);
        _view.Messages.Add(_banner);
        _view.Messages.Add(_spinner);
    }

    /// <summary>tracker 未注入（null）时 token_usage 事件静默容忍 — 不抛异常、不落消息项、不改 busy 态。</summary>
    [Fact]
    public void Apply_TokenUsage_WithoutTracker_ToleratedSilently()
    {
        var countBefore = _view.Messages.Count;

        _driver.Apply(new BridgeEvent.TokenUsage(120, 45, 980, 131072));

        Assert.Equal(countBefore, _view.Messages.Count);
        Assert.False(_driver.IsBusy);
    }

    /// <summary>token_usage 事件在回合内到达 — busy 态不受影响（记账不驱动回合生命周期）。</summary>
    [Fact]
    public void Apply_TokenUsage_DuringTurn_KeepsBusyState()
    {
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.TokenUsage(10, 5));

        Assert.True(_driver.IsBusy);
        Assert.True(_view.Footer.Busy);
    }

    /// <summary>ToolResult 带原始输出 — 路由为统一 ToolResultItem（S2 契约 4 参透传：rawOutput/hiddenLines 落渲染层）。</summary>
    [Fact]
    public void Apply_ToolResultWithRawOutput_RoutesUnifiedToolResultItem()
    {
        _driver.Apply(new BridgeEvent.ToolResult("5 lines", RawOutput: "a\nb\nc\nd\ne", HiddenLines: 2));

        Assert.IsType<ToolResultItem>(_view.Messages.Items[^2]);
        Assert.Same(_spinner, _view.Messages.Items[^1]);
        Assert.False(_spinner.HasActiveTools);
    }

    /// <summary>ToolResult 带补丁 — 统一路由 ToolResultItem（S2 契约 Patch 并入结果项，DiffResultItem 分流退役）。</summary>
    [Fact]
    public void Apply_ToolResultWithPatch_RoutesUnifiedToolResultItem()
    {
        var hunks = new[]
        {
            new DiffHunk { Header = "@@ -1 +1 @@", Lines = [new DiffLine { Kind = DiffLineKind.Added, Content = "new" }] },
        };

        _driver.Apply(new BridgeEvent.ToolResult("Edited a.cs", hunks));

        Assert.IsType<ToolResultItem>(_view.Messages.Items[^2]);
        Assert.Same(_spinner, _view.Messages.Items[^1]);
        Assert.False(_spinner.HasActiveTools);
    }

    /// <summary>完整回合含 token_usage — 事件序透传无干扰（消息落位/spinner 复位既有语义不变）。</summary>
    [Fact]
    public void Apply_FullTurnWithTokenUsage_KeepsEstablishedSemantics()
    {
        BridgeEvent[] turn =
        [
            new BridgeEvent.TurnStarted(),
            new BridgeEvent.ToolCall("Read", "src/main.cs"),
            new BridgeEvent.ToolResult("41 lines"),
            new BridgeEvent.TokenDelta(24),
            new BridgeEvent.AssistantMessage("回复正文"),
            new BridgeEvent.TokenUsage(120, 45, 980, 131072),
            new BridgeEvent.TurnCompleted(),
        ];
        foreach (var bridgeEvent in turn)
        {
            _driver.Apply(bridgeEvent);
        }

        Assert.False(_driver.IsBusy);
        Assert.False(_view.Footer.Busy);
        Assert.Equal(0, _spinner.ResponseLength);
        Assert.Same(_spinner, _view.Messages.Items[^1]);
        Assert.IsType<ToolCallItem>(_view.Messages.Items[^4]);
        Assert.IsType<ToolResultItem>(_view.Messages.Items[^3]);
        Assert.IsType<AssistantMessageItem>(_view.Messages.Items[^2]);
    }
}
