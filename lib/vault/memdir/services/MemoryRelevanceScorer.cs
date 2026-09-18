namespace Core.Memdir;

/// <summary>
/// 记忆相关性评分器 — 计算记忆与查询的相关性分数和匹配原因
/// </summary>
internal sealed class MemoryRelevanceScorer
{
    private readonly IClockService _clock;

    /// <summary>
    /// 构造记忆相关性评分器实例
    /// </summary>
    /// <param name="clock">时钟服务，用于计算时间衰减</param>
    public MemoryRelevanceScorer(IClockService clock)
    {
        _clock = clock;
    }

    /// <summary>
    /// 计算记忆与查询的高级相关性分数
    /// </summary>
    /// <param name="memory">记忆条目</param>
    /// <param name="query">查询字符串</param>
    /// <returns>相关性分数，越高越相关</returns>
    public double CalculateAdvancedRelevanceScore(MemoryEntry memory, string query)
    {
        var score = 0.0;
        var queryWords = QueryWordHelper.ExtractQueryWords(query);
        var contentSpan = memory.Content.AsSpan();

        for (var i = 0; i < queryWords.Length; i++)
        {
            var wordSpan = queryWords[i].AsSpan();
            if (QueryWordHelper.ContainsOrdinalIgnoreCase(contentSpan, wordSpan))
            {
                score += 1.0;

                if (QueryWordHelper.ContainsWholeWordOrdinalIgnoreCase(contentSpan, wordSpan))
                {
                    score += 0.5;
                }
            }
        }

        // 标签匹配（权重更高）— AC 自动机一次扫描
        var queryWordAc = AhoCorasick.CreateBool(queryWords, ignoreCase: true);
        foreach (var tag in memory.Tags)
        {
            if (queryWordAc.ContainsAny(tag.AsSpan()))
            {
                score += 2.0;
            }
        }

        // 类型匹配
        if (queryWordAc.ContainsAny(memory.Type.ToString().AsSpan()))
        {
            score += 1.5;
        }

        // 访问频率加权
        score *= (1 + Math.Log(1 + memory.AccessCount));

        // 时间衰减（越新的记忆分数越高）
        var daysSinceCreated = (_clock.GetUtcNow() - memory.CreatedAt).TotalDays;
        score *= Math.Exp(-daysSinceCreated / 30.0);

        return score;
    }

    /// <summary>
    /// 获取记忆与查询的匹配原因描述
    /// </summary>
    /// <param name="memory">记忆条目</param>
    /// <param name="query">查询字符串</param>
    /// <returns>匹配原因描述，无匹配时为 null</returns>
    public string? GetMatchReason(MemoryEntry memory, string query)
    {
        var reasons = new List<string>();
        var queryWords = QueryWordHelper.ExtractQueryWords(query);
        var queryWordAc = AhoCorasick.CreateBool(queryWords, ignoreCase: true);

        if (queryWordAc.ContainsAny(memory.Content.AsSpan()))
        {
            reasons.Add(L.T(StringKey.VaultMatchReasonContent));
        }

        // 检查标签匹配
        if (memory.Tags.Any(t => queryWordAc.ContainsAny(t.AsSpan())))
        {
            reasons.Add(L.T(StringKey.VaultMatchReasonTag));
        }

        // 检查类型匹配
        if (queryWordAc.ContainsAny(memory.Type.ToString().AsSpan()))
        {
            reasons.Add(L.T(StringKey.VaultMatchReasonType));
        }

        return reasons.Count > 0 ? string.Join(", ", reasons) : null;
    }
}
