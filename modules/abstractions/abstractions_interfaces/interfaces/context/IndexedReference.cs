namespace JoinCode.Abstractions.Brain.Context.Resolution;

public sealed record IndexedReference {
    /// <summary>获取引用路径。</summary>
    public required string Path { get; init; }

    /// <summary>获取文件类型。</summary>
    public required string FileType { get; init; }

    /// <summary>获取关键词列表。</summary>
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>获取最后修改时间。</summary>
    public DateTimeOffset LastModified { get; init; }

    /// <summary>获取文件大小。</summary>
    public long FileSize { get; init; }

    /// <summary>创建索引引用实例。</summary>
    public static IndexedReference Create(
        string path,
        string fileType,
        IEnumerable<string> keywords,
        DateTimeOffset lastModified,
        long fileSize)
        => new() {
            Path = path,
            FileType = fileType,
            Keywords = keywords.ToList(),
            LastModified = lastModified,
            FileSize = fileSize
        };
}