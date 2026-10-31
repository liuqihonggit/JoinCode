namespace Host.Tests.Cli;

/// <summary>
/// TerminalHelper 闸门改道契约（TASK078）— TUI 会话期（ConsoleSuppressionGate 激活），
/// CLI 直写组件经 TerminalHelper 的输出（含 ConsoleActor 持有的 Init 期 _realOut 引用旁路）
/// 必须改道闸门环形缓冲，不再触碰终端画布 — /?? 需求澄清等 CLI 命令在 TUI 内运行时
/// 曾直接写 RealOut 与 inline 渲染器双写互踩（用户实测整帧错乱）。
/// Console.SetOut 是进程全局态 — 本类测试串行（xUnit 同类内串行）。
/// </summary>
[Collection("ConsoleOutput")]
public sealed class TerminalHelperGateRoutingTests {
    [Fact]
    public void WriteLine_DuringGate_RoutesToRingNotOriginalWriter() {
        using var originalOut = new StringWriter();
        Console.SetOut(originalOut);
        try {
            using var _ = ConsoleSuppressionGate.Begin("TUI 会话");
            TerminalHelper.WriteLine("[TEST-TH] line");
            Assert.Equal(string.Empty, originalOut.ToString());
            var drained = ConsoleSuppressionGate.TakeSuppressedLines();
            Assert.Contains(drained, line => line.Contains("[TEST-TH] line"));
        } finally {
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void WriteRaw_DuringGate_RoutesToRingNotOriginalWriter() {
        using var originalOut = new StringWriter();
        Console.SetOut(originalOut);
        try {
            using var _ = ConsoleSuppressionGate.Begin("TUI 会话");
            TerminalHelper.WriteRaw("[TEST-TH] raw");
            TerminalHelper.WriteRaw('x');
            Assert.Equal(string.Empty, originalOut.ToString());
            var drained = ConsoleSuppressionGate.TakeSuppressedLines();
            Assert.Contains(drained, line => line.Contains("[TEST-TH] rawx"));
        } finally {
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void WriteLineReal_DuringGate_RoutesToRingNotOriginalWriter() {
        using var originalOut = new StringWriter();
        Console.SetOut(originalOut);
        try {
            using var _ = ConsoleSuppressionGate.Begin("TUI 会话");
            TerminalHelper.WriteLineReal("[TEST-TH] real");
            Assert.Equal(string.Empty, originalOut.ToString());
            var drained = ConsoleSuppressionGate.TakeSuppressedLines();
            Assert.Contains(drained, line => line.Contains("[TEST-TH] real"));
        } finally {
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void GateInactive_WritesGoToConsoleOut_AsBefore() {
        using var originalOut = new StringWriter();
        Console.SetOut(originalOut);
        try {
            TerminalHelper.WriteLine("[TEST-TH] normal");
            Assert.Contains("[TEST-TH] normal", originalOut.ToString());
        } finally {
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }
}
