namespace JoinCode.Abstractions.CodeIndex;

public sealed class IncrementalUpdateResult
{
    public required bool WasUpdated { get; init; }
}

public sealed class DirectoryUpdateResult
{
    public required int UpdatedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required int DeletedCount { get; init; }
}
