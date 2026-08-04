
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 集群结果摘要器 — 压缩 Worker 输出，生成 Lead 可用的汇总
/// </summary>
public interface IClusterResultSummarizer
{
    Task<ClusterSummary> SummarizeAsync(ClusterSummaryContext context, CancellationToken ct = default);
}

/// <summary>
/// 集群摘要上下文
/// </summary>
public sealed class ClusterSummaryContext
{
    public required string Objective { get; init; }
    public required IReadOnlyList<WorkerOutput> WorkerOutputs { get; init; }
    public int MaxSummaryTokens { get; init; } = 500;
}

/// <summary>
/// Worker 输出
/// </summary>
public sealed class WorkerOutput
{
    public required string SubTaskId { get; init; }
    public required string Title { get; init; }
    public required string Output { get; init; }
    public bool IsSuccess { get; init; }
    public double GradingScore { get; init; }
}

/// <summary>
/// 集群摘要结果
/// </summary>
public sealed class ClusterSummary
{
    public required string Summary { get; init; }
    public required IReadOnlyList<WorkerSummary> WorkerSummaries { get; init; }
    public required double OverallScore { get; init; }
}

/// <summary>
/// Worker 摘要
/// </summary>
public sealed class WorkerSummary
{
    public required string SubTaskId { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required double Score { get; init; }
}
