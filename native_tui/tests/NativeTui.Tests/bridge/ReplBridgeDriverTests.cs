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
            new BridgeEvent.ToolCall("Bash", "ls -la"),
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

    /// <summary>压缩完成路由（TASK070 C6'/DSG046）— 用量快照作废（Reset 后 Warning 归 None，
    /// footer 低水位警告消退）+ 通知槽"上下文已压缩"提示。</summary>
    [Fact]
    public void Apply_CompactCompleted_ResetsTokenUsageTrackerAndNotifies()
    {
        var tracker = new TokenUsageTracker();
        tracker.RecordUsage(inputTokens: 5_000, outputTokens: 1_000, contextTokens: 197_000, contextLimitTokens: 200_000);
        Assert.Equal(TokenWarningLevel.Low, tracker.Warning.Level);
        var notifications = new ReplNotifications();
        _view.Footer = _view.Footer with { Notifications = notifications };
        var driver = new ReplBridgeDriver(_view, _spinner, _banner, tokenUsageTracker: tracker);

        driver.Apply(new BridgeEvent.CompactCompleted(12, 197_000, 900, IsAuto: true));

        Assert.Equal(TokenWarningLevel.None, tracker.Warning.Level);
        Assert.Null(tracker.ContextTokens);
        Assert.Null(tracker.ContextLimitTokens);
        Assert.Equal("上下文已压缩（12 条摘要，197,000 → 900）", notifications.Current);
    }

    /// <summary>压缩完成路由 — tracker/通知队列未注入时静默容忍（纯视图断言场景零异常）。</summary>
    [Fact]
    public void Apply_CompactCompleted_WithoutTracker_NotifiesOnly()
    {
        _view.Footer = _view.Footer with { Notifications = new ReplNotifications() };

        _driver.Apply(new BridgeEvent.CompactCompleted(3, 10_000, 200, IsAuto: false));

        Assert.Equal("上下文已压缩（3 条摘要，10,000 → 200）", _view.Footer.Notifications!.Current);
    }

    /// <summary>重试路由（TASK070 E2/E3/DSG046）— spinner 行显示倒计时式提示（非 ErrorMessageItem）。</summary>
    [Fact]
    public void Apply_Retry_ShowsSpinnerRetryingHint()
    {
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.Retry("连接超时", 1, 3, 1000));

        Assert.Equal("Retrying in 1s (1/3)", _spinner.Message);
    }

    /// <summary>重试路由 — delayMs 亚秒向上取整（350ms → 1s，不显示 0s 误导）。</summary>
    [Fact]
    public void Apply_Retry_SubSecondDelay_CeilToOneSecond()
    {
        _driver.BeginTurn();

        _driver.Apply(new BridgeEvent.Retry("限流", 2, 5, 350));

        Assert.Equal("Retrying in 1s (2/5)", _spinner.Message);
    }

    /// <summary>重试路由 — 回合结束后 spinner Message 复位（EndTurn 清空不残留）。</summary>
    [Fact]
    public void Apply_Retry_ThenTurnCompleted_MessageCleared()
    {
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.Retry("连接超时", 1, 3, 1000));

        _driver.Apply(new BridgeEvent.TurnCompleted());

        Assert.Equal(string.Empty, _spinner.Message);
    }

    [Fact]
    public void Apply_ToolResultWithPatch_AppendsToolResultItemUnifiedContract()
    {
        var hunks = new[]
        {
            new DiffHunk { Header = "@@ -1 +1 @@", Lines = [new DiffLine { Kind = DiffLineKind.Added, Content = "new" }] },
        };

        _driver.Apply(new BridgeEvent.ToolResult("Edited a.cs", hunks));

        var item = Assert.IsType<ToolResultItem>(_view.Messages.Items[^2]);
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
    public void Apply_NonAggregatableTools_NeverAggregate()
    {
        // Edit/Bash/Write 等非聚合类工具照旧独立上屏
        _driver.BeginTurn();
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "ls"));
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "pwd"));

        Assert.Equal(2, _view.Messages.Items.Count(static item => item is ToolCallItem));
        Assert.DoesNotContain(_view.Messages.Items, static item => item is CollapsedToolGroupItem);
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

    /// <summary>TASK065 P3 工具流式进度 — 首个 tool_progress 惰性建流式项落位 ToolCallItem 之后
    ///（spinner 前），后续快照原地 Update（同一项不重复落位）。</summary>
    [Fact]
    public void Apply_ToolProgress_CreatesStreamingItemAfterToolCallAndUpdatesInPlace()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "ls -la"));
        _driver.Apply(new BridgeEvent.ToolProgress("Bash", "bash_progress", "line1"));
        var streamingItem = Assert.IsType<ToolStreamingItem>(_view.Messages.Items[^2]);

        _driver.Apply(new BridgeEvent.ToolProgress("Bash", "bash_progress", "line1\nline2"));

        Assert.Same(streamingItem, _view.Messages.Items[^2]);
        Assert.Equal(["line1", "line2"], streamingItem.DisplayLines);
        // spinner 恒为滚动区末项 — 流式项落位其前
        Assert.Same(_spinner, _view.Messages.Items[^1]);
    }

    /// <summary>TASK065 P3 — tool_result 到达时流式项原位替换为 ToolResultItem
    ///（对齐 StreamingTextItem 替换先例 — 输出尾巴位置落终态项，视觉无跳变）。</summary>
    [Fact]
    public void Apply_ToolResult_ReplacesStreamingItemInPlaceWithToolResultItem()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "ls -la"));
        _driver.Apply(new BridgeEvent.ToolProgress("Bash", "bash_progress", "line1"));
        var streamingIndex = _view.Messages.IndexOf(_view.Messages.Items.OfType<ToolStreamingItem>().Single());

        _driver.Apply(new BridgeEvent.ToolResult("3 files"));

        var resultItem = Assert.IsType<ToolResultItem>(_view.Messages.Items[streamingIndex]);
        Assert.Equal("3 files", resultItem.Summary);
        Assert.DoesNotContain(_view.Messages.Items, static item => item is ToolStreamingItem);
    }

    /// <summary>TASK065 P3 — 回合外（EndTurn 后）迟到 progress 丢弃（IsBusy 防御 —
    /// 引擎漏发收尾事件时不跨回合泄漏新流式项）。</summary>
    [Fact]
    public void Apply_ToolProgress_AfterTurnCompleted_IsDropped()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "ls"));
        _driver.Apply(new BridgeEvent.ToolProgress("Bash", "bash_progress", "line1"));
        _driver.Apply(new BridgeEvent.TurnCompleted());

        _driver.Apply(new BridgeEvent.ToolProgress("Bash", "bash_progress", "late"));

        Assert.DoesNotContain(_view.Messages.Items, static i => i is ToolStreamingItem item && item.DisplayLines.Contains("late"));
    }

    /// <summary>TASK065 P3 — 打断场景（无 tool_result）流式项残留定格：EndTurn 清引用不删项
    ///（已见输出不丢 — 用户看到中断前输出）。</summary>
    [Fact]
    public void Apply_TurnInterrupted_StreamingItemStaysAsSeenOutput()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "ls"));
        _driver.Apply(new BridgeEvent.ToolProgress("Bash", "bash_progress", "partial output"));

        _driver.Apply(new BridgeEvent.TurnInterrupted());

        var streamingItem = Assert.Single(_view.Messages.Items.OfType<ToolStreamingItem>());
        Assert.Equal(["partial output"], streamingItem.DisplayLines);
    }

    /// <summary>TASK065 P3 — 新 tool_call 到达旧流式项定格（引用清空不删项 —
    /// 引擎漏发上一工具 tool_result 时不串新工具输出到旧尾巴）。</summary>
    [Fact]
    public void Apply_NewToolCall_FinalizesPreviousStreamingItem()
    {
        _driver.Apply(new BridgeEvent.TurnStarted());
        _driver.Apply(new BridgeEvent.ToolCall("Bash", "first"));
        _driver.Apply(new BridgeEvent.ToolProgress("Bash", "bash_progress", "old output"));

        _driver.Apply(new BridgeEvent.ToolCall("Read", "a.cs"));
        _driver.Apply(new BridgeEvent.ToolProgress("Read", "query_update", "searching"));

        var streamingItems = _view.Messages.Items.OfType<ToolStreamingItem>().ToList();
        Assert.Equal(2, streamingItems.Count);
        Assert.Equal(["old output"], streamingItems[0].DisplayLines);
        Assert.Equal(["searching"], streamingItems[1].DisplayLines);
    }
}
