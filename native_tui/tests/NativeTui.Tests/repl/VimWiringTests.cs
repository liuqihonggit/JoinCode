// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace NativeTui.Tests.Repl;

/// <summary>
/// vim 模式 ReplShell 接线测试（TASK066 板块 E 连线 E3 — 基准 useVimInput.ts +
/// useCancelRequest.ts 双层证据的接线行为契约，Harness 模式经 HandleKey 全链注入）：
/// 默认关闭零破坏（Esc 双击清输入链不变）；开启后 INSERT 透传原编辑链（footer -- INSERT --
/// 投影）、INSERT 态 Esc 切 NORMAL 不清输入不打断（useVimInput.ts:192-195 +
/// useCancelRequest.ts:146 INSERT 态打断链不激活）、NORMAL 态 Esc 忙时落打断链
///（useCancelRequest.ts:150-154）闲时不清输入（基准 Esc 清输入双击链在 useTextInput.ts:320-331
/// base handler — vim 层 Esc 永不透传死路）、NORMAL 态 h/l 移动光标 + dw 删词 + Enter
/// 直接提交（useVimInput.ts:203-207 allows submission from NORMAL）+ u 撤销 + 方向键
/// 历史导航透传、模态挂起时 vim 不劫持（权限对话框数字键照常路由）。
/// </summary>
public sealed class VimWiringTests
{
    /// <summary>可控时钟 — 逐击推进（每击 +100ms，双击窗口 800ms 内 — 对齐 EscInterruptClosureTests 先例）。</summary>
    private sealed class ManualClock
    {
        private long _ms;
        public long Tick() => _ms += 100;
    }

    /// <summary>构造 vim 关闭 shell（默认参数 — 零行为变化契约）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"), new StringWriter());
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>构造 vim 开启 shell（vimMode: true — E3 接线形态）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine, VimEngine Vim) CreateVimShell()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"), new StringWriter(), vimMode: true);
        shell.SeedContent();
        return (shell, engine, shell.Vim!);
    }

    /// <summary>模拟键入小写文本（经 HandleKey 全链 — 触发建议刷新与 vim 记账链）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false));
        }
    }

    /// <summary>按单字符（NORMAL 命令键注入）。</summary>
    private static void Press(ReplShell shell, char c) =>
        shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false));

    /// <summary>Esc 键。</summary>
    private static ConsoleKeyInfo Esc() => new('\0', ConsoleKey.Escape, false, false, false);

    /// <summary>Enter 键（裸回车 — 提交语义）。</summary>
    private static ConsoleKeyInfo Enter() => new('\r', ConsoleKey.Enter, false, false, false);

    /// <summary>默认关闭 — 引擎不装配、footer 无 vim 投影、Esc 双击清输入原链不变（零破坏契约）。</summary>
    [Fact]
    public void VimOff_ByDefault_EscapeDoubleClickClearsInput_OriginalChainIntact()
    {
        var (shell, _) = CreateShell();
        Assert.Null(shell.Vim);
        Assert.Null(shell.View.Footer.VimMode);
        Type(shell, "hello");

        var clock = new ManualClock();
        shell.EscClock = clock.Tick;
        shell.HandleKey(Esc());
        shell.HandleKey(Esc());

        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>开启 — INSERT 初始态：键入/退格透传原编辑链（引擎仅记账），footer 投影 -- INSERT --。</summary>
    [Fact]
    public void VimOn_InsertMode_TypingPassesThrough_FooterShowsInsert()
    {
        var (shell, _, vim) = CreateVimShell();
        Assert.Equal(VimMode.Insert, vim.Mode);
        Assert.Equal("-- INSERT --", shell.View.Footer.VimMode);

        Type(shell, "hello");
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Backspace, false, false, false));

        Assert.Equal("hell", shell.Input.Text);
        Assert.Equal("hell", vim.Text);
        Assert.Equal("-- INSERT --", shell.View.Footer.VimMode);
    }

    /// <summary>INSERT 态 Esc — vim 消费切 NORMAL（useVimInput.ts:192-195）：不清输入、
    /// 光标左移一格（useVimInput.ts:73-75）、footer 投影清除。</summary>
    [Fact]
    public void VimOn_EscapeInInsert_SwitchesToNormal_KeepsInputAndMovesCursorLeft()
    {
        var (shell, _, vim) = CreateVimShell();
        Type(shell, "hello");

        shell.HandleKey(Esc());

        Assert.Equal(VimMode.Normal, vim.Mode);
        Assert.Equal("hello", shell.Input.Text);
        Assert.Equal(4, shell.Input.CursorCol);
        Assert.Null(shell.View.Footer.VimMode);
    }

    /// <summary>NORMAL 态 i — 重新进 INSERT（基准 EnterInsert）：footer 投影刷新、光标原位。</summary>
    [Fact]
    public void VimOn_IInNormal_ReentersInsert_FooterRefreshes()
    {
        var (shell, _, vim) = CreateVimShell();
        Type(shell, "hi");
        shell.HandleKey(Esc());
        Assert.Null(shell.View.Footer.VimMode);

        Press(shell, 'i');

        Assert.Equal(VimMode.Insert, vim.Mode);
        Assert.Equal("-- INSERT --", shell.View.Footer.VimMode);
        Assert.Equal(1, shell.Input.CursorCol);
    }

    /// <summary>NORMAL 态 h/l — 镜像光标移动同步到 InputBuffer 光标（MoveCursor 效果接线）。</summary>
    [Fact]
    public void VimOn_NormalMode_Hl_MoveInputCursor()
    {
        var (shell, _, vim) = CreateVimShell();
        Type(shell, "hello world");
        shell.HandleKey(Esc());
        Assert.Equal(10, shell.Input.CursorCol);

        Press(shell, 'h');
        Assert.Equal(9, shell.Input.CursorCol);
        Assert.Equal(9, vim.Cursor);

        Press(shell, 'l');
        Assert.Equal(10, shell.Input.CursorCol);
        Assert.Equal("hello world", shell.Input.Text);
    }

    /// <summary>NORMAL 态 dw — 删词写回 InputBuffer（ApplyText 效果接线 — 镜像→真实缓冲同步）。</summary>
    [Fact]
    public void VimOn_NormalMode_Dw_DeletesToWordEnd()
    {
        var (shell, _, vim) = CreateVimShell();
        Type(shell, "foo bar");
        shell.HandleKey(Esc());
        Assert.Equal(6, shell.Input.CursorCol);

        Press(shell, 'd');
        Assert.Equal(VimCommandKind.Operator, vim.Command.Kind);
        Press(shell, 'w');

        Assert.Equal("foo ba", shell.Input.Text);
        Assert.Equal("foo ba", vim.Text);
        // 删 'r' 后文本长 6，光标钳到新文末 5（vim dw 词尾删除同语义 — 原期望 6 越界笔误）
        Assert.Equal(5, shell.Input.CursorCol);
    }

    /// <summary>NORMAL 态 Enter — 直接提交（基准 useVimInput.ts:203-207
    /// "Pass Enter to base handler regardless of mode (allows submission from NORMAL)"）。</summary>
    [Fact]
    public void VimOn_EnterInNormalMode_SubmitsDirectly()
    {
        var (shell, engine, _) = CreateVimShell();
        Type(shell, "hello");
        shell.HandleKey(Esc());

        shell.HandleKey(Enter());

        Assert.Equal(["hello"], engine.Submits);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>忙时 Esc 分流（useCancelRequest.ts:146/150-154）：INSERT 态 Esc 只切模式不打断
    ///（isContextActive 排除 INSERT 态）；切 NORMAL 后忙时 Esc 落打断链单击打断。</summary>
    [Fact]
    public void VimOn_BusyEscape_InsertOnlySwitchesMode_ThenNormalInterrupts()
    {
        var (shell, engine, vim) = CreateVimShell();
        Type(shell, "hello");
        shell.HandleKey(Enter());
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
        Assert.True(shell.IsBusy);

        shell.HandleKey(Esc());
        Assert.Equal(VimMode.Normal, vim.Mode);
        Assert.Equal(0, engine.Interrupts);

        shell.HandleKey(Esc());
        Assert.Equal(1, engine.Interrupts);
    }

    /// <summary>闲时 NORMAL 态 Esc — 不清输入（基准 Esc 清输入双击链在 useTextInput.ts:320-331
    /// base handler，vim 层 Esc 永不透传 → 死路；vim 用户 Esc 只取消 pending 命令）。</summary>
    [Fact]
    public void VimOn_IdleNormalEscape_DoesNotClearInput()
    {
        var (shell, _, _) = CreateVimShell();
        Type(shell, "draft text");
        shell.HandleKey(Esc());

        shell.HandleKey(Esc());
        shell.HandleKey(Esc());

        Assert.Equal("draft text", shell.Input.Text);
    }

    /// <summary>NORMAL 态上下键 — 透传历史导航（useVimInput.ts:235-243 idle 方向键委托宿主）
    /// + 程序化填充后 vim 镜像刷新（SyncFrom 通道）。</summary>
    [Fact]
    public void VimOn_NormalMode_UpArrow_HistoryNavigation_MirrorSynced()
    {
        var (shell, _, vim) = CreateVimShell();
        shell.SeedHistory(["hist"]);
        shell.HandleKey(Esc());
        Assert.Equal(VimMode.Normal, vim.Mode);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));

        Assert.Equal("hist", shell.Input.Text);
        Assert.Equal("hist", vim.Text);
    }

    /// <summary>NORMAL 态无效键 — vim 吞掉不进输入缓冲（基准 transition 无效键双空分支）。</summary>
    [Fact]
    public void VimOn_NormalMode_InvalidKeySwallowed()
    {
        var (shell, _, _) = CreateVimShell();
        Type(shell, "abc");
        shell.HandleKey(Esc());

        Press(shell, 'z');

        Assert.Equal("abc", shell.Input.Text);
    }

    /// <summary>NORMAL 态 u — 撤销 vim 编辑（UndoRequested 效果 → 现有 _input.Undo() 链，
    /// 基准 onUndo 委托 transitions.ts:165-167；vim 编辑经 ReplaceAt 压快照可撤销）。</summary>
    [Fact]
    public void VimOn_NormalMode_U_UndoesVimEdit()
    {
        var (shell, _, _) = CreateVimShell();
        var clock = 10_000L;
        shell.Input.UndoClock = () => clock;
        Type(shell, "ab");
        shell.HandleKey(Esc());

        Press(shell, 'x');
        Assert.Equal("a", shell.Input.Text);

        clock += 2000;
        Press(shell, 'u');
        Assert.Equal("ab", shell.Input.Text);
    }

    /// <summary>模态挂起时 vim 不劫持 — 权限对话框数字键照常路由（分派链模态优先于 vim 插位；
    /// 基准 overlay/权限对话框优先于输入组件 vim 层）。</summary>
    [Fact]
    public void VimOn_PermissionDialogPending_NumberKeyNotHijacked()
    {
        var (shell, _, vim) = CreateVimShell();
        shell.HandleKey(Esc());
        Assert.Equal(VimMode.Normal, vim.Mode);

        shell.BeginPermissionRequest("Bash", "rm -rf /");
        Assert.True(shell.Permission.IsPending);

        Press(shell, '1');

        Assert.False(shell.Permission.IsPending);
        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Equal(VimMode.Normal, vim.Mode);
    }
}
