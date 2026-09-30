namespace JoinCode.CodeIndex.Tests;

public sealed class CodeIndexerSemanticTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private readonly IFileSystem _fs;
    private readonly EmbeddingIndex _embeddingIndex;
    private bool _disposed;

    public CodeIndexerSemanticTests() {
        _store = new InMemoryIndexStore();
        _fs = new IO.FileSystem.InMemoryFileSystem();
        _indexer = new CodeIndexer(_store, _fs);
        _embeddingIndex = new EmbeddingIndex(
            new FakeEmbeddingModel(8),
            new BruteForceAnn());
        _indexer.SetEmbeddingIndex(_embeddingIndex);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _indexer.DisposeSafe();
        _store.Dispose();
        _embeddingIndex.Dispose();
    }

    [Fact]
    public async Task BuildIndex_WithEmbedding_IndexesChunks() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_sem_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"),
            "public class Foo { public void Bar() { } }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        Assert.True(_embeddingIndex.ChunkCount > 0);
        Assert.Equal(IndexStatus.Ready, _embeddingIndex.Status);
    }

    [Fact]
    public async Task SearchSemantic_AfterIndex_ReturnsResults() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_sem_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"),
            "public class Foo { public void Bar() { } }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        var results = await _indexer.SearchSemanticAsync("Foo Bar", 5, CancellationToken.None);
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task BuildIndex_DeletedFile_RemovedFromEmbedding() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_sem_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var fileA = Path.Combine(root, "A.cs");
        await _fs.WriteAllText(fileA, "public class A { }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);
        Assert.True(_embeddingIndex.ChunkCount > 0);

        _fs.DeleteFile(fileA);
        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        Assert.Equal(0, _embeddingIndex.ChunkCount);
    }

    [Fact]
    public async Task SearchSemantic_WithoutEmbedding_ReturnsEmpty() {
        var store = new InMemoryIndexStore();
        var fs = new IO.FileSystem.InMemoryFileSystem();
        await using var indexer = new CodeIndexer(store, fs);
        var root = Path.Combine(Path.GetTempPath(), $"ci_noem_{Guid.NewGuid():N}");
        fs.CreateDirectory(root);
        await fs.WriteAllText(Path.Combine(root, "A.cs"),
            "public class Foo { public void Bar() { } }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await indexer.BuildIndexAsync(options, CancellationToken.None);

        var results = await indexer.SearchSemanticAsync("query", 5, CancellationToken.None);
        Assert.Empty(results);
        await store.DisposeAsync();
    }
}
