namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed record IndexedReference
{
    public required string Path { get; init; }

    public required string FileType { get; init; }

    public required IReadOnlyList<string> Keywords { get; init; }

    public DateTimeOffset LastModified { get; init; }

    public long FileSize { get; init; }

    public static IndexedReference Create(
        string path,
        string fileType,
        IEnumerable<string> keywords,
        DateTimeOffset lastModified,
        long fileSize)
        => new()
        {
            Path = path,
            FileType = fileType,
            Keywords = keywords.ToList(),
            LastModified = lastModified,
            FileSize = fileSize
        };
}
