namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具锚点条目 — 一个工具的锚点关键词及其向量化表示
/// </summary>
public sealed record ToolAnchorEntry {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取锚点关键词集合。</summary>
    public required string[] Anchors { get; init; }
    /// <summary>获取锚点合并文本（用于分词和向量化）。</summary>
    public string AnchorText => string.Join(' ', Anchors);
}

/// <summary>
/// 工具锚点匹配结果 — 用户问题与工具锚点的匹配结果
/// </summary>
public sealed record ToolAnchorMatch {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取余弦相似度分数（0~1）。</summary>
    public float Score { get; init; }
    /// <summary>获取命中的锚点关键词。</summary>
    public required string[] HitAnchors { get; init; }
}

/// <summary>
/// 工具锚点索引 — 收集所有工具锚点，用词袋模型 + 余弦相似度匹配用户问题
/// AOT 兼容，无需外部 embedding 模型，纯本地计算
/// </summary>
public sealed class ToolAnchorIndex {
    private readonly List<ToolAnchorEntry> _entries = [];
    private Dictionary<string, int> _vocabulary = new(StringComparer.OrdinalIgnoreCase);
    private FrozenDictionary<string, float[]> _anchorVectors = FrozenDictionary<string, float[]>.Empty;
    private bool _sealed;

    /// <summary>获取已注册的锚点条目数。</summary>
    public int Count => _entries.Count;

    /// <summary>获取是否已密封（构建词汇表和向量后不可再注册）。</summary>
    public bool IsSealed => _sealed;

    /// <summary>注册工具锚点。</summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="anchors">锚点关键词集合</param>
    public void Register(string toolName, params string[] anchors) {
        if (_sealed) throw new InvalidOperationException("锚点索引已密封，不可再注册");
        _entries.Add(new ToolAnchorEntry { ToolName = toolName, Anchors = anchors });
    }

    /// <summary>密封索引 — 构建词汇表和锚点向量，之后可执行匹配。</summary>
    public void Seal() {
        if (_sealed) return;

        var vocabSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries) {
            foreach (var token in Tokenize(entry.AnchorText)) {
                vocabSet.Add(token);
            }
        }

        _vocabulary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var idx = 0;
        foreach (var word in vocabSet) {
            _vocabulary[word] = idx++;
        }

        var vectorDict = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries) {
            vectorDict[entry.ToolName] = Vectorize(entry.AnchorText);
        }
        _anchorVectors = vectorDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _sealed = true;
    }

    /// <summary>
    /// 匹配用户问题 — 返回按相似度降序排列的匹配结果
    /// </summary>
    /// <param name="query">用户问题</param>
    /// <param name="topK">返回前 K 个结果</param>
    /// <param name="threshold">相似度阈值（低于此值不返回）</param>
    /// <returns>匹配结果列表（按分数降序）</returns>
    public IReadOnlyList<ToolAnchorMatch> Match(string query, int topK = 3, float threshold = 0.1f) {
        if (!_sealed || _entries.Count == 0) return [];

        var queryVec = Vectorize(query);
        var queryNorm = L2Norm(queryVec);
        if (queryNorm < 1e-6f) return [];

        var results = new List<ToolAnchorMatch>(_entries.Count);
        foreach (var entry in _entries) {
            if (!_anchorVectors.TryGetValue(entry.ToolName, out var anchorVec)) continue;

            var score = CosineSimilarity(queryVec, anchorVec);
            if (score < threshold) continue;

            var hitAnchors = entry.Anchors
                .Where(a => query.Contains(a, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            results.Add(new ToolAnchorMatch {
                ToolName = entry.ToolName,
                Score = score,
                HitAnchors = hitAnchors
            });
        }

        return results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>获取所有已注册的锚点条目。</summary>
    public IReadOnlyList<ToolAnchorEntry> Entries => _entries;

    private static IEnumerable<string> Tokenize(string text) {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts) {
            yield return part;
        }
    }

    private float[] Vectorize(string text) {
        var vec = new float[_vocabulary.Count];
        foreach (var token in Tokenize(text)) {
            if (_vocabulary.TryGetValue(token, out var idx)) {
                vec[idx]++;
            }
        }
        return vec;
    }

    private static float CosineSimilarity(float[] a, float[] b) {
        if (a.Length != b.Length || a.Length == 0) return 0f;
        float dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++) {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom < 1e-6f ? 0f : dot / denom;
    }

    private static float L2Norm(float[] a) {
        float sum = 0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * a[i];
        return MathF.Sqrt(sum);
    }
}
