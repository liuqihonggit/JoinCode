namespace Composition.Tests.Commands;

/// <summary>
/// 输出捕获骨架的并发不变量（CI run #1361 回归）。
/// <para>Console.Out 是进程全局态：捕获段期间并行的**非捕获**测试类会读到同一个 writer。
/// 捕获段结束时若把它 Dispose，这些晚到的写入就撞上「已关闭的 TextWriter」，
/// 表现为完全无关的用例随机失败（#1361 里是 DebugLogCommandTest.ClearFlag_ClearsBuffer）。</para>
/// </summary>
public sealed class CommandTestHarnessTests {
    /// <summary>捕获段结束后，捕获期间拿到的 Console.Out 引用必须仍可写。</summary>
    [Fact]
    public async Task CapturedWriter_StaysWritableAfterCaptureEnds() {
        TextWriter? observed = null;

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => {
            observed = Console.Out;
            Console.WriteLine("捕获段内写入");
            return Task.FromResult(true);
        }).ConfigureAwait(true);

        output.Should().Contain("捕获段内写入", "捕获本身不能因为不 Dispose writer 就失效");
        if (observed is null) throw new InvalidOperationException("捕获段内没有取到 Console.Out");

        var lateWrite = () => observed.WriteLine("捕获段外的晚到写入");
        lateWrite.Should().NotThrow(
            "并发写入方持有的是捕获期间的 Console.Out，writer 不能在自己退场后变成已关闭");
    }

    /// <summary>捕获段结束后必须把 Console.Out 从捕获 writer 上摘下来，否则后续输出全漏进已退场的捕获段。</summary>
    [Fact]
    public async Task CaptureOutputAsync_RestoresConsoleOut() {
        TextWriter? capturedWriter = null;

        await CommandTestHarness.CaptureOutputAsync(() => {
            capturedWriter = Console.Out;
            return Task.FromResult(0);
        }).ConfigureAwait(true);

        if (capturedWriter is null) throw new InvalidOperationException("捕获段内没有取到 Console.Out");

        Console.Out.Should().NotBeSameAs(capturedWriter,
            "恢复链必须摘掉本段的捕获 writer（并行类的捕获段可能正占用 Console.Out，故只断言不是本段 writer）");
    }
}
