namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell 消息光标态动作接线集成测试（S6 任务 4 — 消息操作域消费）：
/// 光标态 c → MessageCursor.ExtractFullText → 剪贴板委托 + 通知反馈"已复制全文"；
/// 光标态 p → ExtractPrimaryInput → 剪贴板委托 + 通知反馈"已复制主参数"；
/// 不可复制类型/映射外工具/未注入剪贴板 → 静默无操作（无通知入队）；
/// 操作条投影：光标态激活时 View.MessageActions 按选中项派生（p 段随工具映射显隐），
/// 退出光标态 → null（渲染帧投影验证）。
/// </summary>
public sealed class ReplShellIntegrationActionsTests
{
    /// <summary>构造测试 shell（剪贴板捕获委托 + 固定尺寸源）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine, List<string> Copied) CreateShell(bool withClipboard = true)
    {
        var engine = new FakeReplEngine();
        var copied = new List<string>();
        var shell = new ReplShell(
            engine,
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            clipboard: withClipboard ? copied.Add : null);
        shell.SeedContent();
        return (shell, engine, copied);
    }

    private static void ShiftUp(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, shift: true, alt: false, control: false));

    private static void Esc(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

    private static void Char(ReplShell shell, char c) =>
        shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.A, shift: false, alt: false, control: false));

    /// <summary>光标态 c — 选中助手消息全文写入剪贴板 + 通知反馈"已复制全文"（种子通知显示中 → 入队等待出队）。</summary>
    [Fact]
    public void CursorMode_C_CopiesFullText_WithNotification()
    {
        var (shell, engine, copied) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("完整回复内容"));
        shell.DrainEngine();
        ShiftUp(shell);

        Char(shell, 'c');

        Assert.Equal(["完整回复内容"], copied);
        var notifications = shell.View.Footer.Notifications!;
        Assert.NotNull(notifications.Current);
    }

    /// <summary>光标态 p — 选中 Bash 工具调用复制主参数 command + 通知反馈"已复制主参数"（入队等待出队）。</summary>
    [Fact]
    public void CursorMode_P_CopiesPrimaryInput_WithNotification()
    {
        var (shell, engine, copied) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.ToolCall("Bash", """{"command": "dotnet build"}"""));
        shell.DrainEngine();
        ShiftUp(shell);

        Char(shell, 'p');

        Assert.Equal(["dotnet build"], copied);
        Assert.NotNull(shell.View.Footer.Notifications!.Current);
    }

    /// <summary>光标态 p — 映射外工具（未知工具名）主参数提取 null → 静默（无剪贴板写入无通知入队）。</summary>
    [Fact]
    public void CursorMode_P_UnmappedTool_SilentNoOp()
    {
        var (shell, engine, copied) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.ToolCall("MysteryTool", "some args"));
        shell.DrainEngine();
        ShiftUp(shell);

        Char(shell, 'p');

        Assert.Empty(copied);
        Assert.Null(shell.View.Footer.Notifications!.Current);
    }

    /// <summary>未注入剪贴板委托 — c 复制降级静默（无通知入队，无异常）。</summary>
    [Fact]
    public void CursorMode_C_NullClipboard_SilentNoOp()
    {
        var (shell, engine, _) = CreateShell(withClipboard: false);
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("回复"));
        shell.DrainEngine();
        ShiftUp(shell);

        Char(shell, 'c');

        Assert.Null(shell.View.Footer.Notifications!.Current);
    }

    /// <summary>操作条投影 — 光标态激活渲染帧后 View.MessageActions 按选中项派生（p 段随映射显隐；
    /// Bash 非聚合收录工具 — 逐条 ToolCallItem 落位可被光标选中）。</summary>
    [Fact]
    public void CursorMode_RenderFrame_ProjectsMessageActions()
    {
        var (shell, engine, _) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.ToolCall("Bash", """{"command": "dotnet build"}"""));
        shell.DrainEngine();

        Assert.Null(shell.View.MessageActions);
        ShiftUp(shell);
        shell.RenderAndFlush();

        var actions = shell.View.MessageActions;
        Assert.NotNull(actions);
        Assert.False(actions.CanExpand);
        Assert.Equal("command", actions.PrimaryInputLabel);

        Esc(shell);
        shell.RenderAndFlush();
        Assert.Null(shell.View.MessageActions);
    }

    /// <summary>非光标态 c/p — 走正常字符插入路径进输入框（模态边界守卫）。</summary>
    [Fact]
    public void NonCursorMode_C_And_P_InsertIntoInput()
    {
        var (shell, _, copied) = CreateShell();

        Char(shell, 'c');
        Char(shell, 'p');

        Assert.Equal("cp", shell.Input.Text);
        Assert.Empty(copied);
    }
}
