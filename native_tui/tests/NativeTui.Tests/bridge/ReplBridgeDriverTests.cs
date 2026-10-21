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
        // 全部事件类型逐一应用不抛异常（switch 穷尽由编译器保证）
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
            new BridgeEvent.BackgroundTask("fork", "fork-1", "后台任务", "running"),
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

    [Fact]
    public void Apply_TokenDeltaWithText_CreatesStreamingItemBeforeSpinner()
    {
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.TokenDelta(5, "hello"));

        var streaming = Assert.IsType<StreamingTextItem>(_view.Messages.Items[^2]);
        Assert.Equal("hello", streaming.Text);
        Assert.Same(_spinner, _view.Messages.Items[^1]);
        Assert.Equal(SpinnerMode.Responding, _spinner.Mode);
        Assert.Equal(5, _spinner.ResponseLength);
    }

    [Fact]
    public void Apply_SubsequentTokenDelta_AppendsToSameStreamingItem()
    {
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.TokenDelta(5, "hello"));
        var streaming = Assert.IsType<StreamingTextItem>(_view.Messages.Items[^2]);

        _driver.Apply(new BridgeEvent.TokenDelta(6, " world"));

        Assert.Same(streaming, _view.Messages.Items[^2]);
        Assert.Equal("hello world", streaming.Text);
        Assert.Equal(11, _spinner.ResponseLength);
    }

    [Fact]
    public void Apply_TokenDeltaWithoutText_KeepsSpinnerCounterOnly()
    {
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.TokenDelta(9));

        Assert.Equal(2, _view.Messages.Count);
        Assert.Equal(9, _spinner.ResponseLength);
    }

    [Fact]
    public void Apply_AssistantMessage_ReplacesStreamingItemInPlace()
    {
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.TokenDelta(5, "hello"));
        var streamingIndex = _view.Messages.Count - 2;

        _driver.Apply(new BridgeEvent.AssistantMessage("hello world", MessageId: 1));

        // 流式项被完整消息替换（原位置），不残留流式项
        var final = Assert.IsType<AssistantMessageItem>(_view.Messages.Items[streamingIndex]);
        Assert.Equal("hello world", final.Text);
        Assert.DoesNotContain(_view.Messages.Items, static item => item is StreamingTextItem);
        Assert.Same(_spinner, _view.Messages.Items[^1]);
    }

    [Fact]
    public void Apply_AssistantMessageWithoutStreaming_AppendsDirectly()
    {
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.AssistantMessage("direct", MessageId: 1));

        var final = Assert.IsType<AssistantMessageItem>(_view.Messages.Items[^2]);
        Assert.Equal("direct", final.Text);
    }

    [Fact]
    public void EndTurn_WithLeftoverStreamingText_FinalizesAsPartialAssistantMessage()
    {
        // 打断场景（M8-1 兜底）：引擎被打断时 OnDone 不发，流式 buffer 残留 →
        // EndTurn 定格为 partial AssistantMessage（基准 [user, partial, interrupted] 顺序）
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.TokenDelta(7, "partial "));

        _driver.EndTurn();

        var final = Assert.IsType<AssistantMessageItem>(_view.Messages.Items[^2]);
        Assert.Equal("partial ", final.Text);
        Assert.DoesNotContain(_view.Messages.Items, static item => item is StreamingTextItem);
    }

    [Fact]
    public void EndTurn_WithEmptyStreamingLeftover_RemovesItem()
    {
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.TokenDelta(3, "abc"));
        _driver.Apply(new BridgeEvent.AssistantMessage("abc", MessageId: 1));

        _driver.EndTurn();

        Assert.DoesNotContain(_view.Messages.Items, static item => item is StreamingTextItem);
    }

    [Fact]
    public void BeginTurn_ClearsLeftoverStreamingItem()
    {
        _driver.Apply(new BridgeEvent.TokenDelta(5, "residual"));

        _driver.BeginTurn();

        Assert.DoesNotContain(_view.Messages.Items, static item => item is StreamingTextItem);
    }

    [Fact]
    public void Apply_ConsecutiveSameToolCalls_CollapseIntoSingleItem()
    {
        // M8-4 聚合路由：首次独立上屏 ToolCallItem，第二次同名到达替换为 CollapsedToolItem（Count=2）
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.ToolCall("Read", "src/a.cs"));
        _driver.Apply(new BridgeEvent.ToolCall("Read", "src/b.cs"));

        var collapsed = Assert.IsType<CollapsedToolItem>(_view.Messages.Items[^2]);
        Assert.Equal("Read", collapsed.ToolName);
        Assert.Equal(2, collapsed.Count);
        Assert.Equal("src/b.cs", collapsed.LastArgs);
        Assert.Same(_spinner, _view.Messages.Items[^1]);
        Assert.Equal(3, _view.Messages.Count);
    }

    [Fact]
    public void Apply_SingleAggregatableCall_StaysPlainToolCallItem()
    {
        // 单次调用不聚合 — 保持 ToolCallItem 类型（现有断言面/视觉形态零变化）
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.ToolCall("Read", "src/a.cs"));

        Assert.IsType<ToolCallItem>(_view.Messages.Items[^2]);
        Assert.DoesNotContain(_view.Messages.Items, static item => item is CollapsedToolItem);
    }

    [Fact]
    public void Apply_ThirdSameCall_RecordsIntoExistingCollapsedItem()
    {
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));
        _driver.Apply(new BridgeEvent.ToolCall("Read", "b.cs"));
        var collapsed = Assert.IsType<CollapsedToolItem>(_view.Messages.Items[^2]);

        _driver.Apply(new BridgeEvent.ToolCall("Read", "c.cs"));

        // 聚合激活后后续同名原地 Record — 不新增消息项
        Assert.Same(collapsed, _view.Messages.Items[^2]);
        Assert.Equal(3, collapsed.Count);
        Assert.Equal("c.cs", collapsed.LastArgs);
        Assert.Equal(3, _view.Messages.Count);
    }

    [Fact]
    public void Apply_ToolResultBetweenSameCalls_DoesNotBreakAggregation()
    {
        // 聚合仅统计 ToolCallItem — 工具结果照旧独立上屏且不打断链
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));
        _driver.Apply(new BridgeEvent.ToolResult("41 lines"));

        _driver.Apply(new BridgeEvent.ToolCall("Read", "b.cs"));

        var collapsed = Assert.IsType<CollapsedToolItem>(_view.Messages.Items[^3]);
        Assert.Equal(2, collapsed.Count);
        Assert.Equal(1, _view.Messages.Items.Count(static item => item is ToolResultItem));
    }

    [Fact]
    public void Apply_DifferentAggregatableTool_BreaksChainForIndependentItems()
    {
        // 不同名工具到达即结束当前聚合 — 三次调用互不聚合，各自独立上屏
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));
        _driver.Apply(new BridgeEvent.ToolCall("Grep", "pattern"));
        _driver.Apply(new BridgeEvent.ToolCall("Read", "b.cs"));

        Assert.Equal(3, _view.Messages.Items.Count(static item => item is ToolCallItem));
        Assert.DoesNotContain(_view.Messages.Items, static item => item is CollapsedToolItem);
    }

    [Fact]
    public void Apply_NonAggregatableTools_NeverAggregate()
    {
        // Edit/Bash/Write 等非聚合类工具照旧独立上屏
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "ls"));
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "pwd"));

        Assert.Equal(2, _view.Messages.Items.Count(static item => item is ToolCallItem));
        Assert.DoesNotContain(_view.Messages.Items, static item => item is CollapsedToolItem);
    }

    [Fact]
    public void Apply_AssistantMessage_BreaksAggregationChain()
    {
        // 文本消息到达断链 — 之后的同名调用重新独立上屏（不被旧聚合吸收）
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));
        _driver.Apply(new BridgeEvent.ToolCall("Read", "b.cs"));
        _driver.Apply(new BridgeEvent.AssistantMessage("正文"));

        _driver.Apply(new BridgeEvent.ToolCall("Read", "c.cs"));

        var collapsedItems = _view.Messages.Items.OfType<CollapsedToolItem>().ToList();
        Assert.Single(collapsedItems);
        Assert.Equal(2, collapsedItems[0].Count);
        Assert.Equal(1, _view.Messages.Items.Count(static item => item is ToolCallItem));
    }

    [Fact]
    public void EndTurn_BreaksAggregationChain()
    {
        // 回合边界断链 — 新回合同名调用重新独立上屏
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));
        _driver.Apply(new BridgeEvent.ToolCall("Read", "b.cs"));
        _driver.EndTurn();

        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.ToolCall("Read", "c.cs"));

        Assert.Equal(1, _view.Messages.Items.Count(static item => item is CollapsedToolItem));
        Assert.Equal(1, _view.Messages.Items.Count(static item => item is ToolCallItem));
    }

    [Fact]
    public void Apply_GrepConsecutiveCalls_AggregatesWithSearchVerb()
    {
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.ToolCall("Grep", "TODO"));
        _driver.Apply(new BridgeEvent.ToolCall("Grep", "FIXME"));

        var collapsed = Assert.IsType<CollapsedToolItem>(_view.Messages.Items[^2]);
        Assert.Equal("搜索 2 次", collapsed.SummaryText);
        Assert.Equal("FIXME", collapsed.LastArgs);
    }

    /// <summary>M8-7 上下文余量 — percentLeft 阈值路由（TokenWarning.tsx:166-177 汉化）：
    /// ≤10% 告急（Critical + error 文案）、≤25% 不足（Warning + warning 文案）、&gt;25% 清空（防噪音）。</summary>
    [Fact]
    public void Apply_ContextUsage_CriticalThreshold_SetsCriticalWarning()
    {
        _driver.Apply(new BridgeEvent.ContextUsage(180_000, 200_000, PercentLeft: 8));

        Assert.Equal("上下文告急 (8%) · 运行 /compact", _view.Footer.ContextWarning);
        Assert.Equal(ContextAlertLevel.Critical, _view.Footer.ContextWarningLevel);
    }

    [Fact]
    public void Apply_ContextUsage_AtBoundary10_IsCritical()
    {
        _driver.Apply(new BridgeEvent.ContextUsage(180_000, 200_000, PercentLeft: 10));

        Assert.Equal("上下文告急 (10%) · 运行 /compact", _view.Footer.ContextWarning);
        Assert.Equal(ContextAlertLevel.Critical, _view.Footer.ContextWarningLevel);
    }

    [Fact]
    public void Apply_ContextUsage_WarningThreshold_SetsWarning()
    {
        _driver.Apply(new BridgeEvent.ContextUsage(150_000, 200_000, PercentLeft: 20));

        Assert.Equal("上下文不足 (20%) · 运行 /compact", _view.Footer.ContextWarning);
        Assert.Equal(ContextAlertLevel.Warning, _view.Footer.ContextWarningLevel);
    }

    [Fact]
    public void Apply_ContextUsage_AtBoundary25_IsWarning()
    {
        _driver.Apply(new BridgeEvent.ContextUsage(150_000, 200_000, PercentLeft: 25));

        Assert.Equal("上下文不足 (25%) · 运行 /compact", _view.Footer.ContextWarning);
        Assert.Equal(ContextAlertLevel.Warning, _view.Footer.ContextWarningLevel);
    }

    [Fact]
    public void Apply_ContextUsage_AboveThreshold_ClearsWarning()
    {
        _driver.Apply(new BridgeEvent.ContextUsage(180_000, 200_000, PercentLeft: 8));

        _driver.Apply(new BridgeEvent.ContextUsage(90_000, 200_000, PercentLeft: 55));

        Assert.Null(_view.Footer.ContextWarning);
        Assert.Equal(ContextAlertLevel.None, _view.Footer.ContextWarningLevel);
    }

    [Fact]
    public void Apply_ContextUsage_AtBoundary26_IsCleared()
    {
        _driver.Apply(new BridgeEvent.ContextUsage(90_000, 200_000, PercentLeft: 26));

        Assert.Null(_view.Footer.ContextWarning);
        Assert.Equal(ContextAlertLevel.None, _view.Footer.ContextWarningLevel);
    }

    /// <summary>告警独立槽位 — 不触碰 Notification（临时通知语义由 ReplShell 管理，互不覆盖）。</summary>
    [Fact]
    public void Apply_ContextUsage_DoesNotTouchNotification()
    {
        _view.Footer = _view.Footer with { Notification = "◐ 中等 · /effort" };

        _driver.Apply(new BridgeEvent.ContextUsage(180_000, 200_000, PercentLeft: 8));

        Assert.Equal("◐ 中等 · /effort", _view.Footer.Notification);
        Assert.Equal("上下文告急 (8%) · 运行 /compact", _view.Footer.ContextWarning);
    }

    /// <summary>M8-8 后台任务常驻面板 — BackgroundTask 事件路由 view.BackgroundTasks 按 (kind,taskId) upsert：
    /// 新任务插入面板条目。</summary>
    [Fact]
    public void Apply_BackgroundTask_InsertsPanelEntry()
    {
        _driver.Apply(new BridgeEvent.BackgroundTask("fork", "fork-123", "检查README", "running"));

        var task = Assert.Single(_view.BackgroundTasks.Tasks);
        Assert.Equal("fork", task.Kind);
        Assert.Equal("fork-123", task.TaskId);
        Assert.Equal("检查README", task.Description);
        Assert.Equal("running", task.State);
    }

    [Fact]
    public void Apply_BackgroundTask_SameKeyUpsertsStateAndDescription()
    {
        _driver.Apply(new BridgeEvent.BackgroundTask("fork", "fork-123", "检查README", "running"));

        _driver.Apply(new BridgeEvent.BackgroundTask("fork", "fork-123", "检查README与文档", "completed"));

        var task = Assert.Single(_view.BackgroundTasks.Tasks);
        Assert.Equal("completed", task.State);
        Assert.Equal("检查README与文档", task.Description);
    }

    [Fact]
    public void Apply_BackgroundTask_TerminalEntrySurvivesTurnBoundary()
    {
        _driver.Apply(new BridgeEvent.BackgroundTask("fork", "fork-1", "检查", "completed"));
        _driver.BeginTurn();

        _driver.EndTurn();

        // 常驻语义 — 回合边界不清理面板条目（区别于子代理组的定格脱离）
        var task = Assert.Single(_view.BackgroundTasks.Tasks);
        Assert.Equal("completed", task.State);
    }

    /// <summary>协议契约（与引擎侧约定写死）— background_task JSON 判别符反序列化 + Driver 路由面板。</summary>
    [Fact]
    public void Apply_BackgroundTaskFromJson_ParsesAndRoutesToPanel()
    {
        var reader = new StringReader(
            "{\"type\":\"background_task\",\"kind\":\"fork\",\"taskId\":\"fork-123\",\"description\":\"检查README\",\"state\":\"running\"}\n");
        var result = BridgeProtocol.TryReadEvent(reader, out var parsed, out var diagnostic);

        Assert.Equal(BridgeReadResult.Event, result);
        Assert.Null(diagnostic);
        _driver.Apply(parsed!);
        var task = Assert.Single(_view.BackgroundTasks.Tasks);
        Assert.Equal("fork", task.Kind);
        Assert.Equal("fork-123", task.TaskId);
        Assert.Equal("检查README", task.Description);
        Assert.Equal("running", task.State);
    }
}
