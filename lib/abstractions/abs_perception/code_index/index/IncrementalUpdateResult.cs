namespace JoinCode.Abstractions.CodeIndex;

public sealed class IncrementalUpdateResult {
    /// <summary>获取是否已更新。</summary>
    public required bool WasUpdated { get; init; }
}

public sealed class DirectoryUpdateResult {
    /// <summary>获取已更新文件数。</summary>
    public required int UpdatedCount { get; init; }
    /// <summary>获取已跳过文件数。</summary>
    public required int SkippedCount { get; init; }
    /// <summary>获取已删除文件数。</summary>
    public required int DeletedCount { get; init; }
}
