namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell transcript 收敛集成测试（S6 任务 1 — 新功能面）：
/// verbose 联动（进出 transcript 切 ReplView.SetVerboseMode）、视口行数接终端高度（扣除 footer）、
/// 快照冻结（进入后新消息不影响行）、less 键位（j/k/g/G/ctrl+u/ctrl+d/空格 — Controller 消费）、
/// q 退出、n/N 循环跳转、搜索栏 Esc 回锚、Tick 帧拍累计、分派链位置（建议激活时 ctrl+o 可达）。
/// </summary>
public sealed class ReplShellIntegrationTranscriptTests
{
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell(int rows = 24)
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(
            engine,
            new BannerItem("1.0.0", "m", "b", "/tmp"),
            new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, rows));
        shell.SeedContent();
        return (shell, engine);
    }

    private static void CtrlO(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\u000f', ConsoleKey.O, false, false, false));

    private static void Char(ReplShell shell, char c) =>
        shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.A, char.IsUpper(c), false, false));

    private static void CtrlU(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\u0015', ConsoleKey.U, false, false, true));

    private static void CtrlD(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\u0004', ConsoleKey.D, false, false, true));

    private static void SeedMessages(FakeReplEngine engine, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            engine.Events.Enqueue(new BridgeEvent.AssistantMessage($"回复 {i}", MessageId: i));
        }
    }

    /// <summary>进出 transcript — verbose 联动 ReplView.SetVerboseMode（进入 true / 退出 false）。</summary>
    [Fact]
    public void Transcript_VerboseLinkedToView()
    {
        var (shell, engine) = CreateShell();
        SeedMessages(engine, 1);
        shell.DrainEngine();

        CtrlO(shell);
        Assert.True(shell.View.VerboseMode);
        CtrlO(shell);
        Assert.False(shell.View.VerboseMode);
    }

    /// <summary>视口行数接终端高度 — 24 行终端扣除 footer 1 行 = 23。</summary>
    [Fact]
    public void Transcript_ViewportHeight_TracksTerminalRows()
    {
        var (shell, engine) = CreateShell(rows: 24);
        SeedMessages(engine, 40);
        shell.DrainEngine();

        CtrlO(shell);

        Assert.Equal(24 - TranscriptRenderer.FooterRows, shell.Transcript.ViewportHeight);
        Assert.Equal(shell.Transcript.ViewportHeight, shell.View.Transcript!.ViewportHeight);
    }

    /// <summary>快照冻结 — 进入后到达的新消息不改变冻结行集（防御拷贝隔离 live 增长）。</summary>
    [Fact]
    public void Transcript_SnapshotFrozen_AgainstNewMessages()
    {
        var (shell, engine) = CreateShell();
        SeedMessages(engine, 2);
        shell.DrainEngine();
        CtrlO(shell);
        var frozenCount = shell.Transcript.Snapshot!.LineCount;

        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("迟到消息", MessageId: 99));
        shell.DrainEngine();

        Assert.Equal(frozenCount, shell.Transcript.Snapshot.LineCount);
        Assert.DoesNotContain(shell.Transcript.Snapshot.Lines, static line => line.Contains("迟到消息"));
    }

    /// <summary>流式正文长度投影 — 进入时 StreamingTextItem 当前长度冻结入快照。</summary>
    [Fact]
    public void Transcript_StreamingLength_ProjectsIntoSnapshot()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        engine.Events.Enqueue(new BridgeEvent.TokenDelta(3, "流式中"));
        shell.DrainEngine();

        CtrlO(shell);

        Assert.Equal(3, shell.Transcript.Snapshot!.StreamingLength);
        Assert.Contains(shell.Transcript.Snapshot.Lines, static line => line.Contains("流式中"));
    }

    /// <summary>less 键位 — j/k 逐行滚动（Controller 消费，行偏移变化）。</summary>
    [Fact]
    public void Transcript_JAndK_ScrollByLine()
    {
        var (shell, engine) = CreateShell();
        SeedMessages(engine, 40);
        shell.DrainEngine();
        CtrlO(shell);
        var bottom = shell.Transcript.ScrollOffset;

        Char(shell, 'k');
        Assert.Equal(bottom - 1, shell.Transcript.ScrollOffset);
        Char(shell, 'j');
        Assert.Equal(bottom, shell.Transcript.ScrollOffset);
    }

    /// <summary>less 键位 — g 首部 / G 末尾绝对跳转。</summary>
    [Fact]
    public void Transcript_G_AndShiftG_JumpToEdges()
    {
        var (shell, engine) = CreateShell();
        SeedMessages(engine, 40);
        shell.DrainEngine();
        CtrlO(shell);

        Char(shell, 'g');
        Assert.Equal(0, shell.Transcript.ScrollOffset);
        Char(shell, 'G');
        Assert.Equal(shell.Transcript.MaxScrollOffset, shell.Transcript.ScrollOffset);
    }

    /// <summary>less 键位 — ctrl+u/ctrl+d 半屏滚动（视口/2 向上取整至少 1 行）。</summary>
    [Fact]
    public void Transcript_CtrlUAndCtrlD_HalfPageScroll()
    {
        var (shell, engine) = CreateShell();
        SeedMessages(engine, 40);
        shell.DrainEngine();
        CtrlO(shell);
        var bottom = shell.Transcript.ScrollOffset;

        CtrlU(shell);
        Assert.Equal(bottom - 23 / 2, shell.Transcript.ScrollOffset);
        CtrlD(shell);
        Assert.Equal(bottom, shell.Transcript.ScrollOffset);
    }

    /// <summary>q 退出 — 浏览态 q 直接退出 transcript（对齐基准 transcript:exit ungated）。</summary>
    [Fact]
    public void Transcript_Q_ExitsMode()
    {
        var (shell, engine) = CreateShell();
        SeedMessages(engine, 1);
        shell.DrainEngine();
        CtrlO(shell);

        Char(shell, 'q');

        Assert.False(shell.IsTranscriptMode);
    }

    /// <summary>搜索栏 Esc — 撤销查询并回滚到打开时的锚点（pre-/ 状态恢复）。</summary>
    [Fact]
    public void Transcript_SearchEscape_RollsBackToAnchor()
    {
        var (shell, engine) = CreateShell();
        SeedMessages(engine, 40);
        shell.DrainEngine();
        CtrlO(shell);
        Char(shell, 'g');
        var anchor = shell.Transcript.ScrollOffset;

        Char(shell, '/');
        Char(shell, '回');
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.Equal(anchor, shell.Transcript.ScrollOffset);
        Assert.Equal(string.Empty, shell.Transcript.Search.Query);
    }

    /// <summary>n/N 循环跳转 — 顶部起搜（g 首部）提交后 n 步进到下一匹配（行偏移随匹配行移动）。</summary>
    [Fact]
    public void Transcript_N_StepsToNextMatch()
    {
        var (shell, engine) = CreateShell();
        for (var i = 1; i <= 40; i++)
        {
            engine.Events.Enqueue(new BridgeEvent.AssistantMessage(i % 5 == 0 ? $"目标 {i}" : $"普通 {i}", MessageId: i));
        }
        shell.DrainEngine();
        CtrlO(shell);
        Char(shell, 'g');

        Char(shell, '/');
        foreach (var c in "目标")
        {
            Char(shell, c);
        }
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        var first = shell.Transcript.ScrollOffset;

        Char(shell, 'n');

        Assert.NotEqual(first, shell.Transcript.ScrollOffset);
        Assert.True(shell.Transcript.Search.CurrentOrdinal >= 1);
    }

    /// <summary>Tick 帧拍累计 — RenderAndFlush 每拍 +50ms（激活期间）。</summary>
    [Fact]
    public void Transcript_Tick_AccumulatesPerFrame()
    {
        var (shell, engine) = CreateShell();
        SeedMessages(engine, 1);
        shell.DrainEngine();
        CtrlO(shell);

        shell.RenderAndFlush();
        shell.RenderAndFlush();

        Assert.Equal(2 * 50, shell.Transcript.UptimeMs);
    }

    /// <summary>分派链位置 — transcript 激活时字符 q 不进输入框（模态独占，编辑域不可达）。</summary>
    [Fact]
    public void Transcript_ModalSwallows_EditKeysUnreachable()
    {
        var (shell, engine) = CreateShell();
        SeedMessages(engine, 1);
        shell.DrainEngine();
        CtrlO(shell);

        Char(shell, 'x');
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Equal(string.Empty, shell.View.Input);
    }
}
