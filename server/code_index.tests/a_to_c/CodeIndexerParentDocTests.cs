namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// CodeIndexer 父文档检索集成测试 — 验证 BuildIndex 填充父文档、SearchSemantic 返回父文档、删除文件同步清理。
/// </summary>
public sealed class CodeIndexerParentDocTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private readonly IFileSystem _fs;
    private readonly EmbeddingIndex _embeddingIndex;
    private readonly InMemoryParentDocumentStore _parentStore;
    private bool _disposed;

    public CodeIndexerParentDocTests() {
        _store = new InMemoryIndexStore();
        _fs = new IO.FileSystem.InMemoryFileSystem();
        _parentStore = new InMemoryParentDocumentStore();
        _indexer = new CodeIndexer(_store, _fs);
        _embeddingIndex = new EmbeddingIndex(
            new FakeEmbeddingModel(8),
            new BruteForceAnn(),
            _parentStore);
        _indexer.SetEmbeddingIndex(_embeddingIndex);
        _indexer.SetParentDocumentStore(_parentStore);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _indexer.DisposeSafe();
        _store.Dispose();
        _embeddingIndex.Dispose();
        _parentStore.Dispose();
    }

    [Fact]
    public async Task BuildIndex_WithParentStore_FillsParentDocuments() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_pd_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"),
            "public class Foo { public void Bar() { } }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        Assert.True(_parentStore.Count > 0);
    }

    [Fact]
    public async Task SearchSemantic_WithParentStore_ReturnsParentDocumentText() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_pd_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"),
            "public class Foo { public void Bar() { } }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        var results = await _indexer.SearchSemanticAsync("Bar", 5, CancellationToken.None,
            new SearchOptions { IncludeParentDocument = true });
        Assert.NotEmpty(results);
        var resultWithParent = results.FirstOrDefault(r => r.ParentDocumentText != null);
        Assert.NotNull(resultWithParent);
        Assert.Contains("Foo", resultWithParent!.ParentDocumentText!);
    }

    [Fact]
    public async Task BuildIndex_DeletedFile_RemovedFromParentStore() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_pd_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var fileA = Path.Combine(root, "A.cs");
        await _fs.WriteAllText(fileA, "public class A { }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);
        Assert.True(_parentStore.Count > 0);

        _fs.DeleteFile(fileA);
        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        Assert.Equal(0, _parentStore.Count);
    }

    [Fact]
    public async Task BuildIndex_MultipleClasses_AllParentDocsFilled() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_pd_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "A.cs"),
            "public class Foo { public void Bar() { } }");
        await _fs.WriteAllText(Path.Combine(root, "B.cs"),
            "public class Baz { public void Qux() { } }");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        Assert.True(_parentStore.Count >= 2);
    }

    [Fact]
    public async Task BuildIndex_MarkdownFile_IndexesChunks() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_md_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "doc.md"),
            "# Guide\n\nAuthentication steps.\n");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        Assert.True(_embeddingIndex.ChunkCount > 0);
    }

    [Fact]
    public async Task SearchSemantic_MarkdownFile_ReturnsResults() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_md_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "auth.md"),
            "# Authentication\n\nHow to authenticate users.\n");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        var results = await _indexer.SearchSemanticAsync("authentication", 5, CancellationToken.None);
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task BuildIndex_MarkdownParentDoc_Filled() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_md_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "doc.md"),
            "# Title\n\nContent here.\n");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        Assert.True(_parentStore.Count > 0);
    }

    [Fact]
    public async Task BuildIndex_CsAndMd_BothIndexed() {
        var root = Path.Combine(Path.GetTempPath(), $"ci_mix_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "code.cs"),
            "public class Foo { public void Bar() { } }");
        await _fs.WriteAllText(Path.Combine(root, "doc.md"),
            "# Guide\n\nUsage guide.\n");
        var options = new CodeIndexOptions { WorkspaceRoot = root };

        await _indexer.BuildIndexAsync(options, CancellationToken.None);

        Assert.True(_embeddingIndex.ChunkCount >= 2);
    }
}
