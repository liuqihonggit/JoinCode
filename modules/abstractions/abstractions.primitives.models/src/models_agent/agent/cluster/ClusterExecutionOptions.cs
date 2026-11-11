namespace JoinCode.Abstractions.Models.Agent;

public sealed class ClusterExecutionOptions {
    /// <summary>获取最大并发数。</summary>
    public int MaxConcurrency { get; init; } = int.TryParse(Environment.GetEnvironmentVariable("JCC_CLUSTER_MAX_CONCURRENCY"), out var mc) && mc > 0 ? mc : 5;
    /// <summary>获取结果摘要最大令牌数。</summary>
    public int ResultSummaryMaxTokens { get; init; } = 500;
    /// <summary>获取集群超时时间(秒)。</summary>
    public int ClusterTimeoutSeconds { get; init; } = int.TryParse(Environment.GetEnvironmentVariable("JCC_CLUSTER_TIMEOUT_SECONDS"), out var ct) && ct > 0 ? ct : 1800;
}