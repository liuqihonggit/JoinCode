namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell 键位表接线测试（TASK066 板块 B 连线 B4 — 全局键层走 KeybindingProfile 数据源）：
/// 默认 profile 行为等价（ctrl+o transcript 双向 / ctrl+l 重绘 / ctrl+g 编辑器 / ctrl+x ctrl+e
/// 序列 — 零行为变化兜底）；用户表覆盖生效（transcript:toggle 改 ctrl+t / chat:cancel 改
/// ctrl+j — 旧键失效新键触发，核心价值验证）；解绑生效（transcript:toggle 解绑后 ctrl+o 无效）；
/// 序列键数据源（chat:externalEditor 改绑 ctrl+q ctrl+w 走新序列、旧 ctrl+x ctrl+e 失效）；
/// 保留键不受影响（ctrl+c 仍是复制 / ctrl+d 仍是双击退出 — ReservedShortcuts 退出安全语义）。
/// Harness 形态对齐 TerminalStatusWiringTests / ExternalEditorTests
///（FakeReplEngine + FakeTerminalSizeSource + 可选委托注入）。
/// </summary>
public sealed class KeybindingWiringTests
{
    /// <summary>可控假编辑器 — 记录调用原文（TCS 控制完成时机，形态对齐 ExternalEditorTests.FakeEditor）。</summary>
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

    /// <summary>构造测试 shell（可选注入键位表/剪贴板/外部编辑器委托 — keybindings null = 默认表）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine, StringWriter Output) CreateShell(
        KeybindingProfile? keybindings = null,
        Action<string>? clipboard = null,
        Func<string, Task<string?>>? launchExternalEditor = null)
    {
        var engine = new FakeReplEngine();
        var output = new StringWriter();
        var shell = new ReplShell(
            engine,
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            output,
            sizeSource: new FakeTerminalSizeSource(80, 24),
            clipboard: clipboard,
            launchExternalEditor: launchExternalEditor,
            keybindings: keybindings);
        shell.SeedContent();
        return (shell, engine, output);
    }

    /// <summary>默认表改绑构造 — 移除指定 action 全部默认行 + 追加用户行（模拟用户 json 整体替换语义：
    /// KeybindingLoader 对同 (context, action) 整体替换；同键被其他 action 占用时需一并移除 —
    /// MatchAction 首命中语义，真实用户 json 改绑应让键独占）。</summary>
    /// <param name="remove">要移除默认行的 action 集合。</param>
    /// <param name="add">追加的用户绑定行。</param>
    /// <returns>改绑后的 profile。</returns>
    private static KeybindingProfile Remap(
        IReadOnlyList<KeybindingAction> remove,
        params (KeybindingContext Context, KeybindingAction Action, string Keys)[] add)
    {
        var rows = KeybindingProfile.Default.Rows
            .Where(row => !remove.Contains(row.Action))
            .Select(row => (row.Context, row.Action, row.Chord));
        rows = rows.Concat(add.Select(entry => (entry.Context, entry.Action, KeyChord.Parse(entry.Keys))));
        return KeybindingProfile.Of(rows);
    }

    /// <summary>ctrl+字母注入（ConsoleKeyInfo 形态 — ConsoleKeyAdapter 归一 Ctrl+字母）。</summary>
    private static void Ctrl(ReplShell shell, char letter, ConsoleKey key) =>
        shell.HandleKey(new ConsoleKeyInfo(letter, key, shift: false, alt: false, control: true));

    /// <summary>Esc 注入。</summary>
    private static void Esc(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

    /// <summary>逐字符打字注入（形态对齐 StashInputTests.Type）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.A, shift: false, alt: false, control: false));
        }
    }

    // ---- 默认 profile 行为等价（零行为变化兜底）----

    /// <summary>默认表 ctrl+o — transcript 开（进入键走表）+ 再按关闭（模态内退出键同源表驱动）。</summary>
    [Fact]
    public void DefaultProfile_CtrlO_TogglesTranscriptBothWays()
    {
        var (shell, _, _) = CreateShell();

        Ctrl(shell, 'o', ConsoleKey.O);
        Assert.True(shell.IsTranscriptMode);

        Ctrl(shell, 'o', ConsoleKey.O);
        Assert.False(shell.IsTranscriptMode);
    }

    /// <summary>默认表 ctrl+l — 前缓冲失效 + prevOverride 丢弃（断言对齐 ReplShellIntegrationRepaintTests）。</summary>
    [Fact]
    public void DefaultProfile_CtrlL_InvalidatesFrontBuffer()
    {
        var (shell, _, output) = CreateShell();
        shell.RenderAndFlush();

        Ctrl(shell, 'l', ConsoleKey.L);

        Assert.Null(shell.PrevOverride);
        var lengthBefore = output.ToString().Length;
        shell.RenderAndFlush();
        Assert.True(output.ToString().Length > lengthBefore, "强制重绘帧应产生全量输出");
    }

    /// <summary>默认表 ctrl+g — 外部编辑器触发（委托收到当前输入原文）。</summary>
    [Fact]
    public void DefaultProfile_CtrlG_LaunchesExternalEditor()
    {
        var editor = new FakeEditor();
        var (shell, _, _) = CreateShell(launchExternalEditor: editor.EditAsync);
        Type(shell, "hello draft");

        Ctrl(shell, 'g', ConsoleKey.G);

        Assert.Equal(["hello draft"], editor.Received);
    }

    /// <summary>默认表 ctrl+x ctrl+e 序列 — 编辑器触发（序列键默认行为兜底：首键仅置前缀、第二键消费）。</summary>
    [Fact]
    public void DefaultProfile_CtrlXCtrlE_Sequence_LaunchesExternalEditor()
    {
        var editor = new FakeEditor();
        var (shell, _, _) = CreateShell(launchExternalEditor: editor.EditAsync);
        Type(shell, "draft");

        Ctrl(shell, 'x', ConsoleKey.X);
        Assert.Empty(editor.Received); // 首键仅置前缀等待
        Ctrl(shell, 'e', ConsoleKey.E);

        Assert.Equal(["draft"], editor.Received);
    }

    /// <summary>默认表 esc — 清输入（chat:cancel 识别走表的默认等价）。</summary>
    [Fact]
    public void DefaultProfile_Escape_ClearsInput()
    {
        var (shell, _, _) = CreateShell();
        Type(shell, "draft");

        Esc(shell);

        Assert.Equal(string.Empty, shell.Input.Text);
    }

    // ---- 用户表覆盖生效（核心价值验证）----

    /// <summary>transcript:toggle 改绑 ctrl+t — ctrl+o 不再触发、ctrl+t 触发。
    /// 同步移除 app:toggleTodos 默认 ctrl+t（同键冲突 — MatchAction 首命中语义）。</summary>
    [Fact]
    public void CustomProfile_TranscriptToggle_RemappedToCtrlT()
    {
        var profile = Remap(
            [KeybindingAction.AppToggleTranscript, KeybindingAction.AppToggleTodos],
            (KeybindingContext.Global, KeybindingAction.AppToggleTranscript, "ctrl+t"));
        var (shell, _, _) = CreateShell(keybindings: profile);

        Ctrl(shell, 'o', ConsoleKey.O);
        Assert.False(shell.IsTranscriptMode); // 旧键失效

        Ctrl(shell, 't', ConsoleKey.T);
        Assert.True(shell.IsTranscriptMode); // 新键承接

        Ctrl(shell, 't', ConsoleKey.T);
        Assert.False(shell.IsTranscriptMode); // 模态内退出键同源改绑
    }

    /// <summary>chat:cancel 改绑 ctrl+j — esc 不再清输入（表识别失效落 no-op）、
    /// ctrl+j 清输入（esc 键识别走表的覆盖验证 — 语义分支在 HandleEscape 内保持）。</summary>
    [Fact]
    public void CustomProfile_ChatCancel_RemappedToCtrlJ()
    {
        var profile = Remap(
            [KeybindingAction.ChatCancel],
            (KeybindingContext.Chat, KeybindingAction.ChatCancel, "ctrl+j"));
        var (shell, _, _) = CreateShell(keybindings: profile);
        Type(shell, "draft");

        Esc(shell);
        Assert.Equal("draft", shell.Input.Text); // esc 不再触发清输入

        Ctrl(shell, 'j', ConsoleKey.J);
        Assert.Equal(string.Empty, shell.Input.Text); // ctrl+j 承接清输入
    }

    // ---- 解绑生效 ----

    /// <summary>transcript:toggle 解绑（移除全部默认行无追加）— ctrl+o 无效（无触发无异常）。</summary>
    [Fact]
    public void UnboundProfile_TranscriptToggle_CtrlO_IsDead()
    {
        var profile = Remap([KeybindingAction.AppToggleTranscript]);
        var (shell, _, _) = CreateShell(keybindings: profile);

        Ctrl(shell, 'o', ConsoleKey.O);

        Assert.False(shell.IsTranscriptMode);
        Assert.True(shell.IsRunning); // 解绑是 no-op — 不误伤退出安全
    }

    // ---- 序列键数据源 ----

    /// <summary>chat:externalEditor 序列改绑 ctrl+q ctrl+w — 新序列触发编辑器
    ///（首键仅置前缀无触发，第二键 MatchSequence 消费分派）。</summary>
    [Fact]
    public void CustomProfile_ExternalEditorSequence_RemappedToCtrlQCtrlW()
    {
        var editor = new FakeEditor();
        var profile = Remap(
            [KeybindingAction.ChatExternalEditor],
            (KeybindingContext.Chat, KeybindingAction.ChatExternalEditor, "ctrl+q ctrl+w"));
        var (shell, _, _) = CreateShell(keybindings: profile, launchExternalEditor: editor.EditAsync);
        Type(shell, "draft");

        Ctrl(shell, 'q', ConsoleKey.Q);
        Assert.Empty(editor.Received); // 新序列首键仅置前缀等待
        Ctrl(shell, 'w', ConsoleKey.W);

        Assert.Equal(["draft"], editor.Received); // 新序列完整触发
    }

    /// <summary>旧序列 ctrl+x ctrl+e 在改绑后失效 — ctrl+x 不再是序列首键（置位逻辑数据源化）、
    /// ctrl+e 保持 End() 行尾编辑语义（文本不变、编辑器不触发）。</summary>
    [Fact]
    public void CustomProfile_OldSequence_CtrlXCtrlE_IsDead()
    {
        var editor = new FakeEditor();
        var profile = Remap(
            [KeybindingAction.ChatExternalEditor],
            (KeybindingContext.Chat, KeybindingAction.ChatExternalEditor, "ctrl+q ctrl+w"));
        var (shell, _, _) = CreateShell(keybindings: profile, launchExternalEditor: editor.EditAsync);
        Type(shell, "dr");

        Ctrl(shell, 'x', ConsoleKey.X);
        Ctrl(shell, 'e', ConsoleKey.E);

        Assert.Empty(editor.Received); // 旧序列失效
        Assert.Equal("dr", shell.Input.Text); // 单独 ctrl+e = 行尾（文本不变）
    }

    // ---- 保留键不受影响 ----

    /// <summary>保留键 ctrl+c — 表驱动下仍是复制最后 assistant 消息
    ///（app:interrupt 不接管，TASK059 退出安全语义保留）。</summary>
    [Fact]
    public void ReservedKey_CtrlC_StillCopies()
    {
        var copied = new List<string>();
        var (shell, engine, _) = CreateShell(clipboard: copied.Add);
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("最后一条回答"));
        shell.DrainEngine();

        shell.HandleKey(new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, false));

        Assert.Equal(["最后一条回答"], copied);
        Assert.True(shell.IsRunning);
    }

    /// <summary>保留键 ctrl+d — 空输入双击（确认窗口内）仍退出
    ///（app:exit 表驱动识别 + HandleExitCtrlD 双击窗口逻辑保留）。</summary>
    [Fact]
    public void ReservedKey_CtrlD_DoublePress_StillExits()
    {
        var (shell, _, _) = CreateShell();

        Ctrl(shell, 'd', ConsoleKey.D);
        Assert.True(shell.IsRunning); // 首击提示"再按一次"

        Ctrl(shell, 'd', ConsoleKey.D);
        Assert.False(shell.IsRunning); // 窗口内二击退出
    }

    /// <summary>保留键 ctrl+d 有文本 — 仍是删除字符不退出（空输入门槛在 AppExit 分支保留；
    /// Delete 向右删，光标在文本尾无字符可删 — 文本保留，仅验证不误触发退出链）。</summary>
    [Fact]
    public void ReservedKey_CtrlD_WithText_DeletesNotExits()
    {
        var (shell, _, _) = CreateShell();
        Type(shell, "a");

        Ctrl(shell, 'd', ConsoleKey.D);

        Assert.True(shell.IsRunning);
        Assert.Equal("a", shell.Input.Text);
    }
}
