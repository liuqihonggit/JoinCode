namespace JoinCode.Abstractions.Interfaces;

public sealed class ReleaseInfo {
    /// <summary>获取版本号。</summary>
    public required string Version { get; init; }
    /// <summary>获取发布说明。</summary>
    public required string Notes { get; init; }
    /// <summary>获取发布时间。</summary>
    public required DateTime PublishedAt { get; init; }
}

public interface IReleaseNotesService {
    /// <summary>异步获取最近的发布信息列表。</summary>
    Task<IReadOnlyList<ReleaseInfo>> GetRecentReleasesAsync(int count = 5, CancellationToken ct = default);
}