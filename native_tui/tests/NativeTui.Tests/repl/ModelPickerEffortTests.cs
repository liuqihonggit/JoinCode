namespace NativeTui.Tests.Repl;

/// <summary>
/// 模型选择器 effort 测试（TASK066 板块 D 基建 D5 — M7-6 登记遗留）— 对齐基准 ModelPicker.tsx：
/// 环绕循环 cycleEffortLevel（:431-442 — 四值/三值环 + 越界 clamp high 索引）、
/// handleCycleEffort no-op 条件（:187-194 — 未启用/焦点不支持）、
/// handleFocus 默认档跟随（:173-178 — 未切换且初始未设置）、
/// displayEffort max 降级（:170）+ selectedEffort 确认传出（:244 — 仅切换过且焦点支持）、
/// effort 区渲染（:328 — 档位行 "◉ Max effort (default) ← → to adjust" / not supported 行）、
/// ReplShell 模态 left/right 键位（:206-207 decreaseEffort/increaseEffort）+
/// Enter 确认 effort 回调 + footer 通知投影（"◐ 中等 · /effort" 唯一数据源）+ Esc 不应用。
/// </summary>
public sealed class ModelPickerEffortTests
{
    /// <summary>effort 测试模型清单（3 项）：
    /// opus 当前 + 支持 max + 默认 high（四值环）；sonnet 支持 effort 不支持 max + 默认 medium（三值环）；
    /// haiku 不支持 effort（能力字段缺省 false — vendor/降级项同形态）。</summary>
    private static readonly ModelPickerItem[] EffortItems =
    [
        new("opus", "Opus", "最强推理", IsCurrent: true, SupportsEffort: true, SupportsMaxEffort: true, DefaultEffort: PickerEffortLevel.High),
        new("sonnet", "Sonnet", "均衡", IsCurrent: false, SupportsEffort: true, SupportsMaxEffort: false, DefaultEffort: PickerEffortLevel.Medium),
        new("haiku", "Haiku", "快速", IsCurrent: false),
    ];

    /// <summary>构造挂起选择器（焦点定位当前模型 opus index=0）。</summary>
    private static ModelPickerCoordinator BeginCoordinator(PickerEffortLevel? initialEffort) =>
        BeginCoordinator(EffortItems, initialEffort);

    /// <summary>构造挂起选择器（指定清单 — 不支持行测试用单项目变体）。</summary>
    private static ModelPickerCoordinator BeginCoordinator(IReadOnlyList<ModelPickerItem> items, PickerEffortLevel? initialEffort)
    {
        var coordinator = new ModelPickerCoordinator();
        coordinator.Begin(items, new PickerEffortConfig(initialEffort));
        return coordinator;
    }

    // ---- Coordinator 循环语义（基准 cycleEffortLevel ModelPicker.tsx:431-442）----

    /// <summary>四值环双向环绕 — low→medium→high→max→low / low←max 直达。</summary>
    [Fact]
    public void Cycle_FullCycleWrapsBothDirections()
    {
        var coordinator = BeginCoordinator(PickerEffortLevel.Low);

        Assert.True(coordinator.CycleEffort(forward: true));
        Assert.Equal(PickerEffortLevel.Medium, coordinator.Effort);
        coordinator.CycleEffort(forward: true);
        Assert.Equal(PickerEffortLevel.High, coordinator.Effort);
        coordinator.CycleEffort(forward: true);
        Assert.Equal(PickerEffortLevel.Max, coordinator.Effort);
        coordinator.CycleEffort(forward: true);
        Assert.Equal(PickerEffortLevel.Low, coordinator.Effort);

        coordinator.CycleEffort(forward: false);
        Assert.Equal(PickerEffortLevel.Max, coordinator.Effort);
        coordinator.CycleEffort(forward: false);
        Assert.Equal(PickerEffortLevel.High, coordinator.Effort);
    }

    /// <summary>三值环（不支持 max）— medium→high→low 环绕不含 max。</summary>
    [Fact]
    public void Cycle_StandardCycleOmitsMax()
    {
        var coordinator = BeginCoordinator(PickerEffortLevel.Medium);
        coordinator.MoveFocus(1);

        Assert.Equal("sonnet", coordinator.Focused?.Id);
        coordinator.CycleEffort(forward: true);
        Assert.Equal(PickerEffortLevel.High, coordinator.Effort);
        coordinator.CycleEffort(forward: true);
        Assert.Equal(PickerEffortLevel.Low, coordinator.Effort);
        coordinator.CycleEffort(forward: true);
        Assert.Equal(PickerEffortLevel.Medium, coordinator.Effort);
    }

    /// <summary>越界档 clamp — max 在三值环不存在，从 high 索引起算（基准 :433-36 注释）。</summary>
    [Fact]
    public void Cycle_MaxClampsToHighInStandardCycle()
    {
        var coordinator = BeginCoordinator(PickerEffortLevel.Max);
        coordinator.MoveFocus(1);

        coordinator.CycleEffort(forward: true);

        Assert.Equal(PickerEffortLevel.Low, coordinator.Effort);
        coordinator.CycleEffort(forward: false);
        Assert.Equal(PickerEffortLevel.High, coordinator.Effort);
    }

    /// <summary>未启用（无配置 — vendor 形态）— 循环 no-op。</summary>
    [Fact]
    public void Cycle_DisabledWhenNoConfig()
    {
        var coordinator = new ModelPickerCoordinator();
        coordinator.Begin(EffortItems);

        Assert.False(coordinator.EffortEnabled);
        Assert.False(coordinator.CycleEffort(forward: true));
        Assert.Null(coordinator.Effort);
    }

    /// <summary>焦点项不支持 effort — 循环 no-op（基准 handleCycleEffort :189-191）。</summary>
    [Fact]
    public void Cycle_NoOpWhenFocusedUnsupported()
    {
        var coordinator = BeginCoordinator(PickerEffortLevel.Medium);
        coordinator.MoveFocus(2);

        Assert.Equal("haiku", coordinator.Focused?.Id);
        Assert.False(coordinator.CycleEffort(forward: true));
        Assert.False(coordinator.HasToggledEffort);
    }

    // ---- 展示/传出投影（基准 displayEffort :170 + selectedEffort :244）----

    /// <summary>展示降级 — 内部 max 且焦点不支持 max → 展示 high（内部态保留）。</summary>
    [Fact]
    public void DisplayEffort_MaxDowngradesWithoutSupport()
    {
        var coordinator = BeginCoordinator(PickerEffortLevel.Max);
        coordinator.MoveFocus(1);

        Assert.Equal(PickerEffortLevel.Max, coordinator.Effort);
        Assert.Equal(PickerEffortLevel.High, coordinator.DisplayEffort);
    }

    /// <summary>展示兜底 — 未设置档位 → 焦点项默认档。</summary>
    [Fact]
    public void DisplayEffort_FallsBackToFocusedDefault()
    {
        var coordinator = BeginCoordinator(null);

        Assert.Null(coordinator.Effort);
        Assert.Equal(PickerEffortLevel.High, coordinator.DisplayEffort);
    }

    /// <summary>焦点移动默认档跟随 — 未切换且初始未设置（基准 handleFocus :173-178）。</summary>
    [Fact]
    public void MoveFocus_DefaultFollowsFocusWhenUntoggled()
    {
        var coordinator = BeginCoordinator(null);

        coordinator.MoveFocus(1);
        Assert.Equal(PickerEffortLevel.Medium, coordinator.Effort);
        coordinator.MoveFocus(-1);
        Assert.Equal(PickerEffortLevel.High, coordinator.Effort);
    }

    /// <summary>切换后档位定住 — 焦点移动不再跟随默认（基准 hasToggledEffort 门槛）。</summary>
    [Fact]
    public void MoveFocus_ToggledEffortStays()
    {
        var coordinator = BeginCoordinator(null);
        coordinator.CycleEffort(forward: true);
        var toggled = coordinator.Effort;

        coordinator.MoveFocus(1);

        Assert.Equal(toggled, coordinator.Effort);
        Assert.True(coordinator.HasToggledEffort);
    }

    /// <summary>确认传出 — 未切换 null / 切换过值 / 焦点不支持 null（基准 :244）。</summary>
    [Fact]
    public void SelectedEffort_RequiresToggleAndSupport()
    {
        var untoggled = BeginCoordinator(PickerEffortLevel.Low);
        Assert.Null(untoggled.SelectedEffort);

        var toggled = BeginCoordinator(PickerEffortLevel.Low);
        toggled.CycleEffort(forward: true);
        Assert.Equal(PickerEffortLevel.Medium, toggled.SelectedEffort);

        var unsupported = BeginCoordinator(PickerEffortLevel.Low);
        unsupported.CycleEffort(forward: true);
        unsupported.MoveFocus(2);
        Assert.Null(unsupported.SelectedEffort);
    }

    /// <summary>Complete — effort 态清空（幂等复位）。</summary>
    [Fact]
    public void Complete_ClearsEffortState()
    {
        var coordinator = BeginCoordinator(PickerEffortLevel.Low);
        coordinator.CycleEffort(forward: true);

        coordinator.Complete();

        Assert.False(coordinator.EffortEnabled);
        Assert.Null(coordinator.Effort);
        Assert.False(coordinator.HasToggledEffort);
        Assert.Null(coordinator.SelectedEffort);
    }

    // ---- Dialog 渲染（基准 effort 区 ModelPicker.tsx:328）----

    /// <summary>渲染档位行 — "● High effort (default) ← → to adjust"（默认档尾缀 + 调节提示）。</summary>
    [Fact]
    public void Render_EffortRowWithDefaultSuffix()
    {
        using var screen = new Screen(cols: 60, rows: 20);
        var coordinator = BeginCoordinator(null);
        new ModelPickerDialog(coordinator).Render(screen, new Rect(0, 0, 60, 20), Theme.Dark);

        // 3 项带描述：边框@1 标题@2 副标题@3 空行@4 项@5-10 effort@11
        Assert.StartsWith(" ● High effort (default) ← → to adjust", RowText.Of(screen, 11, 0, 60));
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[11, 1].StyleId);
    }

    /// <summary>渲染档位行 — 切换后无默认尾缀。</summary>
    [Fact]
    public void Render_EffortRowNoDefaultAfterToggle()
    {
        using var screen = new Screen(cols: 60, rows: 20);
        var coordinator = BeginCoordinator(null);
        coordinator.CycleEffort(forward: true);
        new ModelPickerDialog(coordinator).Render(screen, new Rect(0, 0, 60, 20), Theme.Dark);

        Assert.StartsWith(" ◉ Max effort ← → to adjust", RowText.Of(screen, 11, 0, 60));
    }

    /// <summary>渲染不支持行 — "○ Effort not supported for Haiku"。</summary>
    [Fact]
    public void Render_EffortUnsupportedRow()
    {
        using var screen = new Screen(cols: 60, rows: 20);
        var coordinator = BeginCoordinator(null);
        coordinator.MoveFocus(2);
        new ModelPickerDialog(coordinator).Render(screen, new Rect(0, 0, 60, 20), Theme.Dark);

        Assert.StartsWith(" ○ Effort not supported for Haiku", RowText.Of(screen, 11, 0, 60));
    }

    /// <summary>未启用 — 无 effort 行（vendor 形态，快捷键行位置不变）。</summary>
    [Fact]
    public void Render_NoEffortRowWhenDisabled()
    {
        using var screen = new Screen(cols: 60, rows: 20);
        var coordinator = new ModelPickerCoordinator();
        coordinator.Begin(EffortItems);
        var dialog = new ModelPickerDialog(coordinator);
        dialog.Render(screen, new Rect(0, 0, 60, 20), Theme.Dark);

        Assert.DoesNotContain("effort", RowText.Of(screen, 11, 0, 60));
        Assert.StartsWith(" Enter confirm · Esc exit", RowText.Of(screen, dialog.MeasureHeight(60) - 1, 0, 60));
    }

    /// <summary>MeasureHeight — effort 行 +1（启用 14 / 未启用 13 — 3 项带描述）。</summary>
    [Fact]
    public void MeasureHeight_EffortRowAddsOne()
    {
        var enabled = BeginCoordinator(null);
        var disabled = new ModelPickerCoordinator();
        disabled.Begin(EffortItems);

        Assert.Equal(14, new ModelPickerDialog(enabled).MeasureHeight(60));
        Assert.Equal(13, new ModelPickerDialog(disabled).MeasureHeight(60));
    }

    /// <summary>footer 通知文本 — 唯一数据源（对齐种子字面量 "◐ 中等 · /effort"）。</summary>
    [Fact]
    public void ToFooterNotificationText_MatchesSeedForm()
    {
        Assert.Equal("◐ 中等 · /effort", PickerEffortLevel.Medium.ToFooterNotificationText());
        Assert.Equal("○ 低 · /effort", PickerEffortLevel.Low.ToFooterNotificationText());
        Assert.Equal("◉ 最高 · /effort", PickerEffortLevel.Max.ToFooterNotificationText());
    }

    // ---- ReplShell 模态接线（left/right 循环 + Enter/Esc 回调）----

    /// <summary>构造测试 shell（对齐 ModelPickerTests.CreateShell 模式）。</summary>
    private static (ReplShell Shell, FakeEffortEngine Engine) CreateShell()
    {
        var engine = new FakeEffortEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter());
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>left/right 键 — effort 循环不进输入缓冲（基准 modelPicker:decrease/increaseEffort）。</summary>
    [Fact]
    public void Shell_LeftRightCycleEffort()
    {
        var (shell, _) = CreateShell();
        shell.BeginModelPicker(EffortItems, effortConfig: new PickerEffortConfig(PickerEffortLevel.Low));

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        Assert.Equal(PickerEffortLevel.Medium, shell.ModelPicker.Effort);
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        Assert.Equal(PickerEffortLevel.Low, shell.ModelPicker.Effort);

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.True(shell.ModelPicker.IsPending);
    }

    /// <summary>Enter — effort 回调携带切换档位 + footer 通知入队（种子到期后显示档位文本）。</summary>
    [Fact]
    public void Shell_EnterAppliesEffortAndEnqueuesFooterNotification()
    {
        var (shell, _) = CreateShell();
        // 初始 High → Right 环绕到 Max（"◉ 最高 · /effort" 与种子 "◐ 中等 · /effort" key 不同 —
        // 同 key 会 fold 合并不入队，档位文本必须异于种子才能断言入队语义）
        var applied = new[] { (PickerEffortLevel?)PickerEffortLevel.High };
        shell.BeginModelPicker(EffortItems, effortConfig: new PickerEffortConfig(PickerEffortLevel.High),
            onEffortApplied: effort => applied[0] = effort);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal(PickerEffortLevel.Max, applied[0]);
        Assert.False(shell.ModelPicker.IsPending);
        // 种子通知占 current（Enqueue 立即出队语义）→ effort 通知入队等待；Tick 推进 8s+ 种子到期 → 档位文本上屏
        var notifications = shell.View.Footer!.Notifications!;
        Assert.Equal(1, notifications.QueuedCount);
        notifications.Tick(9000);
        Assert.Equal(PickerEffortLevel.Max.ToFooterNotificationText(), notifications.Current);
    }

    /// <summary>Enter 未切换 — effort 回调触发但值为 null（确认了但无档位变化 — 不入通知）。</summary>
    [Fact]
    public void Shell_EnterWithoutToggle_EffortCallbackNull()
    {
        var (shell, _) = CreateShell();
        var applied = new[] { (PickerEffortLevel?)PickerEffortLevel.Low };
        shell.BeginModelPicker(EffortItems, effortConfig: new PickerEffortConfig(PickerEffortLevel.Low),
            onEffortApplied: effort => applied[0] = effort);

        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Null(applied[0]);
        Assert.Equal(0, shell.View.Footer!.Notifications!.QueuedCount);
    }

    /// <summary>Esc — 取消不触发 effort 回调（基准 onCancel 无 effort）。</summary>
    [Fact]
    public void Shell_EscapeSkipsEffortCallback()
    {
        var (shell, _) = CreateShell();
        var applied = new[] { (PickerEffortLevel?)PickerEffortLevel.Low };
        shell.BeginModelPicker(EffortItems, effortConfig: new PickerEffortConfig(PickerEffortLevel.Low),
            onEffortApplied: effort => applied[0] = effort);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false));
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.Equal(PickerEffortLevel.Low, applied[0]);
        Assert.False(shell.ModelPicker.IsPending);
    }

    // ---- CommandPickerEngine 装饰器 effort 注入 ----

    /// <summary>effort 注入 — /model 拦截打开的选择器启用 effort 区；vendor（无注入）不启用。</summary>
    [Fact]
    public void Engine_EffortInjectionTogglesEnabled()
    {
        var inner = new FakeEffortEngine();
        var (shell, _) = CreateShell();
        var modelEngine = new CommandPickerEngine(inner, "/model", "模型", () => EffortItems, _ => { },
            effortConfigProvider: () => new PickerEffortConfig(PickerEffortLevel.Low),
            applyEffort: _ => { });
        var vendorEngine = new CommandPickerEngine(modelEngine, "/vendor", "供应商", () => EffortItems, _ => { });
        vendorEngine.Attach(shell);

        modelEngine.Submit("/model");
        Assert.True(shell.ModelPicker.EffortEnabled);
        Assert.Equal(PickerEffortLevel.Low, shell.ModelPicker.Effort);
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        vendorEngine.Submit("/vendor");
        Assert.False(shell.ModelPicker.EffortEnabled);
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
    }

    /// <summary>记录型假引擎（ModelPickerTests.FakeModelEngine 同构）。</summary>
    private sealed class FakeEffortEngine : IReplEngine
    {
        /// <summary>收到的提交文本。</summary>
        public List<string> Submits { get; } = [];

        /// <summary>收到的打断次数。</summary>
        public int Interrupts;

        /// <summary>收到的权限模式。</summary>
        public List<string> PermissionModes { get; } = [];

        /// <summary>待排空事件队列。</summary>
        public Queue<BridgeEvent> Events { get; } = new();

        /// <summary>记录提交。</summary>
        public void Submit(string text) => Submits.Add(text);

        /// <summary>记录打断。</summary>
        public void Interrupt() => Interrupts++;

        /// <summary>记录权限模式。</summary>
        public void SetPermissionMode(string mode) => PermissionModes.Add(mode);

        /// <summary>排空事件队列。</summary>
        public IReadOnlyList<BridgeEvent> DrainEvents()
        {
            var drained = Events.ToList();
            Events.Clear();
            return drained;
        }

        /// <summary>存活标记。</summary>
        public bool IsAlive => true;
    }
}
