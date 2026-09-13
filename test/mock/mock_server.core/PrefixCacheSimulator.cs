namespace MockServer.Core;

/// <summary>
/// 前缀缓存模拟器 — 模拟真实 LLM 的 Prefix Cache 语义
///
/// 真实 LLM Prefix Cache 行为:
/// - 缓存键: 请求消息历史的"前缀"（system prompt + 早期消息）
/// - 命中条件: 新请求的前缀以已缓存前缀开头（或已缓存前缀以新前缀开头）
/// - 命中粒度: 按已缓存长度比例计算 cache_read / cache_creation tokens
///
/// 与旧实现的区别:
/// - 旧: _seenPrefixes.Contains(prefix) — 完整字符串相等, 无法模拟 prefix 增长
/// - 新: 找最长匹配前缀, 按长度比例计算部分命中
///
/// 关键场景:
/// 1. 完全相同 prefix → 完全命中 (cacheRead = inputTokens, cacheCreation = 0)
/// 2. 新 prefix 以已缓存 prefix 为前缀（多轮对话 prefix 增长）→ 部分命中
/// 3. 已缓存 prefix 以新 prefix 为前缀（新请求较短）→ 完全命中
/// 4. 完全无交集 → 完全 miss
/// </summary>
public sealed class PrefixCacheSimulator : ICacheSimulator
{
    private readonly List<string> _seenPrefixes = [];
    private readonly Func<JsonElement, string> _prefixExtractor;
    private readonly Func<JsonElement, int> _tokenEstimator;
    private readonly object _lock = new();

    public PrefixCacheSimulator(
        Func<JsonElement, string> prefixExtractor,
        Func<JsonElement, int> tokenEstimator)
    {
        ArgumentNullException.ThrowIfNull(prefixExtractor);
        ArgumentNullException.ThrowIfNull(tokenEstimator);
        _prefixExtractor = prefixExtractor;
        _tokenEstimator = tokenEstimator;
    }

    public CacheStats ComputeCacheStats(JsonElement request)
    {
        var prefix = _prefixExtractor(request);
        var inputTokens = _tokenEstimator(request);
        var outputTokens = 50;
        int cacheReadTokens;
        int cacheCreationTokens;

        lock (_lock)
        {
            var bestMatch = FindBestPrefixMatch(prefix);

            if (bestMatch is null)
            {
                // 完全无交集 → 完全 miss
                cacheReadTokens = 0;
                cacheCreationTokens = inputTokens;
            }
            else
            {
                // 命中: 按已缓存部分的长度比例计算
                // cachedLength = min(bestMatch.Length, prefix.Length) — 已缓存部分覆盖的字符数
                var cachedLength = Math.Min(bestMatch.Length, prefix.Length);
                var totalLength = Math.Max(prefix.Length, 1);
                var cachedRatio = (double)cachedLength / totalLength;
                cacheReadTokens = (int)Math.Round(inputTokens * cachedRatio);
                cacheCreationTokens = inputTokens - cacheReadTokens;
            }

            // 始终将当前 prefix 加入缓存（去重），用于后续请求的前缀匹配
            if (!_seenPrefixes.Contains(prefix))
                _seenPrefixes.Add(prefix);
        }

        return new CacheStats
        {
            CacheCreationTokens = cacheCreationTokens,
            CacheReadTokens = cacheReadTokens,
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>
    /// 在已缓存 prefix 列表中找出与当前 prefix 有前缀关系的最长一项。
    /// 前缀关系: prefix.StartsWith(stored) 或 stored.StartsWith(prefix)。
    /// 返回 null 表示无任何匹配。
    /// </summary>
    private string? FindBestPrefixMatch(string prefix)
    {
        string? best = null;
        foreach (var stored in _seenPrefixes)
        {
            var isMatch = prefix.StartsWith(stored, StringComparison.Ordinal)
                       || stored.StartsWith(prefix, StringComparison.Ordinal);
            if (!isMatch) continue;

            if (best is null || stored.Length > best.Length)
                best = stored;
        }
        return best;
    }

    public void ResetCache()
    {
        lock (_lock) { _seenPrefixes.Clear(); }
    }
}
