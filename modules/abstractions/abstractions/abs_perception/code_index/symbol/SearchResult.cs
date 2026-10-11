namespace JoinCode.Abstractions.CodeIndex;

public sealed record SearchResult<T> {
    /// <summary>获取结果项列表。</summary>
    public required IReadOnlyList<T> Items { get; init; }
    /// <summary>获取结果总数。</summary>
    public required int TotalCount { get; init; }
    /// <summary>获取耗时(毫秒)。</summary>
    public required long ElapsedMs { get; init; }
}