namespace JoinCode.Tui.Diagnostics;

/// <summary>
/// 性能埋点 — 在关键路径测量耗时，写入 .jcctui_perf/perf.log。
/// 用法: using var _ = PerfTap.Measure("OutputView.AppendLine");
/// </summary>
public static class PerfTap
{
    private static readonly string PerfDir = System.IO.Path.Combine(
        System.IO.Directory.GetCurrentDirectory(), ".jcctui_perf");
    private static readonly string PerfLog = System.IO.Path.Combine(PerfDir, "perf.log");
    private static long _seq;

    /// <summary>
    /// 测量一个代码块的耗时（using 模式）。
    /// </summary>
    public static PerfScope Measure(string label)
    {
        return new PerfScope(label);
    }

    /// <summary>
    /// 记录一条性能事件。
    /// </summary>
    public static void Log(string label, long elapsedMs, string? extra = null)
    {
        try
        {
            System.IO.Directory.CreateDirectory(PerfDir);
            var seq = Interlocked.Increment(ref _seq);
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] #{seq:D6} {label} {elapsedMs}ms";
            if (extra is not null) line += $" | {extra}";
            System.IO.File.AppendAllText(PerfLog, line + "\n");
        }
        catch (Exception ex) { Console.Error.WriteLine($"[PerfTap] log failed: {ex.Message}"); }
    }

    /// <summary>
    /// 记录慢操作（>10ms 才记录，避免大量微秒级日志）。
    /// </summary>
    public static void LogIfSlow(string label, long elapsedMs, string? extra = null)
    {
        if (elapsedMs < 10) return;
        Log(label, elapsedMs, extra);
    }

    public sealed class PerfScope : IDisposable
    {
        private readonly string _label;
        private readonly long _start;

        public PerfScope(string label)
        {
            _label = label;
            _start = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var elapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            LogIfSlow(_label, (long)elapsedMs);
        }
    }
}
