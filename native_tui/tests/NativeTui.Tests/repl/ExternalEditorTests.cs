namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell ctrl+g 外部编辑器测试（TASK064 P3 — 基准 defaultBindings.ts:83-84
/// ctrl+g/ctrl+x ctrl+e → chat:externalEditor → $EDITOR）：委托调用触发（原文透传 +
/// 会话标记置位）、空输入无操作、编辑结果回填（输入未变）、输入已变丢弃（用户编辑优先）、
/// null 失败降级丢弃、会话进行中防重入、未注入委托降级无操作。
/// 假编辑器 = TaskCompletionSource 可控委托（Host 侧 ExternalEditorLauncher 进程依赖
/// 不在 Core 测试覆盖 — 登记遗留见任务报告）。构造形态对齐 StashInputTests。
/// </summary>
public sealed class ExternalEditorTests
{
    /// <summary>可控假编辑器 — 记录调用原文，TCS 控制编辑完成时机与结果。</summary>
    private sealed class FakeEditor
    {
        public List<string> Received { get; } = [];

        public TaskCompletionSource<string?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> EditAsync(string text)
        {
            Received.Add(text);
            return Completion.Task;
        }
    }

    /// <summary>构造测试 shell（可选注入假编辑器委托）。</summary>
    private static (ReplShell Shell, FakeEditor Editor) CreateShell(bool injectEditor = true)
    {
        var editor = new FakeEditor();
        var shell = new ReplShell(
            new FakeReplEngine(),
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            launchExternalEditor: injectEditor ? editor.EditAsync : null);
        shell.SeedContent();
        return (shell, editor);
    }

    /// <summary>ctrl+g 注入（ConsoleKeyInfo 形态 — ConsoleKeyAdapter 归一 Ctrl+字母）。</summary>
    private static void CtrlG(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('g', ConsoleKey.G, shift: false, alt: false, control: true));

    /// <summary>逐字符打字注入（形态对齐 StashInputTests.Type）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.A, shift: false, alt: false, control: false));
        }
    }

    /// <summary>有输入 ctrl+g — 委托收到当前输入原文 + 会话标记置位 + 任务句柄就绪。</summary>
    [Fact]
    public void CtrlG_WithText_InvokesEditorWithCurrentInput()
    {
        var (shell, editor) = CreateShell();
        Type(shell, "hello draft");

        CtrlG(shell);

        Assert.Equal(["hello draft"], editor.Received);
        Assert.True(shell.ExternalEditorActive);
        Assert.NotNull(shell.ExternalEditorTask);
    }

    /// <summary>空输入 ctrl+g — 无操作（委托不调用、无会话标记）。</summary>
    [Fact]
    public void CtrlG_EmptyInput_IsNoOp()
    {
        var (shell, editor) = CreateShell();

        CtrlG(shell);

        Assert.Empty(editor.Received);
        Assert.False(shell.ExternalEditorActive);
        Assert.Null(shell.ExternalEditorTask);
    }

    /// <summary>编辑完成且输入框未变 — 编辑结果回填输入框 + 会话标记复位。</summary>
    [Fact]
    public async Task CtrlG_EditorResult_FillsInput_WhenUnchanged()
    {
        var (shell, editor) = CreateShell();
        Type(shell, "draft");
        CtrlG(shell);

        editor.Completion.SetResult("edited by external editor");
        await shell.ExternalEditorTask!;
        shell.DrainEngine();

        Assert.Equal("edited by external editor", shell.Input.Text);
        Assert.False(shell.ExternalEditorActive);
    }

    /// <summary>编辑完成但输入框已变（编辑期间用户继续打字）— 结果丢弃防覆盖用户编辑。</summary>
    [Fact]
    public async Task CtrlG_EditorResult_Discarded_WhenInputChanged()
    {
        var (shell, editor) = CreateShell();
        Type(shell, "draft");
        CtrlG(shell);

        // 编辑器会话期间用户继续输入（fire-and-forget 不冻屏 — 输入框仍可编辑）
        Type(shell, " more");

        editor.Completion.SetResult("edited result");
        await shell.ExternalEditorTask!;
        shell.DrainEngine();

        Assert.Equal("draft more", shell.Input.Text);
        Assert.False(shell.ExternalEditorActive);
    }

    /// <summary>编辑器失败/取消（null 结果）— 丢弃不打扰输入框 + 会话标记复位。</summary>
    [Fact]
    public async Task CtrlG_NullResult_DiscardedAndClearsActive()
    {
        var (shell, editor) = CreateShell();
        Type(shell, "draft");
        CtrlG(shell);

        editor.Completion.SetResult(null);
        await shell.ExternalEditorTask!;
        shell.DrainEngine();

        Assert.Equal("draft", shell.Input.Text);
        Assert.False(shell.ExternalEditorActive);
    }

    /// <summary>编辑会话进行中再按 ctrl+g — 防重入（委托只调用一次，不并发开两个编辑器）。</summary>
    [Fact]
    public void CtrlG_WhileActive_IgnoresSecondInvocation()
    {
        var (shell, editor) = CreateShell();
        Type(shell, "draft");
        CtrlG(shell);
        Type(shell, "2");

        CtrlG(shell);

        Assert.Single(editor.Received, "draft");
        Assert.True(shell.ExternalEditorActive);
    }

    /// <summary>未注入编辑器委托 — ctrl+g 降级无操作（不崩溃、不修改输入）。</summary>
    [Fact]
    public void CtrlG_WithoutDelegate_IsNoOp()
    {
        var (shell, _) = CreateShell(injectEditor: false);
        Type(shell, "draft");

        CtrlG(shell);

        Assert.Equal("draft", shell.Input.Text);
        Assert.False(shell.ExternalEditorActive);
        Assert.Null(shell.ExternalEditorTask);
    }

    /// <summary>编辑结果为空串（用户在编辑器里清空文本）— 合法编辑形态，回填清空输入框。</summary>
    [Fact]
    public async Task CtrlG_EmptyEditedText_ClearsInput()
    {
        var (shell, editor) = CreateShell();
        Type(shell, "draft");
        CtrlG(shell);

        editor.Completion.SetResult(string.Empty);
        await shell.ExternalEditorTask!;
        shell.DrainEngine();

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.False(shell.ExternalEditorActive);
    }
}
