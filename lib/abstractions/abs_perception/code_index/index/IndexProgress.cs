namespace JoinCode.Abstractions.CodeIndex;

public sealed record IndexProgress {
    /// <summary>获取当前进度。</summary>
    public required int Current { get; init; }
    /// <summary>获取总数。</summary>
    public required int Total { get; init; }
}

public sealed record BuildIndexResult {
    /// <summary>获取已更新数量。</summary>
    public required int UpdatedCount { get; init; }
    /// <summary>获取已跳过数量。</summary>
    public required int SkippedCount { get; init; }
    /// <summary>获取已删除数量。</summary>
    public required int DeletedCount { get; init; }
}