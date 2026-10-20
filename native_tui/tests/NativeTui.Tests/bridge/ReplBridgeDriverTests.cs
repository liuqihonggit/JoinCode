namespace NativeTui.Tests.Bridge;

/// <summary>
/// bridge 事件 → REPL 视图应用器测试 — ADR 0141 事件语义的唯一翻译层：
/// 会话握手（session_info 更新 banner 真实数据 + footer 模式 + bypass 可用性）、
/// 权限模式回执（permission_mode_changed 更新 footer）、
/// 回合生命周期（busy/spinner 模式/复位）、消息事件插到 spinner 前、
/// token_delta 累加、错误消息项、spinner 恒为滚动区末项。
/// </summary>
public sealed class ReplBridgeDriverTests
{
    private readonly ReplView _view = new();
    private readonly SpinnerItem _spinner = new();
    private readonly BannerItem _banner = new("0.0.0", "占位模型", "API 按量计费", @"C:\placeholder");
    private readonly ReplBridgeDriver _driver;

    public ReplBridgeDriverTests()
    {
        _driver = new ReplBridgeDriver(_view, _spinner, _banner);
        _view.Messages.Add(_banner);
        _view.Messages.Add(_spinner);
    }

    [Fact]
    public void Apply_SessionInfo_UpdatesBannerWithEngineData()
    {
        _driver.Apply(new BridgeEvent.SessionInfo("2.1.303", "glm-4.7", "plan", BypassAvailable: false, @"D:\engine\cwd"));

        Assert.Equal("2.1.303", _banner.Version);
        Assert.Equal("glm-4.7", _banner.Model);
        Assert.Equal(@"D:\engine\cwd", _banner.Cwd);
        Assert.Equal(PermissionMode.Plan, _view.Footer.Mode);
        Assert.False(_driver.BypassAvailable);
    }

    [Fact]
    public void Apply_SessionInfoWithoutModel_ShowsNotSetPlaceholder()
    {
        _driver.Apply(new BridgeEvent.SessionInfo("2.1.303", null, "auto", BypassAvailable: true, @"D:\wd"));

        Assert.Equal("当前模型未设置", _banner.Model);
        Assert.Equal(PermissionMode.Auto, _view.Footer.Mode);
        Assert.True(_driver.BypassAvailable);
    }

    [Fact]
    public void Apply_SessionInfoWithVendor_UpdatesBannerVendor()
    {
        _driver.Apply(new BridgeEvent.SessionInfo("2.1.303", "glm-4.7", "auto", BypassAvailable: true, @"D:\wd", Vendor: "zhipu"));

        Assert.Equal("zhipu", _banner.Vendor);
        Assert.Equal("glm-4.7", _banner.Model);
    }

    [Fact]
    public void Apply_SessionInfoWithoutVendor_ClearsBannerVendor()
    {
        _banner.Vendor = "zhipu";

        _driver.Apply(new BridgeEvent.SessionInfo("2.1.303", "glm-4.7", "auto", BypassAvailable: true, @"D:\wd"));

        Assert.Equal(string.Empty, _banner.Vendor);
    }

    [Fact]
    public void Apply_SessionInfoWithUnknownMode_KeepsFooterMode()
    {
        _view.Footer = _view.Footer with { Mode = PermissionMode.Ask };

        _driver.Apply(new BridgeEvent.SessionInfo("2.1.303", null, "warp-drive", BypassAvailable: true, @"D:\wd"));

        Assert.Equal(PermissionMode.Ask, _view.Footer.Mode);
    }

    [Fact]
    public void Apply_PermissionModeChanged_UpdatesFooterMode()
    {
        _driver.Apply(new BridgeEvent.PermissionModeChanged("bypass"));

        Assert.Equal(PermissionMode.Bypass, _view.Footer.Mode);
    }

    [Fact]
    public void Apply_PermissionModeChangedWithUnknownValue_KeepsFooterMode()
    {
        _view.Footer = _view.Footer with { Mode = PermissionMode.Plan };

        _driver.Apply(new BridgeEvent.PermissionModeChanged("teleport"));

        Assert.Equal(PermissionMode.Plan, _view.Footer.Mode);
    }

    [Fact]
    public void Apply_TurnStarted_BusyWithThinkingMode()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());

        Assert.True(_driver.IsBusy);
        Assert.True(_view.Footer.Busy);
        Assert.Equal(SpinnerMode.Thinking, _spinner.Mode);
        Assert.Equal(0, _spinner.ResponseLength);
        Assert.False(_spinner.HasActiveTools);
    }

    [Fact]
    public void Apply_TurnStartedAgain_RefreshesLoadingStart()
    {
        _driver.BeginTurn();
        var firstStart = _spinner.LoadingStartMs;
        Thread.Sleep(10);

        _driver.Apply(new BridgeEvent.TurnStarted());

        Assert.True(_spinner.LoadingStartMs >= firstStart);
    }

    [Fact]
    public void Apply_FullTurnSequence_MessagesBeforeSpinnerAndSpinnerStaysLast()
    {
        BridgeEvent[] turn =
        [
            new BridgeEvent.TurnStarted(),
            new BridgeEvent.ThinkingStarted(),
            new BridgeEvent.ThinkingEnded(3000),
            new BridgeEvent.ToolCall("Read", "src/main.cs"),
            new BridgeEvent.ToolResult("41 lines"),
            new BridgeEvent.TokenDelta(24),
            new BridgeEvent.TokenDelta(24),
            new BridgeEvent.AssistantMessage("回复正文"),
        ];
        foreach (var bridgeEvent in turn)
        {
            _driver.Apply(bridgeEvent);
        }

        Assert.Equal(48, _spinner.ResponseLength);

        _driver.Apply(new BridgeEvent.TurnCompleted());

        Assert.False(_driver.IsBusy);
        Assert.False(_view.Footer.Busy);
        Assert.Equal(0, _spinner.ResponseLength);
        Assert.False(_spinner.HasActiveTools);
        Assert.Same(_spinner, _view.Messages.Items[^1]);
        Assert.IsType<ToolCallItem>(_view.Messages.Items[^4]);
        Assert.IsType<ToolResultItem>(_view.Messages.Items[^3]);
        Assert.IsType<AssistantMessageItem>(_view.Messages.Items[^2]);
    }

    [Fact]
    public void Apply_ToolCall_SwitchesToToolUseModeWithActiveTools()
    {
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.ToolCall("Bash", "ls -la"));

        Assert.Equal(SpinnerMode.ToolUse, _spinner.Mode);
        Assert.True(_spinner.HasActiveTools);
        Assert.IsType<ToolCallItem>(_view.Messages.Items[^2]);
    }

    [Fact]
    public void Apply_TokenDelta_AccumulatesLengthAndSwitchesToResponding()
    {
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));

        _driver.Apply(new BridgeEvent.TokenDelta(24));
        _driver.Apply(new BridgeEvent.TokenDelta(9));

        Assert.Equal(33, _spinner.ResponseLength);
        Assert.Equal(SpinnerMode.Responding, _spinner.Mode);
        Assert.False(_spinner.HasActiveTools);
    }

    [Fact]
    public void Apply_TurnInterrupted_ReturnsToIdleWithoutExtraMessage()
    {
        _driver.BeginTurn();
        var countBefore = _view.Messages.Count;

        _driver.Apply(new BridgeEvent.TurnInterrupted());

        Assert.False(_driver.IsBusy);
        Assert.False(_view.Footer.Busy);
        Assert.Equal(countBefore, _view.Messages.Count);
        Assert.Equal(0, _spinner.ResponseLength);
    }

    [Fact]
    public void Apply_ToolResultWithPatch_AppendsDiffResultItem()
    {
        var hunks = new[]
        {
            new DiffHunk { Header = "@@ -1 +1 @@", Lines = [new DiffLine { Kind = DiffLineKind.Added, Content = "new" }] },
        };

        _driver.Apply(new BridgeEvent.ToolResult("Edited a.cs", hunks));

        var item = Assert.IsType<DiffResultItem>(_view.Messages.Items[^2]);
        Assert.Equal("Edited a.cs", item.Summary);
        Assert.Same(hunks, item.Patch);
        Assert.Same(_spinner, _view.Messages.Items[^1]);
        Assert.False(_spinner.HasActiveTools);
    }

    [Fact]
    public void Apply_ToolResultWithoutPatch_KeepsPlainToolResultItem()
    {
        _driver.Apply(new BridgeEvent.ToolResult("42 lines"));

        Assert.IsType<ToolResultItem>(_view.Messages.Items[^2]);
    }

    [Fact]
    public void Apply_Error_AppendsErrorMessageItemBeforeSpinner()
    {
        _driver.Apply(new BridgeEvent.Error("引擎异常退出"));

        var error = Assert.IsType<ErrorMessageItem>(_view.Messages.Items[^2]);
        Assert.Equal("引擎异常退出", error.Text);
        Assert.Same(_spinner, _view.Messages.Items[^1]);
    }

    [Fact]
    public void Apply_UnknownFutureEvent_DerivedTypeCoverageIsExhaustiveByConstruction()
    {
        // 全部 12 种事件类型逐一应用不抛异常（switch 穷尽由编译器保证）
        BridgeEvent[] all =
        [
            new BridgeEvent.SessionInfo("1.0.0", "m", "auto", true, "C:\\"),
            new BridgeEvent.PermissionModeChanged("plan"),
            new BridgeEvent.TurnStarted(),
            new BridgeEvent.ThinkingStarted(),
            new BridgeEvent.ThinkingEnded(100),
            new BridgeEvent.ToolCall("Read", "x"),
            new BridgeEvent.ToolResult("y"),
            new BridgeEvent.TokenDelta(1),
            new BridgeEvent.AssistantMessage("z"),
            new BridgeEvent.TurnCompleted(),
            new BridgeEvent.TurnInterrupted(),
            new BridgeEvent.Error("e"),
        ];
        foreach (var bridgeEvent in all)
        {
            var record = bridgeEvent;
            _driver.Apply(record);
        }

        Assert.False(_driver.IsBusy);
    }

    [Fact]
    public void EndTurn_ResetsSpinnerState()
    {
        _driver.BeginTurn();
        _spinner.ResponseLength = 999;
        _spinner.HasActiveTools = true;
        _spinner.Message = "残留消息";

        _driver.EndTurn();

        Assert.False(_driver.IsBusy);
        Assert.Equal(0, _spinner.ResponseLength);
        Assert.False(_spinner.HasActiveTools);
        Assert.Equal(string.Empty, _spinner.Message);
    }
}
