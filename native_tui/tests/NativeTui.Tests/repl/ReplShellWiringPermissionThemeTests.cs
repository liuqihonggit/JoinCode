namespace NativeTui.Tests.Repl;

/// <summary>
/// S11 统一接线 — 权限键位与主题域冒烟（S8 接线点 7 + S9 接线点 8/9）：
/// 权限对话框 Tab 遍历/Space 切换选中/Ctrl+E 解释展开/BackTab 权限模式循环请求
///（ModeCycleRequested → 引擎 SetPermissionMode 回执通道）；/theme 本地拦截三形态
///（合法切换通知/未知名诱导提示/无参清单）；WriteExit 光标显隐对称恢复。
/// </summary>
public sealed class ReplShellWiringPermissionThemeTests
{
    /// <summary>构造挂起权限对话框的测试 shell（SeedContent + Begin 权限请求）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShellWithPendingPermission()
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter(), sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        engine.Events.Enqueue(new BridgeEvent.PermissionRequest("Bash", "运行演示命令"));
        shell.DrainEngine();
        return (shell, engine);
    }

    /// <summary>模拟键入。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false));
        }
    }

    /// <summary>y/n 单键直选（对齐基准 Confirmation y/n — defaultBindings.ts:133-134）：
    /// y → Allow（第一档）、n → Deny（第三档）。</summary>
    [Fact]
    public async Task Permission_YKey_SelectsAllow()
    {
        var (shell, _) = CreateShellWithPendingPermission();
        Assert.True(shell.Permission.IsPending);
        var choiceTask = shell.Permission.Wait();

        Type(shell, "y");

        Assert.False(shell.Permission.IsPending);
        Assert.Equal(PermissionChoice.Allow, await choiceTask);
    }

    /// <summary>n 键直选 → Deny。</summary>
    [Fact]
    public async Task Permission_NKey_SelectsDeny()
    {
        var (shell, _) = CreateShellWithPendingPermission();
        var choiceTask = shell.Permission.Wait();

        Type(shell, "n");

        Assert.False(shell.Permission.IsPending);
        Assert.Equal(PermissionChoice.Deny, await choiceTask);
    }

    /// <summary>接线点7 — Tab 遍历选项：焦点 +1 环绕（0→1→2→0）。</summary>
    [Fact]
    public void PermissionTab_CyclesFocus()
    {
        var (shell, _) = CreateShellWithPendingPermission();
        Assert.Equal(0, shell.Permission.FocusIndex);

        shell.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
        Assert.Equal(1, shell.Permission.FocusIndex);
        shell.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
        shell.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
        Assert.Equal(0, shell.Permission.FocusIndex);
    }

    /// <summary>接线点7 — Space 切换选中态（翻转 + 模态吞键不进输入缓冲）。</summary>
    [Fact]
    public void PermissionSpace_TogglesSelection()
    {
        var (shell, _) = CreateShellWithPendingPermission();
        Assert.False(shell.Permission.SelectionToggled);

        shell.HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));

        Assert.True(shell.Permission.SelectionToggled);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>接线点7 — Ctrl+E 切换解释展开态（模态吞键）。</summary>
    [Fact]
    public void PermissionCtrlE_TogglesExplanation()
    {
        var (shell, _) = CreateShellWithPendingPermission();
        Assert.False(shell.Permission.ExplanationExpanded);

        // \x05 = Ctrl+e（ReadKey 控制字母归一路径）
        shell.HandleKey(new ConsoleKeyInfo('\x05', ConsoleKey.E, false, false, true));

        Assert.True(shell.Permission.ExplanationExpanded);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>接线点7 — BackTab 权限模式循环请求：ModeCycleRequested 转发引擎回执通道
    ///（挂起态 BackTab 不再被模态吞掉，复用 Shift+Tab 的 CyclePermissionMode 实现）。</summary>
    [Fact]
    public void PermissionBackTab_ForwardsModeCycleToEngine()
    {
        var (shell, engine) = CreateShellWithPendingPermission();
        Assert.Empty(engine.PermissionModes);
        Assert.True(shell.Permission.IsPending);

        // ReadKey 路径 Tab+Shift 归一 BackTab
        shell.HandleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: true, alt: false, control: false));

        Assert.True(shell.Permission.IsPending);
        Assert.Single(engine.PermissionModes);
    }

    /// <summary>接线点8 — /theme 合法名：本地切换 + 通知反馈 + 不透传引擎。</summary>
    [Fact]
    public void ThemeCommand_ValidName_SwitchesLocally()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();

        Type(shell, "/theme light");
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Empty(engine.Submits);
        Assert.Contains(shell.View.Messages.Items, m => m is UserMessageItem { Text: "/theme light" });
        // 切换反馈走通知队列（footer 右侧 — SeedContent 的 effort 提示占用 Current，主题通知入队待出）
        Assert.Equal(1, shell.View.Footer.Notifications!.QueuedCount);
    }

    /// <summary>S11 遗留#3 双通道裁决 — /theme auto：TUI 侧支持 auto 名（本地色板按当前时段解析
    /// + 持久化写 auto 字面量 — 与引擎侧 SetAsync("theme","auto") 语义一致，重启后 Host 侧再解析）。</summary>
    [Fact]
    public async Task ThemeCommand_Auto_PersistsAutoLiteral()
    {
        var engine = new FakeReplEngine();
        var saved = new List<string>();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            saveThemeAsync: name => { saved.Add(name); return Task.CompletedTask; });
        shell.SeedContent();

        Type(shell, "/theme auto");
        shell.Submit();

        await WaitForAsync(() => saved.Count > 0);
        // 持久化写原名 auto 字面量（非解析后的 dark/light）
        Assert.Equal(["auto"], saved);
        Assert.Empty(engine.Submits);
    }

    /// <summary>S11 遗留#1 — /theme 合法名：持久化委托 fire-and-forget 收到主题名
    ///（宿主写回 settings.json 闭环；未知名不触发持久化）。</summary>
    [Fact]
    public async Task ThemeCommand_ValidName_InvokesSaveThemeDelegate()
    {
        var engine = new FakeReplEngine();
        var saved = new List<string>();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            saveThemeAsync: name => { saved.Add(name); return Task.CompletedTask; });
        shell.SeedContent();

        Type(shell, "/theme light");
        shell.Submit();

        await WaitForAsync(() => saved.Count > 0);
        Assert.Equal(["light"], saved);

        // 未知名不触发持久化（错误提示路径无写盘）
        var savedBefore = saved.Count;
        Type(shell, "/theme nosuch");
        shell.Submit();
        await Task.Delay(50);
        Assert.Equal(savedBefore, saved.Count);
    }

    /// <summary>S11 遗留#1 — 持久化委托抛异常静默降级（切换仍生效 + 通知仍发 — 写盘失败不打扰用户）。</summary>
    [Fact]
    public void ThemeCommand_SaveDelegateThrows_DegradesSilently()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            saveThemeAsync: _ => throw new IOException("disk full"));
        shell.SeedContent();

        Type(shell, "/theme dark");
        shell.Submit();

        // 切换本地生效 + 通知入队（持久化失败不影响会话级切换）
        Assert.Equal(1, shell.View.Footer.Notifications!.QueuedCount);
    }

    /// <summary>轮询等待异步完成（fire-and-forget 委托投递 — 16 次 × 25ms 收敛窗）。</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 16 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), "异步条件未在收敛窗内满足");
    }

    /// <summary>接线点8 — /theme 未知名：错误提示带诱导（合法值清单 + 正确写法）。</summary>
    [Fact]
    public void ThemeCommand_UnknownName_ShowsGuidance()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();

        Type(shell, "/theme nosuch");
        shell.Submit();

        Assert.Empty(engine.Submits);
        var feedback = shell.View.Messages.Items.OfType<ToolResultItem>().Single();
        Assert.Contains("未知主题", feedback.Summary);
        Assert.Contains("dark", feedback.Summary);
    }

    /// <summary>接线点8 — /theme 无参：显示可用主题清单。</summary>
    [Fact]
    public void ThemeCommand_NoArgument_ListsNames()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();

        Type(shell, "/theme");
        shell.Submit();

        Assert.Empty(engine.Submits);
        var feedback = shell.View.Messages.Items.OfType<ToolResultItem>().Single();
        Assert.Contains("可用主题", feedback.Summary);
    }

    /// <summary>接线点9 — WriteExit 尾部恢复硬件光标（DEC 25 show — inline 与视口两模式对称）。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteExit_ShowsCursor_BothRenderModes(bool inline)
    {
        var writer = new StringWriter();
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), writer,
            inline: inline, sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();

        shell.WriteExit();

        Assert.Contains(CursorLifecycle.Show, writer.ToString());
    }
}
