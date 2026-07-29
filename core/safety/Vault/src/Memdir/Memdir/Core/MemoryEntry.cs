
namespace Core.Memdir;

/// <summary>
/// 记忆条目模型
/// 包含类型、内容、相关性分数和 TTL
/// </summary>
public sealed record MemoryEntry
{
    /// <summary>
    /// 记忆唯一标识符
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 记忆类型
    /// </summary>
    public required MemoryType Type { get; init; }

    /// <summary>
    /// 记忆内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 记忆标题/摘要
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// 标签列表
    /// </summary>
    public ImmutableList<string> Tags { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// 来源信息
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后访问时间
    /// </summary>
    public DateTime LastAccessedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 访问次数
    /// </summary>
    public int AccessCount { get; init; }

    /// <summary>
    /// 相关性分数 (0.0 - 1.0)
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// TTL (生存时间)
    /// </summary>
    public TimeSpan Ttl { get; init; }

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// 是否已归档
    /// </summary>
    public bool IsArchived { get; init; }

    /// <summary>
    /// 归档时间
    /// </summary>
    public DateTime? ArchivedAt { get; init; }

    /// <summary>
    /// 元数据
    /// </summary>
    public ImmutableDictionary<string, string> Metadata { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// 关联的记忆 ID 列表
    /// </summary>
    public ImmutableList<string> RelatedMemoryIds { get; init; } = ImmutableList<string>.Empty;

    /// <summary>
    /// 创建新的记忆条目
    /// </summary>
    public static MemoryEntry Create(
        MemoryType type,
        string content,
        string? title = null,
        IEnumerable<string>? tags = null,
        string? source = null,
        TimeSpan? ttl = null,
        ImmutableDictionary<string, string>? metadata = null,
        DateTime? now = null)
    {
        var actualTtl = ttl ?? type.GetDefaultTtl();
        var currentTime = now ?? DateTime.UtcNow;

        return new MemoryEntry
        {
            Id = Guid.NewGuid().ToString("N")[..16],
            Type = type,
            Content = content,
            Title = title,
            Tags = tags?.ToImmutableList() ?? ImmutableList<string>.Empty,
            Source = source,
            CreatedAt = currentTime,
            LastAccessedAt = currentTime,
            AccessCount = 0,
            RelevanceScore = type.GetBaseRelevanceWeight(),
            Ttl = actualTtl,
            ExpiresAt = currentTime.Add(actualTtl),
            Metadata = metadata ?? ImmutableDictionary<string, string>.Empty
        };
    }

    /// <summary>
    /// 更新访问信息
    /// </summary>
    public MemoryEntry WithAccessed(DateTime? now = null)
    {
        return this with
        {
            LastAccessedAt = now ?? DateTime.UtcNow,
            AccessCount = AccessCount + 1
        };
    }

    /// <summary>
    /// 更新相关性分数
    /// </summary>
    public MemoryEntry WithRelevanceScore(double score)
    {
        return this with { RelevanceScore = Math.Clamp(score, 0.0, 1.0) };
    }

    /// <summary>
    /// 归档记忆
    /// </summary>
    public MemoryEntry WithArchived(DateTime? now = null)
    {
        return this with
        {
            IsArchived = true,
            ArchivedAt = now ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// 检查是否已过期
    /// </summary>
    public bool IsExpired(DateTime? now = null)
    {
        var currentTime = now ?? DateTime.UtcNow;
        return ExpiresAt.HasValue && ExpiresAt.Value < currentTime;
    }

    /// <summary>
    /// 获取剩余生存时间
    /// </summary>
    public TimeSpan GetRemainingTtl(DateTime? now = null)
    {
        var currentTime = now ?? DateTime.UtcNow;
        if (!ExpiresAt.HasValue) return TimeSpan.Zero;
        return ExpiresAt.Value > currentTime ? ExpiresAt.Value - currentTime : TimeSpan.Zero;
    }
}
