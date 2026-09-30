namespace JoinCode.Abstractions.CodeIndex;

public sealed class IncrementalUpdateResult {
    /// <summary>获取是否已更新。</summary>
    public required bool WasUpdated { get; init; }
    /// <summary>提取结果 — WasUpdated=true 且文件存在时填充（含 Chunks + ParentDocuments），null 表示未变更或文件已删除。</summary>
    public ExtractionResult? Extraction { get; init; }
    /// <summary>文件是否被删除 — true 表示文件不存在且已从索引移除。</summary>
    public bool WasDeleted { get; init; }
}

public sealed class DirectoryUpdateResult {
    /// <summary>获取已更新文件数。</summary>
    public required int UpdatedCount { get; init; }
    /// <summary>获取已跳过文件数。</summary>
    public required int SkippedCount { get; init; }
    /// <summary>获取已删除文件数。</summary>
    public required int DeletedCount { get; init; }
}
