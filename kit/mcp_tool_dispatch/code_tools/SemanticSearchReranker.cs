namespace McpToolDispatch;

/// <summary>
/// 语义搜索重排序器 — 多信号重排序 + 图谱加权。
/// <para>重排序信号: 向量余弦(0.5) + 关键词重叠(0.3) + 符号名匹配(0.2)。</para>
/// <para>图谱加权: 调用关系(+0.05) + 同文件(+0.03) + 同命名空间(+0.02)。</para>
/// <para>设计见 ADR 0125。</para>
/// </summary>
public static class SemanticSearchReranker {

    private static readonly FrozenSet<string> StopWords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "public", "private", "protected", "internal", "static", "readonly", "sealed",
        "void", "var", "return", "if", "else", "for", "foreach", "while", "switch",
        "new", "class", "struct", "record", "enum", "interface", "namespace", "using",
        "get", "set", "this", "base", "null", "true", "false", "async", "await",
        "task", "string", "int", "float", "double", "bool", "char", "byte", "long",
        "the", "a", "an", "is", "in", "out", "ref", "of", "to", "and", "or", "not"
    );

    /// <summary>
    /// 重排序 + 图谱加权 → 取 topK。
    /// <para>流程: oversample 候选 → 多信号重排序 → 图谱加权 → 取 topK。</para>
    /// </summary>
    /// <param name="query">原始查询文本。</param>
    /// <param name="candidates">oversample 候选结果（topK×3）。</param>
    /// <param name="topK">最终返回数。</param>
    /// <param name="callGraph">调用图（可选，null 跳过图谱加权）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>重排序+加权后的 topK 结果。</returns>
    public static async Task<IReadOnlyList<ChunkSearchResult>> RerankAsync(
        string query,
        IReadOnlyList<ChunkSearchResult> candidates,
        int topK,
        ICallGraph? callGraph = null,
        CancellationToken ct = default) {
        if (candidates.Count <= topK) return candidates;

        var queryTerms = ExtractTerms(query);
        var reranked = candidates
            .Select(c => c with { Score = ComputeRerankScore(c, queryTerms) })
            .OrderByDescending(c => c.Score)
            .Take(topK * 2)
            .ToList();

        if (callGraph is not null) {
            reranked = await ApplyGraphWeightsAsync(reranked, callGraph, ct).ConfigureAwait(false);
        }

        return reranked
            .OrderByDescending(c => c.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// 多信号重排序分数 = 向量余弦 + 关键词重叠 + 符号名匹配 + 文件名匹配（权重见 SearchConfig）。
    /// </summary>
    private static float ComputeRerankScore(ChunkSearchResult candidate, FrozenSet<string> queryTerms) {
        var vectorScore = candidate.Score;

        var sourceTerms = ExtractTerms(candidate.SourceText ?? candidate.SymbolFqn);
        var keywordOverlap = queryTerms.Intersect(sourceTerms).Count();
        var keywordScore = queryTerms.Count > 0 ? (float)keywordOverlap / queryTerms.Count : 0f;

        var symbolTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        symbolTerms.UnionWith(ExtractTerms(candidate.SymbolFqn));
        foreach (var fqn in candidate.ContainedSymbolFqns) {
            symbolTerms.UnionWith(ExtractTerms(fqn));
        }
        var symbolOverlap = queryTerms.Intersect(symbolTerms).Count();
        var symbolScore = queryTerms.Count > 0 ? (float)symbolOverlap / queryTerms.Count : 0f;

        var fileName = Path.GetFileNameWithoutExtension(candidate.FilePath.AsSpan());
        var fileNameTerms = ExtractTerms(fileName.ToString());
        var fileNameOverlap = queryTerms.Intersect(fileNameTerms).Count();
        var fileNameScore = queryTerms.Count > 0 ? (float)fileNameOverlap / queryTerms.Count : 0f;

        return vectorScore * SearchConfig.VectorWeight
            + keywordScore * SearchConfig.KeywordWeight
            + symbolScore * SearchConfig.SymbolWeight
            + fileNameScore * SearchConfig.FileNameWeight;
    }

    /// <summary>
    /// 图谱加权 — 结果间有调用关系/同文件/同命名空间 → 加分。
    /// </summary>
    private static async Task<List<ChunkSearchResult>> ApplyGraphWeightsAsync(
        List<ChunkSearchResult> results, ICallGraph callGraph, CancellationToken ct) {
        const float CallRelationBonus = 0.05f;
        const float SameFileBonus = 0.03f;
        const float SameNamespaceBonus = 0.02f;

        var weights = new float[results.Count];
        var namespaces = results.Select(ExtractNamespace).ToArray();
        var allSymbols = results
            .SelectMany(r => r.ContainedSymbolFqns)
            .ToFrozenSet(StringComparer.Ordinal);

        for (var i = 0; i < results.Count; i++) {
            for (var j = i + 1; j < results.Count; j++) {
                if (results[i].FilePath == results[j].FilePath) {
                    weights[i] += SameFileBonus;
                    weights[j] += SameFileBonus;
                }
                if (namespaces[i] is not null && namespaces[i] == namespaces[j]) {
                    weights[i] += SameNamespaceBonus;
                    weights[j] += SameNamespaceBonus;
                }
            }
        }

        for (var i = 0; i < results.Count; i++) {
            ct.ThrowIfCancellationRequested();
            foreach (var fqn in results[i].ContainedSymbolFqns) {
                var callees = await callGraph.GetCalleesAsync(fqn, ct).ConfigureAwait(false);
                foreach (var edge in callees) {
                    if (allSymbols.Contains(edge.CalleeSymbol)) {
                        weights[i] += CallRelationBonus;
                    }
                }
                var callers = await callGraph.GetCallersAsync(fqn, ct).ConfigureAwait(false);
                foreach (var edge in callers) {
                    if (allSymbols.Contains(edge.CallerSymbol)) {
                        weights[i] += CallRelationBonus;
                    }
                }
            }
        }

        return results
            .Select((r, i) => r with { Score = r.Score + MathF.Min(weights[i], 0.5f) })
            .ToList();
    }

    /// <summary>
    /// 提取术语 — PascalCase 拆分 + 小写化 + 去停用词。
    /// <para>例: "CosineSimilarity" → {"cosine", "similarity"}。</para>
    /// </summary>
    private static FrozenSet<string> ExtractTerms(string text) {
        if (string.IsNullOrEmpty(text)) return FrozenSet<string>.Empty;
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new List<char>();
        foreach (var ch in text) {
            if (char.IsLetterOrDigit(ch)) {
                current.Add(ch);
            } else if (current.Count > 0) {
                AddSplitTerms(terms, current);
                current.Clear();
            }
        }
        if (current.Count > 0) AddSplitTerms(terms, current);
        return terms.Where(t => !StopWords.Contains(t) && t.Length > 1).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>PascalCase 拆分 — "CosineSimilarity" → "cosine", "similarity"。</summary>
    private static void AddSplitTerms(HashSet<string> terms, List<char> token) {
        var word = new string(token.ToArray()).ToLowerInvariant();
        terms.Add(word);

        var start = 0;
        for (var i = 1; i < token.Count; i++) {
            if (char.IsUpper(token[i]) && i > start && !char.IsUpper(token[i - 1])) {
                var sub = new string(token.GetRange(start, i - start).ToArray()).ToLowerInvariant();
                if (sub.Length > 1) terms.Add(sub);
                start = i;
            }
        }
        if (start > 0 && start < token.Count) {
            var last = new string(token.GetRange(start, token.Count - start).ToArray()).ToLowerInvariant();
            if (last.Length > 1) terms.Add(last);
        }
    }

    /// <summary>从 FQN 提取命名空间 — "A.B.C.Foo" → "A.B.C"。</summary>
    private static string? ExtractNamespace(ChunkSearchResult r) {
        var fqn = r.SymbolFqn.AsSpan();
        var lastDot = fqn.LastIndexOf('.');
        if (lastDot <= 0) return null;
        var nsPart = fqn[..lastDot];
        lastDot = nsPart.LastIndexOf('.');
        return lastDot <= 0 ? null : nsPart[..lastDot].ToString();
    }
}
