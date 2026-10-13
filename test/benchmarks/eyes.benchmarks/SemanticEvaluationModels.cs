namespace JoinCode.CodeIndex.Benchmarks;

/// <summary>
/// 单题语义搜索评估结果 — 记录命中排名、分数、返回结果数。
/// </summary>
public sealed record SemanticEvaluationResult {
    /// <summary>题目 ID。</summary>
    public required string TestCaseId { get; init; }
    /// <summary>查询字符串。</summary>
    public required string Query { get; init; }
    /// <summary>期望文件名。</summary>
    public required string ExpectedFile { get; init; }
    /// <summary>是否命中（Top-K 内有匹配）。</summary>
    public required bool Hit { get; init; }
    /// <summary>命中排名（1-based），未命中为 0。</summary>
    public required int Rank { get; init; }
    /// <summary>命中块的相似度分数，未命中为 0。</summary>
    public required float Score { get; init; }
    /// <summary>返回结果总数。</summary>
    public required int ResultCount { get; init; }
    /// <summary>耗时毫秒。</summary>
    public required long ElapsedMs { get; init; }
    /// <summary>Top-K 返回的文件路径列表（用于调试）。</summary>
    public required string[] TopKFiles { get; init; }
}

/// <summary>
/// 语义搜索评估汇总 — HitRate、MRR、Recall@K、延迟百分位。
/// </summary>
public sealed record SemanticEvaluationSummary {
    /// <summary>评估层名称。</summary>
    public required string Layer { get; init; }
    /// <summary>总题数。</summary>
    public required int TotalCases { get; init; }
    /// <summary>命中题数。</summary>
    public required int HitCount { get; init; }
    /// <summary>命中率 = HitCount / TotalCases。</summary>
    public required double HitRate { get; init; }
    /// <summary>MRR (Mean Reciprocal Rank) = avg(1/rank_i)。</summary>
    public required double Mrr { get; init; }
    /// <summary>平均返回结果数。</summary>
    public required double AvgResultCount { get; init; }
    /// <summary>P50 延迟毫秒。</summary>
    public required double P50Ms { get; init; }
    /// <summary>P95 延迟毫秒。</summary>
    public required double P95Ms { get; init; }
    /// <summary>P99 延迟毫秒。</summary>
    public required double P99Ms { get; init; }
    /// <summary>未命中题目 ID 列表。</summary>
    public required string[] MissedIds { get; init; }
}
