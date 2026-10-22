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
