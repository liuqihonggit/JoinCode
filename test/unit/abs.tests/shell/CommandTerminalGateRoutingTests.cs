namespace Abs.Tests.Shell;

/// <summary>
/// CommandTerminal 闸门改道契约（TASK078）— composition 命令经 global using 别名
/// TerminalHelper = JoinCode.Abstractions.Shell.CommandTerminal 调用本类（AskClarify 等），
/// TUI 会话期（ConsoleSuppressionGate 激活）其输出（含 RealOut 旁路与 SetOut 换装请求）
/// 必须改道闸门环形缓冲，不得触碰终端画布 — /?? 需求澄清曾直写画布与 inline 渲染器双写互踩。
/// Console.SetOut 是进程全局态 — 本类测试串行（xUnit 同类内串行）。
/// </summary>
public sealed class CommandTerminalGateRoutingTests {
    [Fact]
    public void WriteLine_DuringGate_RoutesToRingNotOriginalWriter() {
        using var originalOut = new StringWriter();
        Console.SetOut(originalOut);
        try {
            using var _ = ConsoleSuppressionGate.Begin("TUI 会话");
            CommandTerminal.WriteLine("[TEST-CT] line");
            Assert.Equal(string.Empty, originalOut.ToString());
            var drained = ConsoleSuppressionGate.TakeSuppressedLines();
            Assert.Contains(drained, line => line.Contains("[TEST-CT] line"));
        } finally {
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void WriteRaw_DuringGate_RoutesToRing() {
        try {
            using var _ = ConsoleSuppressionGate.Begin("TUI 会话");
            CommandTerminal.WriteRaw("[TEST-CT] raw");
            var drained = ConsoleSuppressionGate.TakeSuppressedLines();
            Assert.Contains(drained, line => line.Contains("[TEST-CT] raw"));
        } finally {
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void WriteLineReal_DuringGate_RoutesToRingNotRealOut() {
        using var originalOut = new StringWriter();
        Console.SetOut(originalOut);
        try {
            using var _ = ConsoleSuppressionGate.Begin("TUI 会话");
            CommandTerminal.WriteLineReal("[TEST-CT] real");
            Assert.Equal(string.Empty, originalOut.ToString());
            var drained = ConsoleSuppressionGate.TakeSuppressedLines();
            Assert.Contains(drained, line => line.Contains("[TEST-CT] real"));
        } finally {
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void SetOut_DuringGate_Refused() {
        try {
            using var _ = ConsoleSuppressionGate.Begin("TUI 会话");
            var gateWriter = Console.Out;
            using var attacker = new StringWriter();
            CommandTerminal.SetOut(attacker);
            Assert.True(ReferenceEquals(Console.Out, gateWriter), "闸门激活期 SetOut 不得换装");
        } finally {
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void GateInactive_WritesGoToConsoleOut_AsBefore() {
        using var originalOut = new StringWriter();
        Console.SetOut(originalOut);
        try {
            CommandTerminal.WriteLine("[TEST-CT] normal");
            Assert.Contains("[TEST-CT] normal", originalOut.ToString());
        } finally {
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }
}
