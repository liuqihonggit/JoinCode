namespace NativeTui.Tests.Repl;

/// <summary>
/// transcript 按 queryId 归属（ADR 0165 阶段 1b）— 后台查询没被切回时，事件只在
/// <c>_backgroundQueries</c> 的桶里，ctrl+o 看不到就等于永久丢失。这里锁定三条：
/// 后台内容要能进 transcript（带 queryId 归属头）、切回后不得重复出现、无后台时快照零破坏。
/// </summary>
public sealed class TranscriptQueryAttributionTests
{
    /// <summary>固定尺寸源（摆脱测试进程 Console 环境依赖）。</summary>
    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Cols, int Rows) Current => (cols, rows);
    }

    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell()
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"),
            new StringWriter(), sizeSource: new FixedSizeSource(80, 30));
        shell.SeedContent();
        return (shell, engine);
    }

    private static void Type(ReplShell shell, string text)
    {
        foreach (var ch in text)
        {
            shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, ch.ToString()));
        }
    }

    private static void PressCtrlB(ReplShell shell) =>
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "b", Ctrl: true));

    /// <summary>提交 + 转后台（FakeReplEngine 恒返回 q-test）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) BackgroundedQuery()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "long task");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-test" });
        shell.DrainEngine();
        PressCtrlB(shell);
        return (shell, engine);
    }

    /// <summary>后台查询的内容必须在 transcript 里可见，并带 queryId 归属头。</summary>
    [Fact]
    public void Snapshot_WithBackgroundQuery_ContainsItsContentAndHeader()
    {
        var (shell, engine) = BackgroundedQuery();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("后台输出") { QueryId = "q-test" });
        shell.DrainEngine();

        var lines = shell.BuildTranscriptSnapshot().Lines;

        Assert.Contains(lines, line => line.Contains("后台查询 q-test", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("后台输出", StringComparison.Ordinal));
    }

    /// <summary>切回后内容已进主视图，快照不得再重复补一遍（桶的 Pending 已清空）。</summary>
    [Fact]
    public void Snapshot_AfterRefocus_DoesNotDuplicateContent()
    {
        var (shell, engine) = BackgroundedQuery();
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("后台输出") { QueryId = "q-test" });
        shell.DrainEngine();
        PressCtrlB(shell);

        var lines = shell.BuildTranscriptSnapshot().Lines;

        Assert.Equal(1, lines.Count(line => line.Contains("后台输出", StringComparison.Ordinal)));
        Assert.DoesNotContain(lines, line => line.Contains("后台查询 q-test", StringComparison.Ordinal));
    }

    /// <summary>无后台查询时快照等于主视图投影（零破坏 — 不多一行空行、不多个头）。</summary>
    [Fact]
    public void Snapshot_WithoutBackground_MatchesViewProjection()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "hi");
        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnStarted { QueryId = "q-test" });
        engine.Events.Enqueue(new BridgeEvent.AssistantMessage("正常回复") { QueryId = "q-test" });
        shell.DrainEngine();

        var lines = shell.BuildTranscriptSnapshot().Lines;

        Assert.Contains(lines, line => line.Contains("正常回复", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("后台查询", StringComparison.Ordinal));
        // 零破坏：没有后台桶时不追加任何分隔/空行（多了就是给既有 transcript 塞垃圾）
        Assert.DoesNotContain(string.Empty, lines);
    }
}
