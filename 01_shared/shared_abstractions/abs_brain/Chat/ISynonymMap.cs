namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 同义词映射接口 — 提供同义词查找能力
/// </summary>
public interface ISynonymMap
{
    /// <summary>
    /// 同义词映射条目
    /// </summary>
    IReadOnlyDictionary<string, string> Entries { get; }

    /// <summary>
    /// 尝试获取同义词映射值
    /// </summary>
    bool TryGetValue(string key, [NotNullWhen(true)] out string? value);

    /// <summary>
    /// 检查是否包含指定键
    /// </summary>
    bool ContainsKey(string key);
}
