namespace JoinCode.CodeIndex.Tests;

public sealed class SymbolIndexTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly SymbolIndex _index;

    public SymbolIndexTests()
    {
        _store = new InMemoryIndexStore();
        _index = new SymbolIndex(_store, new IO.FileSystem.InMemoryFileSystem(), new CSharpSymbolExtractor());
    }

    public void Dispose()
    {
        _index.Dispose();
        _store.Dispose();
    }

    [Fact]
    public async Task IndexFileAsync_SingleFile_StoresSymbols()
    {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var index = new SymbolIndex(_store, fs, new CSharpSymbolExtractor());
        var path = "test.cs";
        fs.WriteAllText(path, "public class Foo { public void Bar() { } }");

        await index.IndexFileAsync(path, CancellationToken.None).ConfigureAwait(true);

        Assert.True(_store.SymbolsByFqn.Count > 0);
        Assert.True(_store.FileTracking.ContainsKey(path));
    }

    [Fact]
    public async Task IndexFileAsync_MultipleFiles_StoresAllSymbols()
    {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var index = new SymbolIndex(_store, fs, new CSharpSymbolExtractor());
        fs.WriteAllText("a.cs", "public class A { }");
        fs.WriteAllText("b.cs", "public class B { }");

        await index.IndexFileAsync("a.cs", CancellationToken.None).ConfigureAwait(true);
        await index.IndexFileAsync("b.cs", CancellationToken.None).ConfigureAwait(true);

        Assert.True(_store.SymbolsByFqn.Count >= 2);
        Assert.Equal(2, _store.FileTracking.Count);
    }

    [Fact]
    public async Task RemoveFileAsync_RemovesSymbolsForFile()
    {
        var filePath = "a.cs";
        var batch = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>
        {
            (filePath, "code", "h", MakeExtraction(
                symbols: [MakeSymbol("Foo", "Ns.Foo", SymbolKind.Method, filePath)],
                calls: [],
                deps: []))
        };
        await _index.IndexFilesBatchAsync(batch, CancellationToken.None).ConfigureAwait(true);
        Assert.Single(_store.SymbolsByFqn);

        await _index.RemoveFileAsync(filePath, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.SymbolsByFqn);
        Assert.Empty(_store.FileTracking);
    }

    [Fact]
    public async Task IndexFileAsync_ReindexOverwrites_EnsuresIdempotency()
    {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var index = new SymbolIndex(_store, fs, new CSharpSymbolExtractor());
        var path = "test.cs";
        fs.WriteAllText(path, "public class Old { }");
        await index.IndexFileAsync(path, CancellationToken.None).ConfigureAwait(true);
        var firstCount = _store.SymbolsByFqn.Count;

        fs.WriteAllText(path, "public class New { }");
        await index.IndexFileAsync(path, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(firstCount, _store.SymbolsByFqn.Count);
        Assert.Contains(_store.SymbolsByFqn, kvp => kvp.Key.Contains("New"));
        Assert.DoesNotContain(_store.SymbolsByFqn, kvp => kvp.Key.Contains("Old"));
    }

    [Fact]
    public async Task ClearAsync_RemovesAllData()
    {
        var batch = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>
        {
            ("a.cs", "code", "h", MakeExtraction(
                symbols: [MakeSymbol("Foo", "Ns.Foo", SymbolKind.Method, "a.cs")],
                calls: [MakeCall("Foo", "Bar", "a.cs")],
                deps: [MakeDep("Foo", "Bar", DependencyKind.Uses, "a.cs")]))
        };
        await _index.IndexFilesBatchAsync(batch, CancellationToken.None).ConfigureAwait(true);
        Assert.NotEmpty(_store.SymbolsByFqn);

        await _index.ClearAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.SymbolsByFqn);
        Assert.Empty(_store.CallEdges);
        Assert.Empty(_store.DepEdges);
        Assert.Empty(_store.FileTracking);
    }

    [Fact]
    public async Task IndexFileAsync_NonExistentFile_DoesNothing()
    {
        await _index.IndexFileAsync("missing.cs", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.SymbolsByFqn);
        Assert.Empty(_store.FileTracking);
    }

    [Fact]
    public async Task IndexFileAsync_NullFilePath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _index.IndexFileAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task IndexFilesAsync_IndexesMultipleFiles()
    {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        var index = new SymbolIndex(_store, fs, new CSharpSymbolExtractor());
        fs.WriteAllText("a.cs", "public class A { }");
        fs.WriteAllText("b.cs", "public class B { }");

        await index.IndexFilesAsync(["a.cs", "b.cs"], CancellationToken.None).ConfigureAwait(true);

        Assert.True(_store.SymbolsByFqn.Count >= 2);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsCorrectCounts()
    {
        var batch = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>
        {
            ("a.cs", "code", "h", MakeExtraction(
                symbols: [MakeSymbol("Foo", "Ns.Foo", SymbolKind.Method, "a.cs")],
                calls: [MakeCall("Foo", "Bar", "a.cs")],
                deps: [MakeDep("Foo", "Bar", DependencyKind.Uses, "a.cs")]))
        };
        await _index.IndexFilesBatchAsync(batch, CancellationToken.None).ConfigureAwait(true);

        var stats = await _index.GetStatsAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, stats.FileCount);
        Assert.Equal(1, stats.SymbolCount);
        Assert.Equal(1, stats.CallEdgeCount);
        Assert.Equal(1, stats.DependencyEdgeCount);
        Assert.True(stats.LastUpdated > DateTimeOffset.MinValue);
    }

    // ============ IndexFilesBatchAsync (批量写入优化) ============

    [Fact]
    public async Task IndexFilesBatchAsync_MultipleFiles_StoresAllSymbols()
    {
        var batch = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>
        {
            ("a.cs", "code-a", "h-a", MakeExtraction(
                symbols: [MakeSymbol("Foo", "Ns.Foo", SymbolKind.Method, "a.cs")],
                calls: [MakeCall("Foo", "Bar", "a.cs")],
                deps: [])),
            ("b.cs", "code-b", "h-b", MakeExtraction(
                symbols: [MakeSymbol("Bar", "Ns.Bar", SymbolKind.Method, "b.cs")],
                calls: [],
                deps: []))
        };

        await _index.IndexFilesBatchAsync(batch, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, _store.SymbolsByFqn.Count);
        Assert.True(_store.SymbolsByFqn.ContainsKey("Ns.Foo"));
        Assert.True(_store.SymbolsByFqn.ContainsKey("Ns.Bar"));
        Assert.Single(_store.CallEdges);
        Assert.Equal(2, _store.FileTracking.Count);
    }

    [Fact]
    public async Task IndexFilesBatchAsync_CorrectsInheritsToImplements_WhenTargetIsInterface()
    {
        // 接口 IFoo + 类 FooImpl Inherits IFoo → 批量结束后应修正为 Implements
        var batch = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>
        {
            ("iface.cs", "code", "h1", MakeExtraction(
                symbols: [MakeSymbol("IFoo", "Ns.IFoo", SymbolKind.Interface, "iface.cs")],
                calls: [],
                deps: [])),
            ("impl.cs", "code", "h2", MakeExtraction(
                symbols: [MakeSymbol("FooImpl", "Ns.FooImpl", SymbolKind.Class, "impl.cs")],
                calls: [],
                deps: [MakeDep("Ns.FooImpl", "Ns.IFoo", DependencyKind.Inherits, "impl.cs")]))
        };

        await _index.IndexFilesBatchAsync(batch, CancellationToken.None).ConfigureAwait(true);

        Assert.All(_store.DepEdges, e => Assert.Equal(DependencyKind.Implements, e.DependencyKind));
        Assert.Single(_store.DepEdges);
    }

    [Fact]
    public async Task IndexFilesBatchAsync_ReindexFile_OverwritesOldSymbols()
    {
        var first = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>
        {
            ("a.cs", "v1", "h1", MakeExtraction(
                symbols: [MakeSymbol("Old", "Ns.Old", SymbolKind.Method, "a.cs")],
                calls: [],
                deps: []))
        };
        await _index.IndexFilesBatchAsync(first, CancellationToken.None).ConfigureAwait(true);
        Assert.True(_store.SymbolsByFqn.ContainsKey("Ns.Old"));

        var second = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>
        {
            ("a.cs", "v2", "h2", MakeExtraction(
                symbols: [MakeSymbol("New", "Ns.New", SymbolKind.Method, "a.cs")],
                calls: [],
                deps: []))
        };
        await _index.IndexFilesBatchAsync(second, CancellationToken.None).ConfigureAwait(true);

        Assert.False(_store.SymbolsByFqn.ContainsKey("Ns.Old"));
        Assert.True(_store.SymbolsByFqn.ContainsKey("Ns.New"));
        Assert.Single(_store.FileTracking);
    }

    [Fact]
    public async Task IndexFilesBatchAsync_EmptyList_DoesNothing()
    {
        await _index.IndexFilesBatchAsync([], CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(_store.SymbolsByFqn);
        Assert.Empty(_store.FileTracking);
    }

    [Fact]
    public async Task IndexFileWithContentAsync_UsesProvidedExtraction()
    {
        var extraction = MakeExtraction(
            symbols: [MakeSymbol("Foo", "Ns.Foo", SymbolKind.Method, "a.cs")],
            calls: [],
            deps: []);

        await _index.IndexFileWithContentAsync("a.cs", "code", "h", extraction, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(_store.SymbolsByFqn);
        Assert.Equal("h", _store.FileTracking["a.cs"].Hash);
    }

    [Fact]
    public async Task IndexFileWithContentAsync_NullArguments_Throws()
    {
        var extraction = MakeExtraction([], [], []);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _index.IndexFileWithContentAsync(null!, "code", "h", extraction, CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _index.IndexFileWithContentAsync("a.cs", null!, "h", extraction, CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _index.IndexFileWithContentAsync("a.cs", "code", null!, extraction, CancellationToken.None)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _index.IndexFileWithContentAsync("a.cs", "code", "h", null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task IndexFilesBatchAsync_NullList_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _index.IndexFilesBatchAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task RemoveFileAsync_NullFilePath_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _index.RemoveFileAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    // ============ 批量测试辅助方法 ============

    private static SymbolInfo MakeSymbol(string name, string fqn, SymbolKind kind, string file)
    {
        return new SymbolInfo
        {
            Name = name,
            FullyQualifiedName = fqn,
            Kind = kind,
            FilePath = file,
            StartLine = 1,
            EndLine = 10,
            StartColumn = 1,
            EndColumn = 20
        };
    }

    private static CallEdge MakeCall(string caller, string callee, string file)
    {
        return new CallEdge
        {
            CallerSymbol = caller,
            CalleeSymbol = callee,
            CallSiteFilePath = file,
            CallSiteLine = 5,
            CallKind = CallKind.Direct
        };
    }

    private static DependencyEdge MakeDep(string source, string target, DependencyKind kind, string file)
    {
        return new DependencyEdge
        {
            SourceSymbol = source,
            TargetSymbol = target,
            DependencyKind = kind,
            SourceFilePath = file
        };
    }

    private static ExtractionResult MakeExtraction(
        IReadOnlyList<SymbolInfo> symbols, IReadOnlyList<CallEdge> calls, IReadOnlyList<DependencyEdge> deps)
    {
        return new ExtractionResult { Symbols = symbols, Calls = calls, Dependencies = deps };
    }
}
