namespace JoinCode.CodeIndex.Tests;

/// <summary>
/// 端到端集成测试 — 用真实 ONNX 模型验证完整链路：构建索引 → 语义搜索 → 父文档检索 → file_type 过滤。
/// <para>需要模型文件存在：%AppData%/jcc/embedding/model_quantized.onnx + vocab.txt</para>
/// <para>CI 环境无模型文件时自动跳过。</para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class VectorIndexE2ETests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer? _indexer;
    private readonly IFileSystem _fs;
    private readonly EmbeddingIndex? _embeddingIndex;
    private readonly PhysicalFileSystem _realFs;
    private bool _disposed;

    public VectorIndexE2ETests() {
        _store = new InMemoryIndexStore();
        _fs = new IO.FileSystem.InMemoryFileSystem();
        _realFs = new PhysicalFileSystem();

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "jcc", "embedding");
        var modelPath = Path.Combine(appData, "model_quantized.onnx");
        var vocabPath = Path.Combine(appData, "vocab.txt");

        if (!_realFs.FileExists(modelPath) || !_realFs.FileExists(vocabPath)) {
            return;
        }

        var embeddingModel = new OnnxEmbeddingClient(modelPath, vocabPath, _realFs);
        _embeddingIndex = new EmbeddingIndex(embeddingModel, new BruteForceAnn(), _realFs);
        _indexer = new CodeIndexer(_store, _fs);
        _indexer.SetEmbeddingIndex(_embeddingIndex);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _indexer?.DisposeSafe();
        _store?.Dispose();
        _embeddingIndex?.Dispose();
        _embeddingIndex?.DisposeAsync().AsTask().Wait();
    }

    private bool IsModelAvailable => _indexer is not null;

    [Fact]
    public async Task E2E_BuildIndex_SearchReturnsResults() {
        if (!IsModelAvailable) return;
        var indexer = _indexer!;

        var root = Path.Combine(Path.GetTempPath(), $"e2e_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "AuthService.cs"),
            "public class AuthService { public bool Login(string user, string pass) { return true; } public void Logout() { } }");
        await _fs.WriteAllText(Path.Combine(root, "RateLimit.cs"),
            "public class RateLimiter { public bool AllowRequest(string ip) { return true; } }");

        await indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);

        var results = await indexer.SearchSemanticAsync("authentication login", 5, CancellationToken.None);
        Assert.NotEmpty(results);
        var authResult = results.FirstOrDefault(r => r.SymbolFqn.Contains("Auth"));
        Assert.NotNull(authResult);
    }

    [Fact]
    public async Task E2E_ParentDocument_ReturnsClassContext() {
        if (!IsModelAvailable) return;
        var indexer = _indexer!;

        var root = Path.Combine(Path.GetTempPath(), $"e2e_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "Foo.cs"),
            "public class Foo { public void Bar() { } public void Baz() { } }");

        await indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);

        var results = await indexer.SearchSemanticAsync("Bar", 5, CancellationToken.None,
            new SearchOptions { IncludeParentDocument = true });
        var resultWithParent = results.FirstOrDefault(r => r.ParentDocumentText != null);
        Assert.NotNull(resultWithParent);
        Assert.Contains("class Foo", resultWithParent!.ParentDocumentText!);
    }

    [Fact]
    public async Task E2E_FileTypeFilter_OnlyReturnsCs() {
        if (!IsModelAvailable) return;
        var indexer = _indexer!;

        var root = Path.Combine(Path.GetTempPath(), $"e2e_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        await _fs.WriteAllText(Path.Combine(root, "code.cs"),
            "public class Handler { public void Process() { } }");
        await _fs.WriteAllText(Path.Combine(root, "doc.md"),
            "# Processing Guide\n\nHow to process requests.\n");

        await indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);

        var csResults = await indexer.SearchSemanticAsync("process", 10, CancellationToken.None,
            new SearchOptions { FileType = "cs" });
        Assert.All(csResults, r => Assert.EndsWith(".cs", r.FilePath));

        var mdResults = await indexer.SearchSemanticAsync("process", 10, CancellationToken.None,
            new SearchOptions { FileType = "md" });
        Assert.All(mdResults, r => Assert.EndsWith(".md", r.FilePath));
    }

    [Fact]
    public async Task E2E_IncrementalUpdate_VectorIndexSynced() {
        if (!IsModelAvailable) return;
        var indexer = _indexer!;

        var root = Path.Combine(Path.GetTempPath(), $"e2e_{Guid.NewGuid():N}");
        _fs.CreateDirectory(root);
        var file = Path.Combine(root, "A.cs");
        await _fs.WriteAllText(file, "public class Foo { public void Bar() { } }");
        await indexer.BuildIndexAsync(new CodeIndexOptions { WorkspaceRoot = root }, CancellationToken.None);

        await _fs.WriteAllText(file, "public class Foo { public void NewMethod() { } }");
        await indexer.UpdateFileAsync(file, CancellationToken.None);

        var results = await indexer.SearchSemanticAsync("NewMethod", 5, CancellationToken.None);
        Assert.NotEmpty(results);
    }
}
