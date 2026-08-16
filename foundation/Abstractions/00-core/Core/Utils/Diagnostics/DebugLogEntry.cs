namespace JoinCode.Abstractions.Utils.Diagnostics;

/// <summary>
/// 调试日志条目 — 记录诊断输出的时间戳、级别、分类和消息
/// </summary>
public sealed class DebugLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public DebugLogLevel Level { get; init; }
    public string Category { get; init; }
    public string Message { get; init; }

    public DebugLogEntry(DateTimeOffset timestamp, DebugLogLevel level, string category, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Category = category;
        Message = message;
    }
}

/// <summary>
/// 调试日志级别
/// </summary>
public enum DebugLogLevel
{
    [EnumValue("TRACE")] Trace,
    [EnumValue("INFO")] Info,
    [EnumValue("WARN")] Warn,
    [EnumValue("ERROR")] Error,
}
