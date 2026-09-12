namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 工具遥测辅助类 — 统一工具 Handler/Middleware 的遥测记录模式
/// 消除各 Handler 中重复的 RecordXxxMetrics 私有方法
/// </summary>
public static class ToolTelemetryHelper
{
    /// <summary>
    /// 记录工具操作计数 — 统一 pattern: metricName + tags
    /// </summary>
    public static void RecordToolCount(
        ITelemetryService? telemetry,
        string metricName,
        Dictionary<string, string> tags,
        string? description = null)
        => telemetry?.RecordCount(metricName, tags, description: description);

    /// <summary>
    /// 记录工具操作计数 — operation + isSuccess 模式（Agent/Git 等使用）
    /// </summary>
    public static void RecordToolCount(
        ITelemetryService? telemetry,
        string metricName,
        string operation,
        bool isSuccess,
        string? description = null)
        => telemetry?.RecordCount(metricName, new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, description: description);

    /// <summary>
    /// 记录工具操作计数 — operation + result 模式（Shell/Search/Web 等使用）
    /// </summary>
    public static void RecordToolCount(
        ITelemetryService? telemetry,
        string metricName,
        string operation,
        string result,
        string? description = null)
        => telemetry?.RecordCount(metricName, new Dictionary<string, string> { ["operation"] = operation, ["result"] = result }, description: description);

    /// <summary>
    /// 记录工具操作直方图 — 如搜索文件数、Web响应大小
    /// </summary>
    public static void RecordToolHistogram(
        ITelemetryService? telemetry,
        string metricName,
        double value,
        Dictionary<string, string> tags,
        string? unit = null,
        string? description = null)
        => telemetry?.RecordHistogram(metricName, value, tags, unit, description);
}
