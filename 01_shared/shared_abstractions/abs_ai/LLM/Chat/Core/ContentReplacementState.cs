namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 工具结果替换状态 — 对齐 TS ContentReplacementState
/// 线程安全: 使用 ConcurrentDictionary 支持 subagent 并行访问
/// </summary>
public sealed class ContentReplacementState
{
    /// <summary>
    /// 已见过的 toolUseId 集合 — ConcurrentDictionary 模拟线程安全 HashSet
    /// </summary>
    public ConcurrentDictionary<string, byte> SeenIds { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// toolUseId → 替换字符串 映射
    /// </summary>
    public ConcurrentDictionary<string, string> Replacements { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 快照克隆 — 对齐 TS cloneContentReplacementState
    /// 先原子快照两个字典为数组，再从数组填充，保证克隆一致性
    /// 对齐 TS: new Set(source.seenIds) + new Map(source.replacements) 的原子语义
    /// </summary>
    public ContentReplacementState Clone()
    {
        var clone = new ContentReplacementState();

        // 原子快照: ConcurrentDictionary.ToArray() 返回点对点一致的快照
        var replacementsSnapshot = Replacements.ToArray();
        var seenIdsSnapshot = SeenIds.Keys.ToArray();

        // 从快照填充 Replacements — 同时填充 SeenIds（Replacements 中的 key 必定在 SeenIds 中）
        foreach (var kvp in replacementsSnapshot)
        {
            clone.Replacements.TryAdd(kvp.Key, kvp.Value);
            clone.SeenIds.TryAdd(kvp.Key, 0);
        }

        // 补充 SeenIds 中不在 Replacements 中的条目（frozen 但未替换的 ID）
        foreach (var id in seenIdsSnapshot)
            clone.SeenIds.TryAdd(id, 0);

        return clone;
    }
}
