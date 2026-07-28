namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// ITelemetryService 扩展方法 - 简化常见的遥测记录模式
/// </summary>
public static class TelemetryServiceExtensions
{
    /// <summary>
    /// 记录计数指标（简化 GetCounter + Add 模式）
    /// </summary>
    public static void RecordCount(
        this ITelemetryService? telemetry,
        string metricName,
        Dictionary<string, string>? tags = null,
        string? unit = "count",
        string? description = null)
    {
        if (telemetry == null) return;
        var counter = telemetry.GetCounter(metricName, unit, description);
        counter.Add(1, tags);
    }

    /// <summary>
    /// 记录直方图指标（简化 GetHistogram + Record 模式）
    /// </summary>
    public static void RecordHistogram(
        this ITelemetryService? telemetry,
        string metricName,
        double value,
        Dictionary<string, string>? tags = null,
        string? unit = null,
        string? description = null)
    {
        if (telemetry == null) return;
        var histogram = telemetry.GetHistogram(metricName, unit, description);
        histogram.Record(value, tags);
    }
}
