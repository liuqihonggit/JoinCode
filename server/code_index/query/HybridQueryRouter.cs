namespace JoinCode.CodeIndex.Query;

/// <summary>
/// 混合查询路由 — 查询分类 + 双路并行 + 降级链。
/// <para>降级链：向量搜索 → 符号搜索 → 空结果（兜底）。</para>
/// <para>向量未就绪时自动降级到符号搜索。</para>
/// </summary>
public sealed class HybridQueryRouter {

    private readonly EmbeddingIndex? _embeddingIndex;
    private readonly ISymbolSearcher _symbolSearcher;
    private readonly QueryClassifier _classifier;

    /// <summary>
    /// 构造混合查询路由。
    /// </summary>
    /// <param name="embeddingIndex">向量嵌入索引（可选，null 时降级到符号搜索）。</param>
    /// <param name="symbolSearcher">符号搜索器（必需，降级兜底）。</param>
    /// <param name="classifier">查询分类器（可选，默认创建）。</param>
    public HybridQueryRouter(
        EmbeddingIndex? embeddingIndex,
        ISymbolSearcher symbolSearcher,
        QueryClassifier? classifier = null) {
        ArgumentNullException.ThrowIfNull(symbolSearcher);
        _embeddingIndex = embeddingIndex;
        _symbolSearcher = symbolSearcher;
        _classifier = classifier ?? new QueryClassifier();
    }

    /// <summary>
    /// 混合搜索 — 分类查询并路由到合适的搜索引擎。
    /// </summary>
    /// <param name="query">查询文本。</param>
    /// <param name="topK">每路结果数上限。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>混合查询结果，含语义和符号两路结果。</returns>
    public async Task<HybridQueryResult> SearchAsync(string query, int topK, CancellationToken ct) {
        ArgumentNullException.ThrowIfNull(query);
        if (topK <= 0) {
            return new HybridQueryResult {
                SemanticResults = [],
                SymbolResults = [],
                ClassifiedKind = _classifier.Classify(query),
                UsedStrategy = "empty"
            };
        }

        var kind = _classifier.Classify(query);
        return kind switch {
            QueryKind.Symbol => await SearchSymbolOnlyAsync(query, topK, kind, ct).ConfigureAwait(false),
            QueryKind.Semantic => await SearchSemanticOnlyAsync(query, topK, kind, ct).ConfigureAwait(false),
            QueryKind.Hybrid => await SearchHybridAsync(query, topK, kind, ct).ConfigureAwait(false),
            _ => await SearchSemanticOnlyAsync(query, topK, kind, ct).ConfigureAwait(false)
        };
    }

    private async Task<HybridQueryResult> SearchSymbolOnlyAsync(
        string query, int topK, QueryKind kind, CancellationToken ct) {
        var symbolResults = await SearchSymbolsAsync(query, ct).ConfigureAwait(false);
        return new HybridQueryResult {
            SemanticResults = [],
            SymbolResults = symbolResults,
            ClassifiedKind = kind,
            UsedStrategy = "symbol"
        };
    }

    private async Task<HybridQueryResult> SearchSemanticOnlyAsync(
        string query, int topK, QueryKind kind, CancellationToken ct) {
        var semanticResults = await SearchSemanticAsync(query, topK, ct).ConfigureAwait(false);
        var strategy = semanticResults.Count > 0 ? "vector" : "fallback";
        if (semanticResults.Count == 0) {
            var symbolResults = await SearchSymbolsAsync(query, ct).ConfigureAwait(false);
            return new HybridQueryResult {
                SemanticResults = [],
                SymbolResults = symbolResults,
                ClassifiedKind = kind,
                UsedStrategy = strategy
            };
        }
        return new HybridQueryResult {
            SemanticResults = semanticResults,
            SymbolResults = [],
            ClassifiedKind = kind,
            UsedStrategy = strategy
        };
    }

    private async Task<HybridQueryResult> SearchHybridAsync(
        string query, int topK, QueryKind kind, CancellationToken ct) {
        var semanticTask = SearchSemanticAsync(query, topK, ct);
        var symbolTask = SearchSymbolsAsync(query, ct);
        await Task.WhenAll(semanticTask, symbolTask).ConfigureAwait(false);

        var semanticResults = await semanticTask.ConfigureAwait(false);
        var symbolResults = await symbolTask.ConfigureAwait(false);
        return new HybridQueryResult {
            SemanticResults = semanticResults,
            SymbolResults = symbolResults,
            ClassifiedKind = kind,
            UsedStrategy = "hybrid"
        };
    }

    private async Task<IReadOnlyList<ChunkSearchResult>> SearchSemanticAsync(
        string query, int topK, CancellationToken ct) {
        if (_embeddingIndex is null) return [];
        if (_embeddingIndex.Status != IndexStatus.Ready
            && _embeddingIndex.Status != IndexStatus.Partial) return [];
        try {
            return await _embeddingIndex.SearchAsync(query, topK, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return [];
        }
    }

    private async Task<IReadOnlyList<SymbolInfo>> SearchSymbolsAsync(
        string query, CancellationToken ct) {
        try {
            var result = await _symbolSearcher.SearchAsync(query, ct).ConfigureAwait(false);
            return result.Items;
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return [];
        }
    }
}
