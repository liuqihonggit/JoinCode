namespace McpClient.Transports;

/// <summary>
/// 传输降级链遥测报告生成器 — 汇总降级链的指标、熔断器状态与配置,生成可读报告
/// </summary>
public sealed class TransportFallbackTelemetry
{
    private readonly McpTransportFallbackChain _chain;
    private readonly ILogger? _logger;

    /// <summary>
    /// 创建遥测报告生成器
    /// </summary>
    /// <param name="chain">传输降级链实例</param>
    /// <param name="logger">日志记录器,可为 null</param>
    public TransportFallbackTelemetry(McpTransportFallbackChain chain, ILogger? logger = null)
    {
        _chain = chain ?? throw new ArgumentNullException(nameof(chain));
        _logger = logger;
    }

    /// <summary>
    /// 生成降级链遥测报告 — 采集活跃传输、指标快照、熔断器状态与配置
    /// </summary>
    /// <returns>遥测报告对象</returns>
    public TransportFallbackReport GenerateReport()
    {
        var metrics = _chain.Metrics.GetSnapshot();
        var circuitStates = new CircuitBreakerReport[_chain.CircuitBreakers.Length];

        for (var i = 0; i < _chain.CircuitBreakers.Length; i++)
        {
            var cb = _chain.CircuitBreakers[i];
            circuitStates[i] = new CircuitBreakerReport
            {
                State = cb.Phase,
                ConsecutiveFailures = cb.ConsecutiveFailures,
                OpenedAt = cb.OpenedAt,
            };
        }

        return new TransportFallbackReport
        {
            ActiveTransportType = _chain.ActiveTransportType,
            ActiveTransportIndex = _chain.ActiveTransportIndex,
            Metrics = metrics,
            CircuitBreakers = circuitStates,
            Config = _chain.Config,
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// 生成并格式化遥测报告为可读字符串
    /// </summary>
    /// <returns>格式化的遥测报告文本</returns>
    public string FormatReport()
    {
        var report = GenerateReport();
        var sb = new StringBuilder();

        sb.AppendLine("=== Transport Fallback Telemetry ===");
        sb.AppendLine($"Active Transport: {report.ActiveTransportType} (priority {report.ActiveTransportIndex + 1})");
        sb.AppendLine($"Generated At: {report.GeneratedAt:O}");
        sb.AppendLine();

        sb.AppendLine("--- Metrics ---");
        sb.AppendLine($"Total Fallbacks: {report.Metrics.TotalFallbacks}");
        sb.AppendLine($"Avg Fallback Duration: {report.Metrics.AverageFallbackDurationMs:F1}ms");

        for (var i = 0; i < report.Metrics.ConnectionAttempts.Length; i++)
        {
            sb.AppendLine($"  Transport[{i}]: attempts={report.Metrics.ConnectionAttempts[i]}, " +
                          $"successes={report.Metrics.ConnectionSuccesses[i]}, " +
                          $"failures={report.Metrics.ConnectionFailures[i]}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Circuit Breakers ---");
        for (var i = 0; i < report.CircuitBreakers.Length; i++)
        {
            var cb = report.CircuitBreakers[i];
            sb.AppendLine($"  Transport[{i}]: state={cb.State}, " +
                          $"failures={cb.ConsecutiveFailures}" +
                          (cb.OpenedAt.HasValue ? $", openedAt={cb.OpenedAt.Value:O}" : ""));
        }

        sb.AppendLine();
        sb.AppendLine("--- Config ---");
        sb.AppendLine($"  Enabled: {report.Config.Enabled}");
        sb.AppendLine($"  HealthCheck: {report.Config.HealthCheckEnabled}");
        sb.AppendLine($"  CircuitBreaker: {report.Config.CircuitBreakerEnabled}");
        sb.AppendLine($"  ConnectTimeout: {report.Config.ConnectTimeoutMs}ms");
        sb.AppendLine($"  ChainTimeout: {report.Config.ChainTimeoutMs}ms");

        return sb.ToString();
    }
}

/// <summary>
/// 传输降级链遥测报告 — 包含活跃传输信息、指标快照、熔断器状态与配置
/// </summary>
public sealed class TransportFallbackReport
{
    /// <summary>当前活跃传输类型名</summary>
    public required string? ActiveTransportType { get; init; }
    /// <summary>当前活跃传输索引</summary>
    public required int ActiveTransportIndex { get; init; }
    /// <summary>降级指标快照</summary>
    public required TransportFallbackMetricsSnapshot Metrics { get; init; }
    /// <summary>各传输熔断器状态报告数组</summary>
    public required CircuitBreakerReport[] CircuitBreakers { get; init; }
    /// <summary>降级链配置</summary>
    public required TransportFallbackConfig Config { get; init; }
    /// <summary>报告生成时间(UTC)</summary>
    public required DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>
/// 熔断器状态报告 — 描述单个熔断器的当前状态
/// </summary>
public sealed class CircuitBreakerReport
{
    /// <summary>熔断器相位状态</summary>
    public required CircuitBreakerPhase State { get; init; }
    /// <summary>连续失败次数</summary>
    public required int ConsecutiveFailures { get; init; }
    /// <summary>熔断器打开时间,未打开时为 null</summary>
    public required DateTimeOffset? OpenedAt { get; init; }
}
