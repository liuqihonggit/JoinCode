namespace Core.Utils;

/// <summary>
/// 防抖跟踪器 — 按文件路径调度防抖定时器，并标记/消费内部写入以避免自触发
/// </summary>
public sealed class DebounceTracker : IDisposable {
    private volatile ImmutableHamT<string, Timer> _timers;
    private volatile ImmutableHamT<string, long> _internalWriteTimestamps;
    private bool _disposed;

    /// <summary>
    /// 防抖间隔，默认 500ms
    /// </summary>
    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 内部写入识别窗口（毫秒），默认 5000ms
    /// </summary>
    public int InternalWriteWindowMs { get; set; } = 5000;

    /// <summary>
    /// 构造防抖跟踪器
    /// </summary>
    /// <param name="comparer">字符串比较器，用于路径键归一化；默认 OrdinalIgnoreCase</param>
    public DebounceTracker(StringComparer? comparer = null) {
        var c = comparer ?? StringComparer.OrdinalIgnoreCase;
        _timers = ImmutableHamT<string, Timer>.Empty.WithComparers(c);
        _internalWriteTimestamps = ImmutableHamT<string, long>.Empty.WithComparers(c);
    }

    /// <summary>
    /// 标记一次内部写入，用于后续消费时识别为自触发
    /// </summary>
    /// <param name="filePath">文件路径</param>
    public void MarkInternalWrite(string filePath) {
        var normalizedPath = Path.GetFullPath(filePath);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        while (true) {
            var current = _internalWriteTimestamps;
            if (Interlocked.CompareExchange(ref _internalWriteTimestamps, current.SetItem(normalizedPath, now), current) == current) break;
        }
    }

    /// <summary>
    /// 消费内部写入标记，若在窗口期内则返回 true
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否为窗口期内的内部写入</returns>
    public bool ConsumeInternalWrite(string filePath) {
        var normalizedPath = Path.GetFullPath(filePath);
        long timestamp = 0;
        var removed = false;
        while (true) {
            var current = _internalWriteTimestamps;
            ImmutableHamT<string, long> updated;
            if (current.TryGetValue(normalizedPath, out var ts)) {
                timestamp = ts;
                removed = true;
                updated = current.Remove(normalizedPath);
            } else {
                updated = current;
            }
            if (Interlocked.CompareExchange(ref _internalWriteTimestamps, updated, current) == current) break;
        }
        if (removed) {
            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp;
            return elapsed < InternalWriteWindowMs;
        }
        return false;
    }

    /// <summary>
    /// 调度防抖定时器，间隔到期后触发回调；若已有定时器则替换
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="fireAction">防抖到期触发的回调</param>
    public async ValueTask ScheduleDebounce(string filePath, Action fireAction) {
        var interval = DebounceInterval;
        if (interval <= TimeSpan.Zero) {
            fireAction();
            return;
        }

        Timer? existingTimer = null;
        while (true) {
            var current = _timers;
            ImmutableHamT<string, Timer> updated;
            if (current.TryGetValue(filePath, out var t)) {
                existingTimer = t;
                updated = current.Remove(filePath);
            } else {
                updated = current;
            }
            if (Interlocked.CompareExchange(ref _timers, updated, current) == current) break;
        }
        if (existingTimer is { } oldTimer)
            await oldTimer.DisposeAsync().ConfigureAwait(false);

        while (true) {
            var current = _timers;
            var newTimer = new Timer(_ => {
                try {
                    Timer? timer = null;
                    while (true) {
                        var cur = _timers;
                        ImmutableHamT<string, Timer> upd;
                        if (cur.TryGetValue(filePath, out var t)) {
                            timer = t;
                            upd = cur.Remove(filePath);
                        } else {
                            upd = cur;
                        }
                        if (Interlocked.CompareExchange(ref _timers, upd, cur) == cur) break;
                    }
                    timer?.Dispose();
                    if (!_disposed) fireAction();
                }
                catch (Exception ex) { Console.Error.WriteLine($"[DebounceTracker] timer 回调异常: {ex}"); }
            }, null, interval, Timeout.InfiniteTimeSpan);
            if (Interlocked.CompareExchange(ref _timers, current.SetItem(filePath, newTimer), current) == current) break;
            await newTimer.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 释放所有定时器与内部写入标记
    /// </summary>
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        ImmutableHamT<string, Timer> oldTimers = default!;
        while (true) {
            var current = _timers;
            if (Interlocked.CompareExchange(ref _timers, ImmutableHamT<string, Timer>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase), current) == current) { oldTimers = current; break; }
        }
        foreach (var kvp in oldTimers)
            kvp.Value.Dispose();
        while (true) {
            var current = _internalWriteTimestamps;
            if (Interlocked.CompareExchange(ref _internalWriteTimestamps, ImmutableHamT<string, long>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase), current) == current) break;
        }
    }
}