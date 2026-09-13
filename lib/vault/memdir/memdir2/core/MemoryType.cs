namespace Core.Memdir;

/// <summary>
/// 记忆类型枚举
/// 定义四种记忆分类：用户、反馈、项目、参考
/// </summary>
public enum MemoryType
{
    /// <summary>
    /// 用户记忆 - 用户直接输入或确认的信息
    /// </summary>
    [EnumValue("user")]
    User,

    /// <summary>
    /// 反馈记忆 - 用户对AI输出的反馈
    /// </summary>
    [EnumValue("feedback")]
    Feedback,

    /// <summary>
    /// 项目记忆 - 项目相关的上下文信息
    /// </summary>
    [EnumValue("project")]
    Project,

    /// <summary>
    /// 参考记忆 - 参考资料、文档等
    /// </summary>
    [EnumValue("reference")]
    Reference
}

/// <summary>
/// 记忆类型扩展方法
/// </summary>
public static class MemoryTypeExtensions
{
    private static readonly FrozenDictionary<string, MemoryType> __reverseMap = new Dictionary<string, MemoryType>
    {
        ["user"] = MemoryType.User,
        ["feedback"] = MemoryType.Feedback,
        ["project"] = MemoryType.Project,
        ["reference"] = MemoryType.Reference
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<MemoryType, TimeSpan> DefaultTtls = new Dictionary<MemoryType, TimeSpan>
    {
        [MemoryType.User] = TimeSpan.FromDays(365),
        [MemoryType.Feedback] = TimeSpan.FromDays(180),
        [MemoryType.Project] = TimeSpan.FromDays(90),
        [MemoryType.Reference] = TimeSpan.FromDays(30)
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<MemoryType, double> BaseRelevanceWeights = new Dictionary<MemoryType, double>
    {
        [MemoryType.User] = 1.0,
        [MemoryType.Feedback] = 0.9,
        [MemoryType.Project] = 0.8,
        [MemoryType.Reference] = 0.6
    }.ToFrozenDictionary();

    /// <summary>
    /// 从字符串值解析枚举成员
    /// </summary>
    public static MemoryType? FromValue(string? value)
        => value is not null && __reverseMap.TryGetValue(value, out var result) ? result : null;

    /// <summary>
    /// 获取记忆类型名称
    /// </summary>
    public static string GetName(this MemoryType type)
    {
        return type.ToString();
    }

    /// <summary>
    /// 获取默认 TTL
    /// </summary>
    public static TimeSpan GetDefaultTtl(this MemoryType type)
    {
        return DefaultTtls.GetValueOrDefault(type, TimeSpan.FromDays(30));
    }

    /// <summary>
    /// 获取基础相关性权重
    /// </summary>
    public static double GetBaseRelevanceWeight(this MemoryType type)
    {
        return BaseRelevanceWeights.GetValueOrDefault(type, 0.5);
    }
}
