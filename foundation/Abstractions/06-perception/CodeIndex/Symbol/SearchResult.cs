namespace JoinCode.Abstractions.CodeIndex;

public sealed record SearchResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int TotalCount { get; init; }
    public required long ElapsedMs { get; init; }
}
