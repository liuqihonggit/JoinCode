using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.Models.Telemetry;


[Register]
public sealed class TelemetryConfig
{
    public static TelemetryConfig FromEnvironment()
    {
        var exportFormatStr = Environment.GetEnvironmentVariable("JCC_TELEMETRY_EXPORT");
        var exportFormat = exportFormatStr switch
        {
            "Console" => TelemetryExportFormat.Console,
            "Otlp" => TelemetryExportFormat.Otlp,
            "Prometheus" => TelemetryExportFormat.Prometheus,
            _ => TelemetryExportFormat.None
        };

        return new TelemetryConfig(skipInit: true)
        {
            ExportFormat = exportFormat,
            TracingEnabled = Environment.GetEnvironmentVariable("JCC_TELEMETRY_ENABLED") is "false"
                ? false
                : Environment.GetEnvironmentVariable("JCC_TELEMETRY_TRACING") is not "false",
            MetricsEnabled = Environment.GetEnvironmentVariable("JCC_TELEMETRY_ENABLED") is "false"
                ? false
                : Environment.GetEnvironmentVariable("JCC_TELEMETRY_METRICS") is not "false"
        };
    }

    /// <summary>
    /// DI 构造函数 — 从环境变量初始化
    /// </summary>
    public TelemetryConfig()
    {
        var config = FromEnvironment();
        ExportFormat = config.ExportFormat;
        TracingEnabled = config.TracingEnabled;
        MetricsEnabled = config.MetricsEnabled;
    }

    /// <summary>
    /// 内部构造函数 — 跳过 FromEnvironment 初始化，供 FromEnvironment 和测试使用
    /// </summary>
    private TelemetryConfig(bool skipInit) { }

    public string ServiceName { get; init; } = "JoinCode";

    public string ServiceVersion { get; init; } = "1.0.0";

    public bool TracingEnabled { get; init; } = true;

    public bool MetricsEnabled { get; init; } = true;

    public TelemetryExportFormat ExportFormat { get; init; } = TelemetryExportFormat.None;

    public string? OtlpEndpoint { get; init; }

    public int MetricsIntervalSeconds { get; init; } = 15;

    public bool RecordExceptions { get; init; } = true;

    public Dictionary<string, string> DefaultTags { get; init; } = [];
}
