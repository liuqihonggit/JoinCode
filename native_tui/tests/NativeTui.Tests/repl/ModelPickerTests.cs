namespace NativeTui.Tests.Repl;

/// <summary>
/// 模型选择器测试（M7-6，全部自研）— 对齐基准 ModelPicker 形态：
/// ModelPickerCoordinator 状态机（Begin 焦点定位当前模型 + 上下 clamp + 滚动窗口 min(10,n) 跟随 +
/// Complete 幂等）+ ModelPickerDialog 渲染（permission 色顶边框 + bold 标题 "Select model" + dim 副标题 +
/// 列表行 ❯ 焦点 suggestion + 序号 dim + ✓ 当前 success + 描述缩进 dim + "and N more…" 溢出 +
/// "Enter confirm · Esc exit" 快捷键行）+ ReplShell 模态改道（挂起时 ↑↓/Enter/Esc 路由选择器；
/// 权限对话框优先级更高）+ ModelPickerEngine 装饰器（Submit "/model" 拦截打开选择器、其余透传、
/// 确认回调 applyModel + PostInfo 反馈）。
/// </summary>
public sealed class ModelPickerTests
{
    /// <summary>测试模型清单（3 项，第 2 项为当前模型）。</summary>
    private static readonly ModelPickerItem[] SampleItems =
    [
        new("gpt-x", "GPT X", "最强推理能力", IsCurrent: false),
        new("gpt-mini", "GPT Mini", "快速响应", IsCurrent: true),
        new("gpt-nano", "GPT Nano", "轻量便宜", IsCurrent: false),
    ];

    /// <summary>构造挂起状态的选择器（焦点定位当前模型 index=1）。</summary>
    private static ModelPickerCoordinator BeginCoordinator()
    {
        var coordinator = new ModelPickerCoordinator();
        coordinator.Begin(SampleItems);
        return coordinator;
    }

    // ---- ModelPickerCoordinator ----

    /// <summary>Begin — IsPending + Items 就位 + 焦点定位当前模型（index=1）。</summary>
    [Fact]
    public void Begin_SetsItemsAndFocusesCurrent()
    {
        var coordinator = BeginCoordinator();

        Assert.True(coordinator.IsPending);
        Assert.Equal(3, coordinator.Items.Count);
        Assert.Equal(1, coordinator.FocusIndex);
        Assert.Equal("gpt-mini", coordinator.Focused?.Id);
    }

    /// <summary>Begin 无当前模型 — 焦点回退 0。</summary>
    [Fact]
    public void Begin_NoCurrent_FocusesZero()
    {
        var coordinator = new ModelPickerCoordinator();
        coordinator.Begin([new ModelPickerItem("a", "A", "", IsCurrent: false)]);

        Assert.Equal(0, coordinator.FocusIndex);
    }

    /// <summary>焦点移动 — 上下 clamp 不环绕（0 上保持 0；末位下保持末位）。</summary>
    [Fact]
    public void MoveFocus_ClampsNotWraps()
    {
        var coordinator = BeginCoordinator();

        coordinator.MoveFocus(-1);
        Assert.Equal(0, coordinator.FocusIndex);
        coordinator.MoveFocus(-1);
        Assert.Equal(0, coordinator.FocusIndex);

        coordinator.MoveFocus(2);
        Assert.Equal(2, coordinator.FocusIndex);
        coordinator.MoveFocus(1);
        Assert.Equal(2, coordinator.FocusIndex);
    }

    /// <summary>可见窗口 — min(10, n) 可见 + HiddenCount 溢出计数。</summary>
    [Fact]
    public void VisibleCount_CapsAtTenAndCountsHidden()
    {
        var many = Enumerable.Range(0, 13)
            .Select(i => new ModelPickerItem($"m{i}", $"M{i}", "", IsCurrent: false))
            .ToArray();

        var coordinator = new ModelPickerCoordinator();
        coordinator.Begin(many);

        Assert.Equal(10, coordinator.VisibleCount);
        Assert.Equal(3, coordinator.HiddenCount);

        var small = BeginCoordinator();
        Assert.Equal(3, small.VisibleCount);
        Assert.Equal(0, small.HiddenCount);
    }

    /// <summary>滚动窗口跟随 — 焦点移出窗口时窗口下移，移回顶部时上移。</summary>
    [Fact]
    public void MoveFocus_ScrollsWindowToKeepFocusVisible()
    {
        var many = Enumerable.Range(0, 13)
            .Select(i => new ModelPickerItem($"m{i}", $"M{i}", "", IsCurrent: false))
            .ToArray();
        var coordinator = new ModelPickerCoordinator();
        coordinator.Begin(many);

        Assert.Equal(0, coordinator.WindowStart);
        coordinator.MoveFocus(9);
        Assert.Equal(9, coordinator.FocusIndex);
        Assert.Equal(0, coordinator.WindowStart);

        coordinator.MoveFocus(1);
        Assert.Equal(10, coordinator.FocusIndex);
        Assert.Equal(1, coordinator.WindowStart);

        coordinator.MoveFocus(-9);
        Assert.Equal(1, coordinator.FocusIndex);
        Assert.Equal(1, coordinator.WindowStart);
        coordinator.MoveFocus(-1);
        Assert.Equal(0, coordinator.FocusIndex);
        Assert.Equal(0, coordinator.WindowStart);
    }

    /// <summary>Complete — 状态清空 + 幂等（二次 no-op）。</summary>
    [Fact]
    public void Complete_ClearsAndIsIdempotent()
    {
        var coordinator = BeginCoordinator();

        coordinator.Complete();
        Assert.False(coordinator.IsPending);
        Assert.Null(coordinator.Focused);

        coordinator.Complete();
        Assert.False(coordinator.IsPending);
    }

    /// <summary>Begin 空清单 — 断言（开心路径：宿主保证非空）。</summary>
    [Fact]
    public void Begin_EmptyItems_Throws()
    {
        var coordinator = new ModelPickerCoordinator();

        Assert.Throws<ArgumentException>(() => coordinator.Begin([]));
    }

    // ---- ModelPickerDialog 渲染 ----

    /// <summary>渲染顶边框 + 标题 — permission 色全宽 ─ 行 + 1 缩进 bold permission "Select model"。</summary>
    [Fact]
    public void Render_BorderAndTitle()
    {
        using var screen = new Screen(cols: 40, rows: 16);
        var coordinator = BeginCoordinator();
        new ModelPickerDialog(coordinator).Render(screen, new Rect(0, 0, 40, 16), Theme.Dark);

        var border = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Permission });
        Assert.Equal(new string('─', 40), RowText.Of(screen, 1, 0, 40));
        Assert.Equal(border, screen[1, 0].StyleId);

        var title = screen.Styles.GetOrAdd(new TextStyle { Bold = true, Foreground = Theme.Dark.Permission });
        Assert.StartsWith(" Select model", RowText.Of(screen, 2, 0, 40));
        Assert.Equal(title, screen[2, 1].StyleId);
    }

    /// <summary>渲染副标题 — 1 缩进 dim。</summary>
    [Fact]
    public void Render_SubtitleDim()
    {
        using var screen = new Screen(cols: 60, rows: 16);
        var coordinator = BeginCoordinator();
        new ModelPickerDialog(coordinator).Render(screen, new Rect(0, 0, 60, 16), Theme.Dark);

        var text = RowText.Of(screen, 3, 0, 60);
        Assert.StartsWith(" Switch between models.", text);
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[3, 1].StyleId);
    }

    /// <summary>渲染列表行 — 焦点行 ❯ suggestion + 序号 dim + 名称；当前行尾 ✓ success；非焦点行空格指示。</summary>
    [Fact]
    public void Render_ListRowsFocusTickAndIndices()
    {
        using var screen = new Screen(cols: 60, rows: 16);
        var coordinator = BeginCoordinator();
        new ModelPickerDialog(coordinator).Render(screen, new Rect(0, 0, 60, 16), Theme.Dark);

        // row5 = 首项（非焦点）：空格指示 + "1. " + 名称
        var first = RowText.Of(screen, 5, 0, 60);
        Assert.StartsWith("  1. GPT X", first);

        // row7 = 当前+焦点项：❯ + "2. " + 名称 + ✓
        var focused = RowText.Of(screen, 7, 0, 60);
        Assert.StartsWith("❯ 2. GPT Mini ✓", focused);
        var suggestion = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Suggestion });
        Assert.Equal(suggestion, screen[7, 0].StyleId);
        var success = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Success });
        Assert.Equal(success, screen[7, "❯ 2. GPT Mini ".Length].StyleId);

        // row9 = 第三项（非焦点、无 ✓）
        Assert.StartsWith("  3. GPT Nano", RowText.Of(screen, 9, 0, 60));
    }

    /// <summary>渲染描述行 — 名称行下一行缩进 dim。</summary>
    [Fact]
    public void Render_DescriptionIndentedDim()
    {
        using var screen = new Screen(cols: 60, rows: 16);
        var coordinator = BeginCoordinator();
        new ModelPickerDialog(coordinator).Render(screen, new Rect(0, 0, 60, 16), Theme.Dark);

        Assert.StartsWith("     快速响应", RowText.Of(screen, 8, 0, 60));
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(dim, screen[8, 5].StyleId);
    }

    /// <summary>渲染溢出行 — 超出 10 条可见折叠 "and N more…"。</summary>
    [Fact]
    public void Render_OverflowRowWhenHidden()
    {
        var many = Enumerable.Range(0, 13)
            .Select(i => new ModelPickerItem($"m{i}", $"M{i}", "", IsCurrent: false))
            .ToArray();
        using var screen = new Screen(cols: 60, rows: 40);
        var coordinator = new ModelPickerCoordinator();
        coordinator.Begin(many);
        new ModelPickerDialog(coordinator).Render(screen, new Rect(0, 0, 60, 40), Theme.Dark);

        // 13 项无描述：margin1 + 边框1 + 标题1 + 副标题1 + 空行1 + 可见10行 → 溢出行在 row15
        var overflow = RowText.Of(screen, 15, 0, 60);
        Assert.StartsWith(" and 3 more…", overflow);
    }

    /// <summary>渲染快捷键行 — 尾行 "Enter confirm · Esc exit" dim。</summary>
    [Fact]
    public void Render_ShortcutHintRow()
    {
        using var screen = new Screen(cols: 60, rows: 16);
        var coordinator = BeginCoordinator();
        var dialog = new ModelPickerDialog(coordinator);
        dialog.Render(screen, new Rect(0, 0, 60, 16), Theme.Dark);

        var hintRow = dialog.MeasureHeight(60) - 1;
        Assert.StartsWith(" Enter confirm · Esc exit", RowText.Of(screen, hintRow, 0, 60));
    }

    /// <summary>MeasureHeight — 3 项带描述 = margin1 + 边框1 + 标题1 + 副标题1 + 空行1 + 3×2 + 空行1 + 快捷键1 = 13。</summary>
    [Fact]
    public void MeasureHeight_WithDescriptions()
    {
        var coordinator = BeginCoordinator();
        Assert.Equal(13, new ModelPickerDialog(coordinator).MeasureHeight(60));
    }

    /// <summary>MeasureHeight — 无描述项高度减半。</summary>
    [Fact]
    public void MeasureHeight_WithoutDescriptions()
    {
        var coordinator = new ModelPickerCoordinator();
        coordinator.Begin([
            new ModelPickerItem("a", "A", "", IsCurrent: false),
            new ModelPickerItem("b", "B", "", IsCurrent: false),
        ]);
        Assert.Equal(9, new ModelPickerDialog(coordinator).MeasureHeight(60));
    }

    /// <summary>挂起被清除后渲染 — 空输出（状态投影，同权限对话框）。</summary>
    [Fact]
    public void Render_AfterComplete_NoOutput()
    {
        using var screen = new Screen(cols: 40, rows: 16);
        var coordinator = BeginCoordinator();
        var dialog = new ModelPickerDialog(coordinator);
        coordinator.Complete();

        dialog.Render(screen, new Rect(0, 0, 40, 16), Theme.Dark);

        Assert.Equal(new string(' ', 40), RowText.Of(screen, 1, 0, 40));
    }

    // ---- ReplShell 模态改道 ----

    /// <summary>构造测试 shell（对齐 PermissionDialogTests 模式）。</summary>
    private static (ReplShell Shell, FakeModelEngine Engine) CreateShell()
    {
        var engine = new FakeModelEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter());
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>BeginModelPicker — 对话框插入滚动区 + 挂起。</summary>
    [Fact]
    public void Shell_BeginModelPicker_InsertsDialog()
    {
        var (shell, _) = CreateShell();
        shell.BeginModelPicker(SampleItems);

        Assert.True(shell.ModelPicker.IsPending);
        Assert.Contains(shell.View.Messages.Items, m => m is ModelPickerDialog);
    }

    /// <summary>上下箭头 — 焦点移动不进输入缓冲。</summary>
    [Fact]
    public void Shell_ArrowsMoveFocusNotInput()
    {
        var (shell, _) = CreateShell();
        shell.BeginModelPicker(SampleItems);
        Assert.Equal(1, shell.ModelPicker.FocusIndex);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.Equal(0, shell.ModelPicker.FocusIndex);
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.Equal(1, shell.ModelPicker.FocusIndex);

        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>Enter — 确认回调携带选中 id + 对话框移除。</summary>
    [Fact]
    public void Shell_Enter_CompletesWithSelectedId()
    {
        var (shell, _) = CreateShell();
        var selected = new[] { (string?)"none" };
        shell.BeginModelPicker(SampleItems, id => selected[0] = id);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal("gpt-x", selected[0]);
        Assert.False(shell.ModelPicker.IsPending);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is ModelPickerDialog);
    }

    /// <summary>Esc — 取消回调 null + 对话框移除。</summary>
    [Fact]
    public void Shell_Escape_CompletesWithNull()
    {
        var (shell, _) = CreateShell();
        var selected = new[] { (string?)"sentinel" };
        shell.BeginModelPicker(SampleItems, id => selected[0] = id);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.Null(selected[0]);
        Assert.False(shell.ModelPicker.IsPending);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is ModelPickerDialog);
    }

    /// <summary>其他字符 — 模态吞掉（不进输入缓冲、不提交）。</summary>
    [Fact]
    public void Shell_OtherCharsSwallowedWhilePending()
    {
        var (shell, engine) = CreateShell();
        shell.BeginModelPicker(SampleItems);

        shell.HandleKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Empty(engine.Submits);
        Assert.True(shell.ModelPicker.IsPending);
    }

    /// <summary>权限挂起优先 — 权限对话框模态先于模型选择器（引擎请求优先级更高）。</summary>
    [Fact]
    public async Task Shell_PermissionTakesPrecedenceOverModelPicker()
    {
        var (shell, _) = CreateShell();
        shell.BeginPermissionRequest("Bash", "run tests");
        shell.BeginModelPicker(SampleItems);
        var permissionTask = shell.Permission.Wait();

        shell.HandleKey(new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false));

        Assert.Equal(PermissionChoice.Allow, await permissionTask);
        Assert.True(shell.ModelPicker.IsPending);
    }

    // ---- CommandPickerEngine 装饰器（ModelPickerEngine 泛化 — command/noun 参数化，/model 与 /vendor 共用） ----

    /// <summary>Submit "/model" — 拦截打开选择器，不透传内层引擎。</summary>
    [Fact]
    public void Engine_SubmitModelCommand_OpensPickerNotForwarded()
    {
        var inner = new FakeModelEngine();
        var (shell, _) = CreateShell();
        var pickerEngine = new CommandPickerEngine(inner, "/model", "模型", () => SampleItems, _ => { });
        pickerEngine.Attach(shell);

        pickerEngine.Submit("/model");

        Assert.Empty(inner.Submits);
        Assert.True(shell.ModelPicker.IsPending);
    }

    /// <summary>Submit 其他输入 — 透传内层。</summary>
    [Fact]
    public void Engine_SubmitOtherText_ForwardedToInner()
    {
        var inner = new FakeModelEngine();
        var (shell, _) = CreateShell();
        var pickerEngine = new CommandPickerEngine(inner, "/model", "模型", () => SampleItems, _ => { });
        pickerEngine.Attach(shell);

        pickerEngine.Submit("/model mini");

        Assert.Equal(["/model mini"], inner.Submits);
        Assert.False(shell.ModelPicker.IsPending);
    }

    /// <summary>确认选择 — applyItem 收到 id + PostInfo 反馈消息入滚动区。</summary>
    [Fact]
    public void Engine_ConfirmSelection_AppliesModelAndPostsFeedback()
    {
        var inner = new FakeModelEngine();
        var applied = new List<string>();
        var (shell, _) = CreateShell();
        var pickerEngine = new CommandPickerEngine(inner, "/model", "模型", () => SampleItems, applied.Add);
        pickerEngine.Attach(shell);

        pickerEngine.Submit("/model");
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal(["gpt-mini"], applied);
        Assert.Contains(shell.View.Messages.Items, m => m is AssistantMessageItem { } a && a.Text.Contains("GPT Mini"));
    }

    /// <summary>Esc 取消 — applyItem 不调用 + 无反馈消息。</summary>
    [Fact]
    public void Engine_EscapeCancel_NoApplyNoFeedback()
    {
        var inner = new FakeModelEngine();
        var applied = new List<string>();
        var (shell, _) = CreateShell();
        var pickerEngine = new CommandPickerEngine(inner, "/model", "模型", () => SampleItems, applied.Add);
        pickerEngine.Attach(shell);

        pickerEngine.Submit("/model");
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.Empty(applied);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is AssistantMessageItem { } a && a.Text.Contains("切换"));
    }

    /// <summary>透传成员 — Interrupt/SetPermissionMode/DrainEvents/IsAlive 走内层。</summary>
    [Fact]
    public void Engine_PassThroughMembers()
    {
        var inner = new FakeModelEngine();
        inner.Events.Enqueue(new BridgeEvent.TurnStarted());
        var pickerEngine = new CommandPickerEngine(inner, "/model", "模型", () => SampleItems, _ => { });

        pickerEngine.Interrupt();
        pickerEngine.SetPermissionMode("plan");
        var drained = pickerEngine.DrainEvents();

        Assert.Equal(1, inner.Interrupts);
        Assert.Equal(["plan"], inner.PermissionModes);
        Assert.Single(drained);
        Assert.True(pickerEngine.IsAlive);
    }

    /// <summary>vendor 装饰器（TASK058）— Submit "/vendor" 拦截打开选择器 + 反馈文案带供应商名词；"/model" 透传内层。</summary>
    [Fact]
    public void Engine_VendorCommand_OpensPickerAndModelForwards()
    {
        var inner = new FakeModelEngine();
        var applied = new List<string>();
        var (shell, _) = CreateShell();
        var vendorEngine = new CommandPickerEngine(inner, "/vendor", "供应商", () => SampleItems, applied.Add);
        vendorEngine.Attach(shell);

        vendorEngine.Submit("/model");
        Assert.Equal(["/model"], inner.Submits);
        Assert.False(shell.ModelPicker.IsPending);

        vendorEngine.Submit("/vendor");
        Assert.True(shell.ModelPicker.IsPending);
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal(["gpt-mini"], applied);
        Assert.Contains(shell.View.Messages.Items, m => m is AssistantMessageItem { } a && a.Text.Contains("已切换供应商"));
    }

    /// <summary>叠层装饰（TASK058 Host 组装形态）— vendor 外层 + model 内层，两命令各自拦截互不干扰。</summary>
    [Fact]
    public void Engine_StackedVendorsOuterModelInner_BothIntercept()
    {
        var inner = new FakeModelEngine();
        var modelApplied = new List<string>();
        var vendorApplied = new List<string>();
        var (shell, _) = CreateShell();
        var modelEngine = new CommandPickerEngine(inner, "/model", "模型", () => SampleItems, modelApplied.Add);
        var vendorEngine = new CommandPickerEngine(modelEngine, "/vendor", "供应商", () => SampleItems, vendorApplied.Add);
        vendorEngine.Attach(shell);

        vendorEngine.Submit("/model");
        Assert.True(shell.ModelPicker.IsPending);
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        Assert.Empty(modelApplied);

        vendorEngine.Submit("/vendor");
        Assert.True(shell.ModelPicker.IsPending);
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Empty(modelApplied);
        Assert.Equal(["gpt-mini"], vendorApplied);
        Assert.Empty(inner.Submits);
    }

    /// <summary>记录型假引擎（PermissionDialogTests.FakePermissionEngine 同构）。</summary>
    private sealed class FakeModelEngine : IReplEngine
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
