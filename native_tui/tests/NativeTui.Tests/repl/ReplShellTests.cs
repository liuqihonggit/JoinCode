namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell 单元测试 — 共享 REPL 外壳（HostSession/DemoSession 提取，TASK055 M7 前置）：
/// 种子内容挂载顺序、引擎事件应用链（经 ReplBridgeDriver 唯一翻译层）、引擎死亡检测、
/// Esc 三态分派（忙打断/闲清输入/闲退出）、提交路径（用户消息 + 引擎 Submit）、权限循环透传。
/// </summary>
public sealed class ReplShellTests
{
    /// <summary>构造测试 shell（StringWriter 收渲染输出，seed 消息可缺省）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell(IReadOnlyList<IScrollItem>? seed = null)
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter(), seedMessages: seed);
        return (shell, engine);
    }

    /// <summary>模拟键入（逐字符插入输入缓冲）。</summary>
    private static void TypeText(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.Input.Insert(c);
        }
    }

    /// <summary>SeedContent：banner 首项、seed 消息居中、spinner 恒为滚动区末项，footer 四字段就位。</summary>
    [Fact]
    public void SeedContent_BannerFirst_SpinnerLast_SeedMessagesBetween()
    {
        var (shell, _) = CreateShell(seed: [new UserMessageItem("seed q"), new AssistantMessageItem("seed a")]);
        shell.SeedContent();

        var messages = shell.View.Messages.Items;
        Assert.Equal(4, messages.Count);
        Assert.IsType<BannerItem>(messages[0]);
        Assert.IsType<UserMessageItem>(messages[1]);
        Assert.IsType<AssistantMessageItem>(messages[2]);
        Assert.IsType<SpinnerItem>(messages[^1]);

        Assert.Equal(PermissionMode.Auto, shell.View.Footer.Mode);
        Assert.Equal("未登录 · 运行 /login", shell.View.Footer.LoginError);
        Assert.NotNull(shell.View.Footer.AgentsHint);
        Assert.NotNull(shell.View.Footer.Notification);
    }

    /// <summary>DrainEngine：TurnStarted 事件驱动 busy（footer.Busy + 内部忙标记）。</summary>
    [Fact]
    public void DrainEngine_TurnStarted_SetsBusy()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();
        Assert.False(shell.IsBusy);

        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
        Assert.True(shell.IsBusy);
        Assert.True(shell.View.Footer.Busy);
    }

    /// <summary>DrainEngine：TurnCompleted 解除 busy。</summary>
    [Fact]
    public void DrainEngine_TurnCompleted_ClearsBusy()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();

        engine.Events.Enqueue(new BridgeEvent.TurnCompleted());
        shell.DrainEngine();
        Assert.False(shell.IsBusy);
        Assert.False(shell.View.Footer.Busy);
    }

    /// <summary>DrainEngine：权限回执事件更新 footer 模式（引擎回执为准）。</summary>
    [Fact]
    public void DrainEngine_PermissionModeChanged_UpdatesFooter()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();
        Assert.Equal(PermissionMode.Auto, shell.View.Footer.Mode);

        engine.Events.Enqueue(new BridgeEvent.PermissionModeChanged("plan"));
        shell.DrainEngine();
        Assert.Equal(PermissionMode.Plan, shell.View.Footer.Mode);
    }

    /// <summary>DrainEngine：PermissionRequest 事件经 Driver 钩子分派到权限对话框（模态挂起 + 对话框项插入滚动区）。</summary>
    [Fact]
    public void DrainEngine_PermissionRequestEvent_BeginsPermissionDialog()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();

        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        engine.Events.Enqueue(new BridgeEvent.PermissionRequest("Bash", "运行演示命令"));
        shell.DrainEngine();

        Assert.True(shell.Permission.IsPending);
        Assert.Contains(shell.View.Messages.Items, m => m is PermissionRequestItem);
    }

    /// <summary>DrainEngine：引擎死亡（非忙）— 追加错误消息并请求停止。</summary>
    [Fact]
    public void DrainEngine_EngineDead_AppendsErrorAndStops()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();
        engine.Alive = false;

        shell.DrainEngine();
        Assert.False(shell.Running);
        Assert.Contains(shell.View.Messages.Items, m => m is ErrorMessageItem);
    }

    /// <summary>DrainEngine：忙时引擎死亡不立即退出（回合收尾事件优先）。</summary>
    [Fact]
    public void DrainEngine_EngineDeadWhileBusy_KeepsRunning()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();

        engine.Alive = false;
        shell.DrainEngine();
        Assert.True(shell.Running);
    }

    /// <summary>Esc 忙时：双击才请求引擎打断（TASK059 退出安全 — 单击等双击窗口无操作）。</summary>
    [Fact]
    public void Escape_WhileBusy_InterruptsEngine()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();

        shell.HandleEscape();
        Assert.Equal(0, engine.Interrupts);
        shell.HandleEscape();
        Assert.Equal(1, engine.Interrupts);
        Assert.True(shell.Running);
    }

    /// <summary>Esc 闲时有输入：清空输入不退出。</summary>
    [Fact]
    public void Escape_IdleWithText_ClearsInput()
    {
        var (shell, _) = CreateShell();
        shell.SeedContent();
        TypeText(shell, "draft");

        shell.HandleEscape();
        Assert.True(shell.Running);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>Esc 闲时无输入：永不退出（TASK059 退出安全 — 退出路径只剩 Ctrl+D 与 /exit 命令）。</summary>
    [Fact]
    public void Escape_IdleEmpty_NeverStops()
    {
        var (shell, _) = CreateShell();
        shell.SeedContent();

        shell.HandleEscape();
        shell.HandleEscape();
        Assert.True(shell.Running);
    }

    /// <summary>提交：空文本忽略（不产用户消息、不透传引擎）。</summary>
    [Fact]
    public void Submit_EmptyText_Ignored()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();

        shell.Submit();
        Assert.Empty(engine.Submits);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is UserMessageItem);
    }

    /// <summary>提交：非空文本 — 用户消息插入 spinner 之前 + 透传引擎 + 输入清空。</summary>
    [Fact]
    public void Submit_Text_AddsUserMessageAndForwards()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();
        TypeText(shell, "hello engine");

        shell.Submit();
        Assert.Equal(["hello engine"], engine.Submits);
        Assert.Contains(shell.View.Messages.Items, m => m is UserMessageItem);
        Assert.Equal(string.Empty, shell.Input.Text);

        var spinnerIndex = shell.View.Messages.Count - 1;
        var userIndex = shell.View.Messages.Items.ToList().FindIndex(m => m is UserMessageItem);
        Assert.True(userIndex < spinnerIndex);
    }

    /// <summary>Shift+Tab 权限循环：透传引擎请求，footer 不本地直改（回执事件为准）。</summary>
    [Fact]
    public void CyclePermissionMode_ForwardsToEngine()
    {
        var (shell, engine) = CreateShell();
        shell.SeedContent();

        shell.CyclePermissionMode();
        Assert.Single(engine.PermissionModes);
        Assert.Equal(PermissionMode.Auto, shell.View.Footer.Mode);
    }

    /// <summary>AdvanceAnimClock：动画时钟广播给滚动区全部 IAnimatable — spinner 与注入项同值（基准 useBlink 共享时钟 → 全实例同步闪烁）；每拍 +50ms。</summary>
    [Fact]
    public void AdvanceAnimClock_BroadcastsToAllAnimatableItems()
    {
        var (shell, _) = CreateShell();
        shell.SeedContent();
        var fake = new FakeAnimatableItem();
        shell.View.Messages.Insert(shell.View.Messages.Count - 1, fake);

        shell.AdvanceAnimClock();
        Assert.Equal(50, fake.AnimTimeMs);
        var spinner = (SpinnerItem)shell.View.Messages.Items[^1];
        Assert.Equal(50, spinner.AnimTimeMs);
        Assert.Equal(fake.AnimTimeMs, spinner.AnimTimeMs);

        shell.AdvanceAnimClock();
        Assert.Equal(100, fake.AnimTimeMs);
        Assert.Equal(fake.AnimTimeMs, spinner.AnimTimeMs);
    }
}

/// <summary>测试注入用动画项（IAnimatable 消费面 — 广播路由断言）。</summary>
internal sealed class FakeAnimatableItem : IScrollItem, IAnimatable
{
    public long AnimTimeMs { get; set; }

    public int MeasureHeight(int width) => 1;

    public void Render(Screen screen, Rect frame, Theme theme)
    {
    }
}
