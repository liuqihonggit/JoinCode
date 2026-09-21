namespace JoinCode.Abstractions.Utils.Diagnostics;

/// <summary>
/// 调试日志条目 — 记录诊断输出的时间戳、级别、分类和消息
/// </summary>
public sealed class DebugLogEntry {
    /// <summary>获取时间戳。</summary>
    public DateTimeOffset Timestamp { get; init; }
    /// <summary>获取日志级别。</summary>
    public DebugLogLevel Level { get; init; }
    /// <summary>获取日志分类。</summary>
    public string Category { get; init; }
    /// <summary>获取日志消息。</summary>
    public string Message { get; init; }

    /// <summary>
    /// 构造调试日志条目。
    /// </summary>
    /// <param name="timestamp">时间戳。</param>
    /// <param name="level">日志级别。</param>
    /// <param name="category">日志分类。</param>
    /// <param name="message">日志消息。</param>
    public DebugLogEntry(DateTimeOffset timestamp, DebugLogLevel level, string category, string message) {
        Timestamp = timestamp;
        Level = level;
        Category = category;
        Message = message;
    }
}

/// <summary>
/// 调试日志级别
/// </summary>
public enum DebugLogLevel {
    [EnumValue("TRACE")] Trace,
    [EnumValue("INFO")] Info,
    [EnumValue("WARN")] Warn,
    [EnumValue("ERROR")] Error,
}
