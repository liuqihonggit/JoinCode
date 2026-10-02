namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// 增量更新向量索引联动测试 — 验证 UpdateFileAsync 修改/删除文件时 EmbeddingIndex 同步更新。
/// </summary>
public sealed class CodeIndexerIncrementalVectorTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private readonly IFileSystem _fs;
    private readonly EmbeddingIndex _embeddingIndex;
    private bool _disposed;

    public CodeIndexerIncrementalVectorTests() {
        _store = new InMemoryIndexStore();
        _fs = new IO.FileSystem.InMemoryFileSystem();
        _indexer = new CodeIndexer(_store, _fs);
        _embeddingIndex = new EmbeddingIndex(
            new FakeEmbeddingModel(8),
            new BruteForceAnn(),
            _fs);
        _indexer.SetEmbeddingIndex(_embeddingIndex);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _indexer.DisposeSafe();
        _store.Dispose();
        _embeddingIndex.Dispose();
        _embeddingIndex.DisposeAsync().AsTask().Wait();
    }

    [Fact]
    public async Task UpdateFileAsync_ModifiedCs_UpdatesEmbeddingIndex() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_inc_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var file = Path.Combine(root, "A.cs");
        await _fs.WriteAllText(file, "public class Foo { public void Bar() { } }");
        await _indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);
        Assert.True(_embeddingIndex.ChunkCount > 0);

        await _fs.WriteAllText(file, "public class Foo { public void Baz() { } }");
        await _indexer.UpdateFileAsync(file, CancellationToken.None);

        var results = await _indexer.SearchSemanticAsync("Baz", 5, CancellationToken.None);
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task UpdateFileAsync_DeletedCs_RemovesFromEmbeddingIndex() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_inc_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var file = Path.Combine(root, "A.cs");
        await _fs.WriteAllText(file, "public class Foo { public void Bar() { } }");
        await _indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);
        Assert.True(_embeddingIndex.ChunkCount > 0);

        _fs.DeleteFile(file);
        await _indexer.UpdateFileAsync(file, CancellationToken.None);

        Assert.Equal(0, _embeddingIndex.ChunkCount);
    }

    [Fact]
    public async Task UpdateFileAsync_UnchangedCs_SkipsEmbeddingUpdate() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_inc_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var file = Path.Combine(root, "A.cs");
        await _fs.WriteAllText(file, "public class Foo { public void Bar() { } }");
        await _indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);
        var chunkCountBefore = _embeddingIndex.ChunkCount;

        await _indexer.UpdateFileAsync(file, CancellationToken.None);

        Assert.Equal(chunkCountBefore, _embeddingIndex.ChunkCount);
    }

    [Fact]
    public async Task UpdateFileAsync_ModifiedMd_UpdatesEmbeddingIndex() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_inc_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var file = Path.Combine(root, "doc.md");
        await _fs.WriteAllText(file, "# Guide\n\nAuthentication steps.\n");
        await _indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);
        Assert.True(_embeddingIndex.ChunkCount > 0);

        await _fs.WriteAllText(file, "# Guide\n\nRate limiting logic.\n");
        await _indexer.UpdateFileAsync(file, CancellationToken.None);

        var results = await _indexer.SearchSemanticAsync("rate limiting", 5, CancellationToken.None);
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task UpdateFileAsync_DeletedMd_RemovesFromEmbeddingIndex() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_inc_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var file = Path.Combine(root, "doc.md");
        await _fs.WriteAllText(file, "# Guide\n\nContent.\n");
        await _indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);
        Assert.True(_embeddingIndex.ChunkCount > 0);

        _fs.DeleteFile(file);
        await _indexer.UpdateFileAsync(file, CancellationToken.None);

        Assert.Equal(0, _embeddingIndex.ChunkCount);
    }

    [Fact]
    public async Task UpdateFileAsync_ModifiedCs_UpdatesParentDocStore() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_inc_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var file = Path.Combine(root, "A.cs");
        await _fs.WriteAllText(file, "public class Foo { public void Bar() { } }");
        await _indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);
        Assert.True(_embeddingIndex.ChunkCount > 0);

        await _fs.WriteAllText(file, "public class Foo { public void Baz() { } public void Qux() { } }");
        await _indexer.UpdateFileAsync(file, CancellationToken.None);

        var results = await _indexer.SearchSemanticAsync("Baz", 5, CancellationToken.None,
            new SearchOptions { IncludeParentDocument = true });
        var resultWithParent = results.FirstOrDefault(r => r.ParentDocumentText != null);
        Assert.NotNull(resultWithParent);
    }
}
