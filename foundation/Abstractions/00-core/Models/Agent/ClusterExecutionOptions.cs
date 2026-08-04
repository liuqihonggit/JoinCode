namespace JoinCode.Abstractions.Models.Agent;

public sealed class ClusterExecutionOptions
{
    public int MaxConcurrency { get; init; } = 5;
    public int ResultSummaryMaxTokens { get; init; } = 500;
}
