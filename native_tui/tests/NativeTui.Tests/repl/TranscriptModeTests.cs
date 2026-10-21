namespace NativeTui.Tests.Repl;

/// <summary>
/// ctrl+o 全局 transcript 模式测试（M8-3 — TASK064，基准 app:toggleTranscript；S6 收敛后
/// 语义等价迁移：S5 TranscriptController 唯一引擎 — 快照行投影/视口滚动/搜索状态机经
/// Controller 驱动，ReplShell 只做分派与渲染投影接线）：
/// ctrl+o 双向切换 prompt↔transcript 全屏模态；
/// 进入 = 快照拍摄（CoT 全文行投影 — verbose）+ ReplView.Transcript 挂载（整屏模态）+ verbose 联动开；
/// 退出 = 快照丢弃 + Transcript 卸载 + verbose 联动关；
/// transcript 态吞全部按键（j/k/g/G/ctrl+u/ctrl+d 滚动、/ 搜索、ctrl+o 退出 — 字符不进输入框）；
/// / 搜索：字符增量查询（每键就近预览跳转）+ Esc 撤销回锚点。
/// </summary>
public sealed class TranscriptModeTests
{
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell() {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"), new StringWriter(), sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();
        return (shell, engine);
    }

    private static void CtrlO(ReplShell shell) =>
        shell.HandleKey(new ConsoleKeyInfo('\x0f', ConsoleKey.O, false, false, false));

    private static void Type(ReplShell shell, string text) {
        foreach (var ch in text) {
            shell.HandleKey(new ConsoleKeyInfo(ch, ConsoleKey.A, false, false, false));
        }
    }

    /// <summary>两条带 CoT 消息 → ctrl+o 进入 transcript → 快照行含 CoT 全文（verbose 投影）+ footer 提示。</summary>
    [Fact]
    public void CtrlO_EntersTranscriptMode_AllCoTExpanded_FooterHint()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("第一条", MessageId: 1, ThinkingDurationMs: 100, ThinkingContent: "CoT1"));
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("第二条", MessageId: 2, ThinkingDurationMs: 200, ThinkingContent: "CoT2"));
        shell.DrainEngine();

        CtrlO(shell);

        Assert.True(shell.IsTranscriptMode);
        var snapshotLines = shell.Transcript.Snapshot!.Lines;
        Assert.Contains(snapshotLines, static line => line.Contains("CoT1"));
        Assert.Contains(snapshotLines, static line => line.Contains("CoT2"));
        Assert.NotNull(shell.View.Transcript);
        Assert.Contains("详细转录", shell.View.Transcript.Footer.FullText);
    }

    /// <summary>再次 ctrl+o 退出 → 快照丢弃（Transcript 卸载恢复主屏）+ verbose 联动关。</summary>
    [Fact]
    public void CtrlO_Again_Exits_AllCollapsed_FooterRestored()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("带思考", MessageId: 1, ThinkingDurationMs: 100, ThinkingContent: "CoT"));
        shell.DrainEngine();

        CtrlO(shell);
        Assert.True(shell.View.VerboseMode);
        CtrlO(shell);

        Assert.False(shell.IsTranscriptMode);
        Assert.Null(shell.Transcript.Snapshot);
        Assert.Null(shell.View.Transcript);
        Assert.False(shell.View.VerboseMode);
    }

    /// <summary>无 CoT 消息 ctrl+o — 模式仍切换（verbose 全屏可看全文），快照仅正文行无 CoT 段。</summary>
    [Fact]
    public void CtrlO_NoCoT_StillTogglesMode()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("无思考回复"));
        shell.DrainEngine();

        CtrlO(shell);

        Assert.True(shell.IsTranscriptMode);
        var line = Assert.Single(shell.Transcript.Snapshot!.Lines);
        Assert.Equal("● 无思考回复", line);
    }

    /// <summary>transcript 态吞按键 — 字符 j/k 不进输入框（模态语义守卫）。</summary>
    [Fact]
    public void TranscriptMode_SwallowsCharacterKeys_NotIntoInput()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("回复", MessageId: 1));
        shell.DrainEngine();
        CtrlO(shell);

        Type(shell, "jkx");

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.True(shell.IsTranscriptMode);
    }

    /// <summary>/ 搜索 — 字符增量查询（footer 搜索栏显示查询）+ Esc 撤销回浏览态（查询清空）。</summary>
    [Fact]
    public void TranscriptSearch_TypeAccumulates_EscClears()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("错误修复完成"));
        shell.DrainEngine();
        CtrlO(shell);

        shell.HandleKey(new ConsoleKeyInfo('/', ConsoleKey.D1, false, false, false));
        Type(shell, "错误");
        Assert.Contains("错误", shell.View.Transcript!.Footer.SearchQuery);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));
        Assert.Null(shell.View.Transcript.Footer.SearchQuery);
        Assert.True(shell.IsTranscriptMode);
    }

    /// <summary>/ 搜索增量输入 — 每键就近预览跳转（滚动移向匹配行 — 从底部回绕到中部首个匹配 → ScrollOffset 增大）。</summary>
    [Fact]
    public void TranscriptSearch_Enter_ScrollsToNextMatch()
    {
        var (shell, engine) = CreateShell();
        for (var i = 1; i <= 30; i++) {
            engine.Events.Enqueue(new BridgeEvent.AssistantMessage($"普通回复 {i}", MessageId: i));
            if (i == 15) {
                engine.Events.Enqueue(new BridgeEvent.AssistantMessage("目标关键词在这里", MessageId: 31));
            }
        }
        shell.DrainEngine();
        CtrlO(shell);
        var before = shell.Transcript.ScrollOffset;
        Assert.True(before > 0, "31 行内容 > 23 行视口 — 进入后应滚到底部");

        shell.HandleKey(new ConsoleKeyInfo('/', ConsoleKey.D1, false, false, false));
        Type(shell, "目标关键词");
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

        Assert.True(shell.Transcript.ScrollOffset > before, $"输入应跳向匹配行（{before} → {shell.Transcript.ScrollOffset}）");
    }

    /// <summary>transcript 态 Esc — 退出 transcript（非搜索态时）。</summary>
    [Fact]
    public void TranscriptMode_Esc_ExitsMode()
    {
        var (shell, engine) = CreateShell();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("回复", MessageId: 1));
        shell.DrainEngine();
        CtrlO(shell);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false));

        Assert.False(shell.IsTranscriptMode);
    }
}
