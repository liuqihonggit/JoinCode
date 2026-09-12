namespace JoinCode.Abstractions.Models.Agent;

public sealed class ClusterExecutionOptions
{
    public int MaxConcurrency { get; init; } = int.TryParse(Environment.GetEnvironmentVariable("JCC_CLUSTER_MAX_CONCURRENCY"), out var mc) && mc > 0 ? mc : 5;
    public int ResultSummaryMaxTokens { get; init; } = 500;
    public int ClusterTimeoutSeconds { get; init; } = int.TryParse(Environment.GetEnvironmentVariable("JCC_CLUSTER_TIMEOUT_SECONDS"), out var ct) && ct > 0 ? ct : 1800;
}
