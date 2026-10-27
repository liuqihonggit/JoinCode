namespace NativeTui.Host.Tests;

/// <summary>
/// TASK065 F2 命令交互桥测试 — TuiCommandCallbacks 的 confirm/prompt 透传链：
/// Bind 延迟绑定容器（对齐 TuiPermissionBridge 形态）→ EngineSession.DispatchSlashCommandAsync
/// → SlashCommandRunner.RunAsync → 命令侧 context.Confirm/context.Prompt 消费。
/// /exit 真实命令驱动（context.Confirm 非空走确认路径：拒绝 Continue / 同意 Exit）— fake DI 纯管道，
/// 无 TUI 无 git 副作用；Bind 前回调 null 降级语义（确认默认拒绝）由容器契约断言覆盖。
/// </summary>
[Collection(SlashCommandSerialCollection.CollectionName)]
public sealed class TuiCommandInteractionTests
{
    /// <summary>容器契约 — Bind 前 confirm/prompt 为 null + IsBound false（降级：命令侧 ?.Invoke 安全跳过）。</summary>
    [Fact]
    public void TuiCommandCallbacks_BeforeBind_ConfirmPromptAreNull()
    {
        var callbacks = new TuiCommandCallbacks();

        Assert.Null(callbacks.Confirm);
        Assert.Null(callbacks.Prompt);
        Assert.False(callbacks.IsBound);
    }

    /// <summary>容器契约 — Bind 后 confirm/prompt 委托同引用暴露 + 调用透传生效。</summary>
    [Fact]
    public void TuiCommandCallbacks_AfterBind_ExposesConfirmPromptDelegates()
    {
        var callbacks = new TuiCommandCallbacks();
        var confirmedMessage = string.Empty;
        var promptedMessage = string.Empty;
        Func<string, bool> confirm = message =>
        {
            confirmedMessage = message;
            return true;
        };
        Func<string, string?> prompt = message =>
        {
            promptedMessage = message;
            return "输入结果";
        };

        callbacks.BindInteractions(confirm: confirm, prompt: prompt, readPassword: null, beginSessionPicker: null);

        Assert.Same(confirm, callbacks.Confirm);
        Assert.Same(prompt, callbacks.Prompt);
        Assert.True(callbacks.IsBound);
        Assert.True(callbacks.Confirm!("确认吗"));
        Assert.Equal("确认吗", confirmedMessage);
        Assert.Equal("输入结果", callbacks.Prompt!("输入"));
        Assert.Equal("输入", promptedMessage);
    }

    /// <summary>/exit 拒绝路径 — confirm 回调收到命令确认消息并返回 false → 命令 Continue（不退出）。</summary>
    [Fact]
    public async Task Dispatch_Exit_ConfirmDeclined_StaysRunning()
    {
        using var services = CommandTestHarness.BuildFakeServices(new CommandTestHarness.FakeChatService());
        var received = new List<string>();
        var exitRequested = false;
        var callbacks = new TuiCommandCallbacks();
        callbacks.Bind(
            clearScreen: null,
            onExitRequested: () => exitRequested = true);
        callbacks.BindInteractions(
            confirm: message =>
            {
                received.Add(message);
                return false;
            },
            prompt: null,
            readPassword: null,
            beginSessionPicker: null);
        var consumer = new InProcessEventConsumer();

        await EngineSession.DispatchSlashCommandAsync(services, "/exit", consumer, callbacks, CancellationToken.None);

        Assert.Contains("确定要退出吗？", received);
        Assert.False(exitRequested, "确认拒绝后 /exit 不应触发退出信号");
    }

    /// <summary>/exit 同意路径 — confirm 返回 true → ShouldContinue=false → onExitRequested 触发。</summary>
    [Fact]
    public async Task Dispatch_Exit_ConfirmAccepted_InvokesExitRequested()
    {
        using var services = CommandTestHarness.BuildFakeServices(new CommandTestHarness.FakeChatService());
        var exitRequested = false;
        var callbacks = new TuiCommandCallbacks();
        callbacks.Bind(
            clearScreen: null,
            onExitRequested: () => exitRequested = true);
        callbacks.BindInteractions(
            confirm: static _ => true,
            prompt: null,
            readPassword: null,
            beginSessionPicker: null);

        await EngineSession.DispatchSlashCommandAsync(services, "/exit", new InProcessEventConsumer(), callbacks, CancellationToken.None);

        Assert.True(exitRequested, "确认同意后 /exit 应触发退出信号");
    }

    /// <summary>Bind 前降级 — confirm 未绑定时 /exit 走既有降级路径（非交互直退）不抛异常。</summary>
    [Fact]
    public async Task Dispatch_Exit_UnboundConfirm_FallsBackSafely()
    {
        using var services = CommandTestHarness.BuildFakeServices(new CommandTestHarness.FakeChatService());
        var callbacks = new TuiCommandCallbacks();
        var consumer = new InProcessEventConsumer();

        await EngineSession.DispatchSlashCommandAsync(services, "/exit", consumer, callbacks, CancellationToken.None);

        Assert.Null(callbacks.Confirm);
    }
}
