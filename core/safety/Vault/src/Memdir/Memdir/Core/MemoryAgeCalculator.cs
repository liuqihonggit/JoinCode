namespace Core.Memdir;

/// <summary>
/// 记忆老化计算器
/// 根据 TTL 计算权重衰减
/// </summary>
public interface IMemoryAgeCalculator
{
    /// <summary>
    /// 计算老化后的相关性分数
    /// </summary>
    /// <param name="entry">记忆条目</param>
    /// <param name="now">当前时间</param>
    /// <returns>老化后的相关性分数</returns>
    double CalculateAgedRelevance(MemoryEntry entry, DateTime? now = null);

    /// <summary>
    /// 检查是否应该归档
    /// </summary>
    /// <param name="entry">记忆条目</param>
    /// <param name="now">当前时间</param>
    /// <returns>是否应该归档</returns>
    bool ShouldArchive(MemoryEntry entry, DateTime? now = null);
}

/// <summary>
/// 记忆老化计算器实现
/// 使用指数衰减模型
/// </summary>
[Register]
public sealed partial class MemoryAgeCalculator : IMemoryAgeCalculator
{
    private readonly MemoryAgeOptions _options;

    public MemoryAgeCalculator(MemoryAgeOptions? options = null)
    {
        _options = options ?? MemoryAgeOptions.Default;
    }

    /// <inheritdoc />
    public double CalculateAgedRelevance(MemoryEntry entry, DateTime? now = null)
    {
        var currentTime = now ?? DateTime.UtcNow;
        var age = currentTime - entry.CreatedAt;

        // 如果已过期，返回最低分数
        if (entry.IsExpired(currentTime))
        {
            return _options.MinRelevanceScore;
        }

        // 计算衰减因子 (指数衰减)
        var decayFactor = Math.Exp(-_options.DecayRate * age.TotalDays);

        // 应用访问频率加权
        var accessBonus = Math.Min(entry.AccessCount * _options.AccessBonusPerCount, _options.MaxAccessBonus);

        // 计算最终分数
        var agedScore = entry.RelevanceScore * decayFactor + accessBonus;

        // 确保在有效范围内
        return Math.Clamp(agedScore, _options.MinRelevanceScore, 1.0);
    }

    /// <inheritdoc />
    public bool ShouldArchive(MemoryEntry entry, DateTime? now = null)
    {
        var currentTime = now ?? DateTime.UtcNow;

        // 已归档的不需要再次归档
        if (entry.IsArchived)
        {
            return false;
        }

        // 检查是否已过期
        if (entry.IsExpired(currentTime))
        {
            return true;
        }

        // 检查相关性分数是否低于归档阈值
        var agedScore = CalculateAgedRelevance(entry, currentTime);
        if (agedScore < _options.ArchiveThreshold)
        {
            return true;
        }

        // 检查是否超过最大年龄
        var age = currentTime - entry.CreatedAt;
        if (age > _options.MaxAge)
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// 记忆老化选项
/// </summary>
public sealed record MemoryAgeOptions
{
    /// <summary>
    /// 衰减率 (每天)
    /// </summary>
    public double DecayRate { get; init; } = 0.01; // 约 69 天衰减到 50%

    /// <summary>
    /// 每次访问的加分
    /// </summary>
    public double AccessBonusPerCount { get; init; } = 0.02;

    /// <summary>
    /// 最大访问加分
    /// </summary>
    public double MaxAccessBonus { get; init; } = 0.2;

    /// <summary>
    /// 最小相关性分数
    /// </summary>
    public double MinRelevanceScore { get; init; } = 0.1;

    /// <summary>
    /// 归档阈值
    /// </summary>
    public double ArchiveThreshold { get; init; } = 0.2;

    /// <summary>
    /// 最大年龄
    /// </summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(365);

    /// <summary>
    /// 默认选项
    /// </summary>
    public static MemoryAgeOptions Default => new();
}
