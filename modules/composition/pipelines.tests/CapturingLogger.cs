namespace Pipelines.Tests;

/// <summary>
/// 捕获 ILogger 调用 — 用于验证日志路径执行（级别/格式化消息）。
/// 确定性：仅内存记录，无 IO。
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T> {
    private readonly List<(LogLevel Level, string Message)> _entries = new();

    /// <summary>已记录的日志条目（级别+格式化消息）。</summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => NullLogger.Instance.BeginScope(state);

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        _entries.Add((logLevel, formatter(state, exception)));
    }
}
