namespace JoinCode.CodeIndex.Tests;

public sealed class HybridQueryRouterTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly SymbolSearcher _searcher;
    private readonly EmbeddingIndex _embeddingIndex;
    private readonly HybridQueryRouter _router;
    private bool _disposed;

    public HybridQueryRouterTests() {
        _store = new InMemoryIndexStore();
        _searcher = new SymbolSearcher(_store);
        _embeddingIndex = new EmbeddingIndex(
            new FakeEmbeddingModel(8),
            new BruteForceAnn(),
            TestFileSystem.Current);
        _router = new HybridQueryRouter(_embeddingIndex, _searcher);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _store?.Dispose();
        _embeddingIndex.Dispose();
    }

    [Fact]
    public async Task SearchAsync_TopKZero_ReturnsEmpty() {
        var result = await _router.SearchAsync("query", 0, CancellationToken.None);
        Assert.Empty(result.SemanticResults);
        Assert.Empty(result.SymbolResults);
        Assert.Equal("empty", result.UsedStrategy);
    }

    [Fact]
    public async Task SearchAsync_SymbolQuery_UsesSymbolStrategy() {
        var result = await _router.SearchAsync("Foo.Bar", 5, CancellationToken.None);
        Assert.Equal(QueryKind.Symbol, result.ClassifiedKind);
        Assert.Equal("symbol", result.UsedStrategy);
        Assert.Empty(result.SemanticResults);
    }

    [Fact]
    public async Task SearchAsync_SemanticQuery_EmbeddingNotReady_FallsBackToSymbol() {
        var result = await _router.SearchAsync("how to handle file upload", 5, CancellationToken.None);
        Assert.Equal(QueryKind.Semantic, result.ClassifiedKind);
        Assert.Equal("fallback", result.UsedStrategy);
    }

    [Fact]
    public async Task SearchAsync_WithoutEmbedding_SemanticFallsBack() {
        var router = new HybridQueryRouter(null, _searcher);
        var result = await router.SearchAsync("find similar code", 5, CancellationToken.None);
        Assert.Equal("fallback", result.UsedStrategy);
    }

    [Fact]
    public async Task SearchAsync_HybridQuery_UsesHybridStrategy() {
        var result = await _router.SearchAsync("who calls the Foo.Bar method that handles upload", 5, CancellationToken.None);
        Assert.Equal(QueryKind.Hybrid, result.ClassifiedKind);
        Assert.Equal("hybrid", result.UsedStrategy);
    }

    [Fact]
    public async Task SearchAsync_WithReadyEmbedding_ReturnsSemanticResults() {
        var chunk = new ChunkInfo {
            ChunkId = "c1",
            SymbolFqn = "Test.Foo",
            Kind = SymbolKind.Method,
            FilePath = "test.cs",
            StartLine = 1,
            EndLine = 5,
            LanguageId = "c-sharp",
            ContentHash = "h1",
            SourceText = "void Foo() {}"
        };
        await _embeddingIndex.IndexChunksAsync([chunk], CancellationToken.None);

        var result = await _router.SearchAsync("find similar code to Foo", 5, CancellationToken.None);

        Assert.NotEmpty(result.SemanticResults);
    }
}
