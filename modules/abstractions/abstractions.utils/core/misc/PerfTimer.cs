namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 性能计时工具 — 统一启动打点，受 <c>JCC_DEBUG_TIMING</c> 环境变量开关控制（默认关）。
/// <para>
/// 用法：<c>using var _ = PerfTimer.Scope("xxx");</c> — 作用域结束自动输出 <c>[TIMING] xxx: Nms</c> 到 stderr。
/// 开关关闭时 <c>Scope</c> 返回 <c>default</c>，<c>Dispose</c> 为 no-op，零开销。
/// </para>
/// <para>详见 AGENTS.md「性能打点」、ADR-0093 工具类风格。</para>
/// </summary>
public static class PerfTimer {
    private static readonly bool _enabled =
        System.Environment.GetEnvironmentVariable("JCC_DEBUG_TIMING") is "1" or "true";

    /// <summary>
    /// 计时作用域 — <c>using var s = PerfTimer.Scope("xxx");</c> 自动在 Dispose 时输出 <c>[TIMING] xxx: Nms</c>。
    /// </summary>
    /// <param name="label">计时标签，输出为 <c>[TIMING] {label}: {ms}ms</c>。</param>
    /// <returns>计时作用域；开关关闭时返回 <c>default</c>（Dispose 为 no-op）。</returns>
    public static PerfScope Scope(string label) =>
        _enabled ? new PerfScope(label) : default;

    /// <summary>
    /// 手动计时 — <c>var sw = PerfTimer.Start("xxx"); ... sw?.Stop();</c>，适合连续多段计时。
    /// </summary>
    /// <param name="label">计时标签。</param>
    /// <returns>计时器；开关关闭时返回 <c>null</c>。</returns>
    public static PerfStopwatch? Start(string label) =>
        _enabled ? new PerfStopwatch(label) : null;

    /// <summary>
    /// 标记点 — 立即输出 <c>[TIMING] {label}</c>（不带耗时，用于标记执行位置）。
    /// </summary>
    /// <param name="label">标记文本。</param>
    public static void Mark(string label) {
        if (_enabled) System.Console.Error.WriteLine($"[TIMING] {label}");
    }

    /// <summary>开关是否启用（<c>JCC_DEBUG_TIMING=1/true</c>）。</summary>
    public static bool IsEnabled => _enabled;
}

/// <summary>
/// 性能计时作用域 — <c>using</c> 模式自动输出耗时到 stderr。
/// <para>由 <see cref="PerfTimer.Scope"/> 创建，开关关闭时为 <c>default</c>（Dispose no-op）。</para>
/// </summary>
public readonly struct PerfScope : System.IDisposable {
    private readonly string? _label;
    private readonly long _startTicks;

    internal PerfScope(string label) {
        _label = label;
        _startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>输出耗时到 stderr（<c>[TIMING] {label}: {ms}ms</c>）；未启用时为 no-op。</summary>
    public void Dispose() {
        if (_label is null) return;
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(_startTicks).TotalMilliseconds;
        System.Console.Error.WriteLine($"[TIMING] {_label}: {ms:F0}ms");
    }
}

/// <summary>
/// 手动计时器 — <see cref="PerfTimer.Start"/> 创建，<see cref="Stop"/> 输出耗时。
/// </summary>
public readonly struct PerfStopwatch {
    private readonly string _label;
    private readonly long _startTicks;

    internal PerfStopwatch(string label) {
        _label = label;
        _startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>输出耗时到 stderr（<c>[TIMING] {label}: {ms}ms</c>）。</summary>
    public void Stop() {
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(_startTicks).TotalMilliseconds;
        System.Console.Error.WriteLine($"[TIMING] {_label}: {ms:F0}ms");
    }
}
