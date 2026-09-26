namespace Core.Utils;

/// <summary>
/// 防抖跟踪器 — 按文件路径调度防抖定时器，并标记/消费内部写入以避免自触发
/// </summary>
public sealed class DebounceTracker : IDisposable {
    private ImmutableDictionary<string, Timer> _timers;
    private ImmutableDictionary<string, long> _internalWriteTimestamps;
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
        _timers = ImmutableDictionary<string, Timer>.Empty.WithComparers(c);
        _internalWriteTimestamps = ImmutableDictionary<string, long>.Empty.WithComparers(c);
    }

    /// <summary>
    /// 标记一次内部写入，用于后续消费时识别为自触发
    /// </summary>
    /// <param name="filePath">文件路径</param>
    public void MarkInternalWrite(string filePath) {
        var normalizedPath = Path.GetFullPath(filePath);
        ImmutableInterlocked.Update(ref _internalWriteTimestamps, d => d.SetItem(normalizedPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
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
        ImmutableInterlocked.Update(ref _internalWriteTimestamps, d => {
            if (d.TryGetValue(normalizedPath, out var ts)) {
                timestamp = ts;
                removed = true;
                return d.Remove(normalizedPath);
            }
            return d;
        });
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
        ImmutableInterlocked.Update(ref _timers, d => {
            if (d.TryGetValue(filePath, out var t)) {
                existingTimer = t;
                return d.Remove(filePath);
            }
            return d;
        });
        if (existingTimer is { } oldTimer)
            await oldTimer.DisposeAsync().ConfigureAwait(false);

        var newTimer = new Timer(_ => {
            try {
                Timer? timer = null;
                ImmutableInterlocked.Update(ref _timers, d => {
                    if (d.TryGetValue(filePath, out var t)) {
                        timer = t;
                        return d.Remove(filePath);
                    }
                    return d;
                });
                timer?.Dispose();
                if (!_disposed) fireAction();
            }
            catch (Exception ex) { Console.Error.WriteLine($"[DebounceTracker] timer 回调异常: {ex}"); }
        }, null, interval, Timeout.InfiniteTimeSpan);
        ImmutableInterlocked.Update(ref _timers, d => d.SetItem(filePath, newTimer));
    }

    /// <summary>
    /// 释放所有定时器与内部写入标记
    /// </summary>
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in Interlocked.Exchange(ref _timers, ImmutableDictionary<string, Timer>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase)))
            kvp.Value.Dispose();
        Interlocked.Exchange(ref _internalWriteTimestamps, ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase));
    }
}