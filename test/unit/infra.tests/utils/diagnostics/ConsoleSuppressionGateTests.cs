namespace Infra.Tests.Utils.Diagnostics;

/// <summary>
/// ConsoleSuppressionGate 单元测试 — TUI 会话期控制台抑制闸门契约：
/// 抑制期间 stdout/stderr 全部改写环形缓冲（终端画布独占给 inline TUI 渲染器 —
/// 诊断行溢出底行滚屏会打垮相对光标模型，TASK077）；最外层退出时冲刷回原 stderr（CI/E2E 捕获不受损）；
/// 嵌套引用计数；环形上限丢旧。Console.SetOut/SetError 是进程全局态 — 本类测试串行（xUnit 同类内串行）。
/// </summary>
public sealed class ConsoleSuppressionGateTests {
    [Fact]
    public void Suppressed_ConsoleErrorAndOut_CapturedInRingNotOriginalWriters() {
        using var err = new StringWriter();
        using var outWriter = new StringWriter();
        Console.SetError(err);
        Console.SetOut(outWriter);
        try {
            using var _ = ConsoleSuppressionGate.Begin("test");
            Console.Error.WriteLine("[TEST-ERR] line1");
            Console.Out.WriteLine("[TEST-OUT] line2");
            Assert.Equal(string.Empty, err.ToString());
            Assert.Equal(string.Empty, outWriter.ToString());
        } finally {
            Console.SetError(new StreamWriter(Stream.Null) { AutoFlush = true });
            Console.SetOut(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void Suppressed_RingHoldsLines_ThenTakeDrains() {
        try {
            using var _ = ConsoleSuppressionGate.Begin("test");
            Console.Error.WriteLine("[TEST] a");
            Console.Error.WriteLine("[TEST] b");
            var drained = ConsoleSuppressionGate.TakeSuppressedLines();
            Assert.Equal(2, drained.Count);
            Assert.Contains("[TEST] a", drained[0]);
            Assert.Contains("[TEST] b", drained[1]);
            Assert.Empty(ConsoleSuppressionGate.TakeSuppressedLines());
        } finally {
            Console.SetError(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void OnOutermostDispose_RingFlushedToOriginalErrorWriter() {
        using var err = new StringWriter();
        Console.SetError(err);
        try {
            using (var _ = ConsoleSuppressionGate.Begin("test")) {
                Console.Error.WriteLine("[TEST] during");
                Assert.Equal(string.Empty, err.ToString());
            }

            Assert.Contains("[TEST] during", err.ToString());
        } finally {
            Console.SetError(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void NestedDispose_InnerDoesNotRestoreOrFlush() {
        using var err = new StringWriter();
        Console.SetError(err);
        try {
            using var outer = ConsoleSuppressionGate.Begin("outer");
            Console.Error.WriteLine("[TEST] outer-line");
            using (var inner = ConsoleSuppressionGate.Begin("inner")) {
                Console.Error.WriteLine("[TEST] inner-line");
            }

            Assert.Equal(string.Empty, err.ToString());
            Console.Error.WriteLine("[TEST] still-suppressed");
            Assert.Equal(string.Empty, err.ToString());
        } finally {
            Console.SetError(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }

    [Fact]
    public void RingCap_DropsOldest_Lines() {
        try {
            using var _ = ConsoleSuppressionGate.Begin("cap");
            for (var i = 0; i < 1500; i++) {
                Console.Error.WriteLine($"[TEST] n{i};");
            }

            var drained = ConsoleSuppressionGate.TakeSuppressedLines();
            Assert.True(drained.Count <= 1024);
            Assert.DoesNotContain(drained, line => line.Contains("n0;"));
            Assert.Contains(drained, line => line.Contains("n1499;"));
        } finally {
            Console.SetError(new StreamWriter(Stream.Null) { AutoFlush = true });
        }
    }
}
