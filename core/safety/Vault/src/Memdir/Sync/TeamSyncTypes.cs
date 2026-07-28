
namespace Memdir.Sync;

/// <summary>
/// 冲突类型枚举
/// 定义团队记忆同步时可能出现的冲突类型
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// 内容不一致 - 本地与远程内容不同
    /// </summary>
    [EnumValue("contentMismatch")]
    ContentMismatch,

    /// <summary>
    /// 本地已删除 - 本地已删除但远程仍存在
    /// </summary>
    [EnumValue("deletedLocally")]
    DeletedLocally,

    /// <summary>
    /// 远程已删除 - 远程已删除但本地仍存在
    /// </summary>
    [EnumValue("deletedRemotely")]
    DeletedRemotely
}

/// <summary>
/// 团队记忆冲突记录
/// 描述同步过程中发现的具体冲突
/// </summary>
public sealed record TeamMemoryConflict
{
    /// <summary>
    /// 冲突涉及的记忆 ID
    /// </summary>
    [JsonPropertyName("memoryId")]
    public required string MemoryId { get; init; }

    /// <summary>
    /// 本地版本内容摘要
    /// </summary>
    [JsonPropertyName("localVersion")]
    public string? LocalVersion { get; init; }

    /// <summary>
    /// 远程版本内容摘要
    /// </summary>
    [JsonPropertyName("remoteVersion")]
    public string? RemoteVersion { get; init; }

    /// <summary>
    /// 冲突类型
    /// </summary>
    [JsonPropertyName("conflictType")]
    public ConflictType ConflictType { get; init; }

    /// <summary>
    /// 冲突发现时间
    /// </summary>
    [JsonPropertyName("detectedAt")]
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 团队同步状态
/// 描述某个团队的记忆同步状态
/// </summary>
public sealed record TeamSyncStatus
{
    /// <summary>
    /// 团队 ID
    /// </summary>
    [JsonPropertyName("teamId")]
    public required string TeamId { get; init; }

    /// <summary>
    /// 最后同步时间
    /// </summary>
    [JsonPropertyName("lastSyncAt")]
    public DateTime? LastSyncAt { get; init; }

    /// <summary>
    /// 是否正在监视目录变更
    /// </summary>
    [JsonPropertyName("isWatching")]
    public bool IsWatching { get; init; }

    /// <summary>
    /// 已同步的记忆数量
    /// </summary>
    [JsonPropertyName("syncedMemoryCount")]
    public int SyncedMemoryCount { get; init; }

    /// <summary>
    /// 是否存在未解决的冲突
    /// </summary>
    [JsonPropertyName("hasConflicts")]
    public bool HasConflicts { get; init; }

    /// <summary>
    /// 冲突列表
    /// </summary>
    [JsonPropertyName("conflicts")]
    public ImmutableList<TeamMemoryConflict> Conflicts { get; init; } = ImmutableList<TeamMemoryConflict>.Empty;
}
