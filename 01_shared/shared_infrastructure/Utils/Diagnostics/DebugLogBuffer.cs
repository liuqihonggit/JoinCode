namespace Infrastructure.Utils.Diagnostics;

/// <summary>
/// 调试日志缓冲区 — 订阅 Diag.DiagnosticLineWritten 事件，捕获诊断输出到环形缓冲区
/// </summary>
[Register(typeof(IDebugLogBuffer), ServiceLifetime.Singleton)]
public sealed partial class DebugLogBuffer : IDebugLogBuffer
{
    private readonly ConcurrentQueue<DebugLogEntry> _entries = new();
    private readonly int _maxCapacity;

    public DebugLogBuffer(int maxCapacity = 2000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCapacity);
        _maxCapacity = maxCapacity;

        Diag.DiagnosticLineWritten += OnDiagnosticLineWritten;
    }

    public int Count => _entries.Count;

    public IReadOnlyList<DebugLogEntry> GetRecent(int count = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return _entries.Reverse().Take(count).ToList();
    }

    public IReadOnlyList<DebugLogEntry> GetByLevel(DebugLogLevel level, int count = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return _entries.Where(e => e.Level == level).Reverse().Take(count).ToList();
    }

    public IReadOnlyList<DebugLogEntry> GetByMinLevel(DebugLogLevel minLevel, int count = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return _entries.Where(e => e.Level >= minLevel).Reverse().Take(count).ToList();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
    }

    private void OnDiagnosticLineWritten(object? sender, string message)
    {
        var (level, category) = ClassifyMessage(message);
        var entry = new DebugLogEntry(DateTimeOffset.UtcNow, level, category, message);

        _entries.Enqueue(entry);

        while (_entries.Count > _maxCapacity && _entries.TryDequeue(out _)) { }
    }

    /// <summary>
    /// 根据消息前缀分类日志级别和类别
    /// 已知前缀: [DIAG-ERR], [DIAG-ERR-STACK], [DIAG-ERR-INNER-N], [WIRE], [STEP], [READY], [DI], [ALIVE], [DIAG-TERM] 等
    /// </summary>
    private static (DebugLogLevel Level, string Category) ClassifyMessage(string message)
    {
        if (message.StartsWith("[DIAG-ERR", StringComparison.Ordinal))
            return (DebugLogLevel.Error, "ERROR");

        if (message.StartsWith("[WIRE]", StringComparison.Ordinal))
            return (DebugLogLevel.Trace, "WIRE");

        if (message.StartsWith("[STEP]", StringComparison.Ordinal))
            return (DebugLogLevel.Info, "STEP");

        if (message.StartsWith("[READY]", StringComparison.Ordinal))
            return (DebugLogLevel.Info, "READY");

        if (message.StartsWith("[DI]", StringComparison.Ordinal))
            return (DebugLogLevel.Trace, "DI");

        if (message.StartsWith("[ALIVE]", StringComparison.Ordinal))
            return (DebugLogLevel.Trace, "ALIVE");

        if (message.StartsWith("[DIAG-TERM]", StringComparison.Ordinal))
            return (DebugLogLevel.Trace, "TERM");

        if (message.StartsWith("[CrashStore]", StringComparison.Ordinal))
            return (DebugLogLevel.Error, "CRASH");

        if (message.StartsWith("[RUN]", StringComparison.Ordinal))
            return (DebugLogLevel.Info, "RUN");

        var span = message.AsSpan();
        var bracketEnd = span.IndexOf(']');
        if (bracketEnd > 0 && span[0] == '[')
        {
            var category = span[..(bracketEnd + 1)].ToString();
            return (DebugLogLevel.Info, category);
        }

        return (DebugLogLevel.Info, "GENERAL");
    }
}
