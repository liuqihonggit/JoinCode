namespace JoinCode.Abstractions.Models.Telemetry;


[Register(typeof(TelemetryConfig), ServiceLifetime.Singleton)]
public sealed class TelemetryConfig {
    /// <summary>从环境变量创建配置。</summary>
    public static TelemetryConfig FromEnvironment() {
        var exportFormatStr = Environment.GetEnvironmentVariable("JCC_TELEMETRY_EXPORT");
        var exportFormat = exportFormatStr switch {
            "Console" => TelemetryExportFormat.Console,
            "Otlp" => TelemetryExportFormat.Otlp,
            "Prometheus" => TelemetryExportFormat.Prometheus,
            _ => TelemetryExportFormat.None
        };

        return new TelemetryConfig(skipInit: true) {
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
    public TelemetryConfig() {
        var config = FromEnvironment();
        ExportFormat = config.ExportFormat;
        TracingEnabled = config.TracingEnabled;
        MetricsEnabled = config.MetricsEnabled;
    }

    /// <summary>
    /// 内部构造函数 — 跳过 FromEnvironment 初始化，供 FromEnvironment 和测试使用
    /// </summary>
    private TelemetryConfig(bool skipInit) { }

    /// <summary>获取服务名称。</summary>
    public string ServiceName { get; init; } = BrandConstants.ProductName; // P1-⑦ 委托统一数据源

    /// <summary>获取服务版本。</summary>
    public string ServiceVersion { get; init; } = "1.0.0";

    /// <summary>获取是否启用链路追踪。</summary>
    public bool TracingEnabled { get; init; } = true;

    /// <summary>获取是否启用指标采集。</summary>
    public bool MetricsEnabled { get; init; } = true;

    /// <summary>获取导出格式。</summary>
    public TelemetryExportFormat ExportFormat { get; init; } = TelemetryExportFormat.None;

    /// <summary>获取 OTLP 端点地址。</summary>
    public string? OtlpEndpoint { get; init; }

    /// <summary>获取指标采集间隔（秒）。</summary>
    public int MetricsIntervalSeconds { get; init; } = 15;

    /// <summary>获取是否记录异常。</summary>
    public bool RecordExceptions { get; init; } = true;

    /// <summary>获取默认标签字典。</summary>
    public Dictionary<string, string> DefaultTags { get; init; } = [];
}