namespace JoinCode.Abstractions.CodeIndex;

public sealed record IndexProgress
{
    public required int Current { get; init; }
    public required int Total { get; init; }
}

public sealed record BuildIndexResult
{
    public required int UpdatedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required int DeletedCount { get; init; }
}
