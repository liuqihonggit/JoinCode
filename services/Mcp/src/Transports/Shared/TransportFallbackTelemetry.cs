namespace McpClient.Transports;

public sealed class TransportFallbackTelemetry
{
    private readonly McpTransportFallbackChain _chain;
    private readonly ILogger? _logger;

    public TransportFallbackTelemetry(McpTransportFallbackChain chain, ILogger? logger = null)
    {
        _chain = chain ?? throw new ArgumentNullException(nameof(chain));
        _logger = logger;
    }

    public TransportFallbackReport GenerateReport()
    {
        var metrics = _chain.Metrics.GetSnapshot();
        var circuitStates = new CircuitBreakerReport[_chain.CircuitBreakers.Length];

        for (var i = 0; i < _chain.CircuitBreakers.Length; i++)
        {
            var cb = _chain.CircuitBreakers[i];
            circuitStates[i] = new CircuitBreakerReport
            {
                State = cb.State,
                ConsecutiveFailures = cb.ConsecutiveFailures,
                FailureThreshold = cb.FailureThreshold,
                CoolDownPeriod = cb.CoolDownPeriod,
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
                          $"failures={cb.ConsecutiveFailures}/{cb.FailureThreshold}, " +
                          $"cooldown={cb.CoolDownPeriod.TotalSeconds}s" +
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

public sealed class TransportFallbackReport
{
    public required string? ActiveTransportType { get; init; }
    public required int ActiveTransportIndex { get; init; }
    public required TransportFallbackMetricsSnapshot Metrics { get; init; }
    public required CircuitBreakerReport[] CircuitBreakers { get; init; }
    public required TransportFallbackConfig Config { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
}

public sealed class CircuitBreakerReport
{
    public required JoinCode.Transport.CircuitBreakerState State { get; init; }
    public required int ConsecutiveFailures { get; init; }
    public required int FailureThreshold { get; init; }
    public required TimeSpan CoolDownPeriod { get; init; }
    public required DateTimeOffset? OpenedAt { get; init; }
}
