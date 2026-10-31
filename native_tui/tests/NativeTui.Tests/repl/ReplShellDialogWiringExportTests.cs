namespace NativeTui.Tests.Repl;

/// <summary>
/// S12 四域对话框统一接线 — /export 命令拦截域冒烟：
/// 命令拦截挂载（不透传引擎）/ 空会话守卫 / 未知参数诱导提示 / 模态吞键 /
/// enter 确认剪贴板目标（剪贴板委托 + 通知反馈 + 对话框移除）/
/// esc 取消（无 IO + 移除）/ 数字直选文件目标（saveExportAsync 委托 + 文件名通知反馈）/
/// 快照防御拷贝（挂载后滚动区新增消息不进导出文本）。
/// </summary>
public sealed class ReplShellDialogWiringExportTests
{
    /// <summary>构造测试 shell（消息区含一条 assistant 消息 — 导出内容非空）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShellWithMessage(
        Action<string>? clipboard = null,
        Func<string, Task<string?>>? saveExportAsync = null)
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            clipboard: clipboard,
            saveExportAsync: saveExportAsync);
        shell.SeedContent();
        shell.PostInfo("你好世界");
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

    /// <summary>/export 无参：消息快照拍平 + 挂载 ExportDialog + 不透传引擎。</summary>
    [Fact]
    public void ExportCommand_MountsDialog_NoEngineSubmit()
    {
        var (shell, engine) = CreateShellWithMessage();

        Type(shell, "/export");
        shell.Submit();

        Assert.Empty(engine.Submits);
        Assert.True(shell.IsExportDialogOpen);
        Assert.Contains(shell.View.Messages.Items, item => item is ExportDialog);
        Assert.Contains(shell.View.Messages.Items, item => item is UserMessageItem { Text: "/export" });
    }

    /// <summary>/export 空会话守卫：导出全文为空 → 诱导提示不挂载。</summary>
    [Fact]
    public void ExportCommand_EmptyConversation_ShowsGuidance()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();

        Type(shell, "/export");
        shell.Submit();

        Assert.False(shell.IsExportDialogOpen);
        var feedback = shell.View.Messages.Items.OfType<ToolResultItem>().Single();
        Assert.Contains("无可导出", feedback.Summary);
    }

    /// <summary>/export 带参：参数诱导提示（正确写法）不挂载。</summary>
    [Fact]
    public void ExportCommand_WithArgument_ShowsUsageGuidance()
    {
        var (shell, engine) = CreateShellWithMessage();

        Type(shell, "/export file");
        shell.Submit();

        Assert.Empty(engine.Submits);
        Assert.False(shell.IsExportDialogOpen);
        var feedback = shell.View.Messages.Items.OfType<ToolResultItem>().Single();
        Assert.Contains("/export", feedback.Summary);
    }

    /// <summary>模态吞键：对话框激活期间字符不进输入缓冲。</summary>
    [Fact]
    public void ExportDialogOpen_CharKeys_Swallowed()
    {
        var (shell, _) = CreateShellWithMessage();
        Type(shell, "/export");
        shell.Submit();

        Type(shell, "abc");

        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>enter 确认焦点档（初始 0 = 剪贴板）：剪贴板委托收到导出全文 + 通知反馈 + 对话框移除。</summary>
    [Fact]
    public void ExportConfirm_EnterClipboard_InvokesClipboardDelegate()
    {
        var copied = new List<string>();
        var (shell, _) = CreateShellWithMessage(clipboard: copied.Add);
        Type(shell, "/export");
        shell.Submit();

        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.False(shell.IsExportDialogOpen);
        Assert.DoesNotContain(shell.View.Messages.Items, item => item is ExportDialog);
        Assert.Single(copied);
        Assert.Contains("你好世界", copied[0]);
        Assert.NotNull(shell.View.Footer.Notifications!.Current);
    }

    /// <summary>esc 取消：无剪贴板/文件 IO + 对话框移除。</summary>
    [Fact]
    public void ExportCancel_Escape_NoIoAndRemoved()
    {
        var copied = new List<string>();
        var saved = new List<string>();
        var (shell, _) = CreateShellWithMessage(
            clipboard: copied.Add,
            saveExportAsync: text => { saved.Add(text); return Task.FromResult<string?>("conversation-x.md"); });
        Type(shell, "/export");
        shell.Submit();

        shell.HandleKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));

        Assert.False(shell.IsExportDialogOpen);
        Assert.Empty(copied);
        Assert.Empty(saved);
    }

    /// <summary>数字直选 2 = 文件目标：saveExportAsync 委托收到导出全文 + 完成结果消费后通知含文件名。</summary>
    [Fact]
    public async Task ExportDirectSelect_FileTarget_InvokesSaveDelegateWithFeedback()
    {
        var saved = new List<string>();
        var (shell, _) = CreateShellWithMessage(
            saveExportAsync: text => { saved.Add(text); return Task.FromResult<string?>("conversation-20261023-1200.md"); });
        Type(shell, "/export");
        shell.Submit();

        shell.HandleKey(new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false));

        Assert.False(shell.IsExportDialogOpen);
        // fire-and-forget 委托已同步完成入队 — DrainEngine 主循环消费出通知
        await WaitForAsync(() => saved.Count > 0);
        shell.DrainEngine();
        Assert.Contains("你好世界", saved[0]);
        Assert.NotNull(shell.View.Footer.Notifications!.Current);
    }

    /// <summary>剪贴板能力降级：未注入剪贴板委托 → 确认后错误反馈（诱导式）不静默。</summary>
    [Fact]
    public void ExportConfirm_NoClipboardDelegate_ShowsDegradedFeedback()
    {
        var (shell, _) = CreateShellWithMessage();
        Type(shell, "/export");
        shell.Submit();

        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.False(shell.IsExportDialogOpen);
        var feedback = shell.View.Messages.Items.OfType<ToolResultItem>().Single();
        Assert.Contains("剪贴板", feedback.Summary);
    }

    /// <summary>快照防御拷贝：挂载后滚动区新增消息不进导出文本（拍摄即定格）。</summary>
    [Fact]
    public void ExportSnapshot_DefensiveCopy_AgainstLaterMessages()
    {
        var copied = new List<string>();
        var (shell, _) = CreateShellWithMessage(clipboard: copied.Add);
        Type(shell, "/export");
        shell.Submit();

        // 挂载后新消息到达（引擎事件路径等价 — 直投 PostInfo）
        shell.PostInfo("挂载后的新消息");
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Single(copied);
        Assert.Contains("你好世界", copied[0]);
        Assert.DoesNotContain("挂载后的新消息", copied[0]);
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
}
