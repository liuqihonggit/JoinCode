namespace Core.Configuration;

/// <summary>
/// Pre-DI 阶段的 stderr 日志 Provider — config loading 发生在 DI 容器构建之前，
/// 用此 Provider 直接输出到 Console.Error，--debuglog 时 minLevel=Debug，否则 Warning。
/// 替代 Diag.WriteLine 临时埋点，让 logger?.LogDebug 贯穿 config loading 全链路。
/// </summary>
public sealed class StderrLoggerProvider : ILoggerProvider {
    private readonly LogLevel _minLevel;

    /// <summary>
    /// 构造 stderr LoggerProvider — 按最低级别过滤，直接输出到 Console.Error
    /// </summary>
    ///; <param name="minLevel">最低日志级别，低于此级别的日志被丢弃</param>
    public StderrLoggerProvider(LogLevel minLevel = LogLevel.Warning) {
        _minLevel = minLevel;
    }

    /// <summary>
    /// 创建指定分类名称的 logger 实例
    /// </summary>
    public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName, _minLevel);

    /// <summary>
    /// 释放资源 — 无托管资源需要释放
    /// </summary>
    public void Dispose() { }
}

internal sealed class StderrLogger : ILogger {
    private readonly string _category;
    private readonly LogLevel _minLevel;

    internal StderrLogger(string category, LogLevel minLevel) {
        _category = category;
        _minLevel = minLevel;
    }

    /// <summary>开始日志作用域。</summary>
    /// <param name="state">作用域状态。</param>
    /// <returns>作用域 disposable，本实现返回 null。</returns>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>判断指定日志级别是否启用。</summary>
    /// <param name="logLevel">日志级别。</param>
    /// <returns>启用返回 true。</returns>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    /// <summary>写入日志到 stderr。</summary>
    /// <param name="logLevel">日志级别。</param>
    /// <param name="eventId">事件 ID。</param>
    /// <param name="state">日志状态。</param>
    /// <param name="exception">异常对象。</param>
    /// <param name="formatter">格式化函数。</param>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null) return;

        var levelStr = logLevel switch {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "????"
        };

        var shortCategory = _category.AsSpan(_category.LastIndexOf('.') + 1).ToString();

        Console.Error.Write($"{levelStr}: {shortCategory}");
        if (eventId.Id != 0)
            Console.Error.Write($"[{eventId.Id}]");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"      {message}");

        if (exception is not null)
            Console.Error.WriteLine($"      {exception.GetType().Name}: {exception.Message}");
    }
}