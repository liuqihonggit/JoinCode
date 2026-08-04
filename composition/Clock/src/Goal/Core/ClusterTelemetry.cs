
namespace Core.Goal;

[Register]
public sealed partial class ClusterTelemetry : IClusterTelemetry
{
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly ILogger<ClusterTelemetry>? _logger;

    private readonly ConcurrentBag<ClusterPhaseMetric> _phases = [];

    public void RecordPhase(ClusterPhaseMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        _phases.Add(metric);

        _telemetryService?.RecordCount("cluster.phase.count", new Dictionary<string, string>
        {
            ["phase"] = metric.Phase,
            ["success"] = metric.IsSuccess.ToString(),
        }, "count", "Cluster phase execution count");

        _telemetryService?.RecordHistogram("cluster.phase.duration_ms", metric.Duration.TotalMilliseconds, new Dictionary<string, string>
        {
            ["phase"] = metric.Phase,
        }, "ms", "Cluster phase duration in milliseconds");

        _logger?.LogInformation("[ClusterTelemetry] Phase={Phase} Duration={Duration}ms Success={Success}",
            metric.Phase, metric.Duration.TotalMilliseconds, metric.IsSuccess);
    }

    public ClusterExecutionSummary GetSummary()
    {
        var phaseList = _phases.ToArray();
        var successCount = phaseList.Count(p => p.IsSuccess);
        var failureCount = phaseList.Count(p => !p.IsSuccess);

        return new ClusterExecutionSummary
        {
            SessionId = phaseList.FirstOrDefault()?.SessionId ?? "",
            Phases = phaseList,
            TotalDuration = phaseList.Any() ? phaseList.Aggregate(TimeSpan.Zero, (acc, p) => acc + p.Duration) : TimeSpan.Zero,
            SuccessCount = successCount,
            FailureCount = failureCount,
            WorkerCount = phaseList.Count(p => p.Phase == "worker"),
        };
    }
}
