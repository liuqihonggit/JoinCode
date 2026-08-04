
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 集群循环遥测服务 — 记录各阶段耗时/成功率
/// </summary>
public interface IClusterTelemetry
{
    void RecordPhase(ClusterPhaseMetric metric);
    ClusterExecutionSummary GetSummary();
}

/// <summary>
/// 集群阶段指标
/// </summary>
public sealed class ClusterPhaseMetric
{
    public required string SessionId { get; init; }
    public required string Phase { get; init; }
    public required TimeSpan Duration { get; init; }
    public required bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// 集群执行摘要
/// </summary>
public sealed class ClusterExecutionSummary
{
    public required string SessionId { get; init; }
    public required IReadOnlyList<ClusterPhaseMetric> Phases { get; init; }
    public required TimeSpan TotalDuration { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required int WorkerCount { get; init; }
}
