namespace CodeIndex.Benchmarks;

public sealed record TestCase
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string ExpectedLayer { get; init; }
    public required string Query { get; init; }
    public required string[] ExpectedResults { get; init; }
    public required string Description { get; init; }
    public string? QueryType { get; init; }
    public string? SourceSymbol { get; init; }
    public string? TargetSymbol { get; init; }
    public int? MaxDepth { get; init; }
}

public sealed record EvaluationResult
{
    public required string TestCaseId { get; init; }
    public required string Category { get; init; }
    public required bool Passed { get; init; }
    public required double Recall { get; init; }
    public required double Precision { get; init; }
    public required double F1 { get; init; }
    public required long ElapsedMs { get; init; }
    public required string[] ActualResults { get; init; }
    public required string[] MissingResults { get; init; }
    public required string[] ExtraResults { get; init; }
}

public sealed record EvaluationSummary
{
    public required string Layer { get; init; }
    public required int TotalCases { get; init; }
    public required int PassedCases { get; init; }
    public required double PassRate { get; init; }
    public required double AvgRecall { get; init; }
    public required double AvgPrecision { get; init; }
    public required double AvgF1 { get; init; }
    public required double P50Ms { get; init; }
    public required double P95Ms { get; init; }
    public required double P99Ms { get; init; }
}
