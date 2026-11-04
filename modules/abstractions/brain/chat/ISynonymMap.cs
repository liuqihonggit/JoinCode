namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 同义词映射接口 — 提供同义词查找能力
/// </summary>
public interface ISynonymMap {
    /// <summary>
    /// 同义词条目数量
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 获取所有同义词条目的快照 — 用于遍历
    /// </summary>
    IReadOnlyDictionary<string, string> GetAllEntries();

    /// <summary>
    /// 尝试获取同义词映射值
    /// </summary>
    bool TryGetValue(string key, [NotNullWhen(true)] out string? value);

    /// <summary>
    /// 检查是否包含指定键
    /// </summary>
    bool ContainsKey(string key);
}