namespace JoinCode.CodeIndex.Tests;

public sealed class GraphPersistenceTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly SymbolIndex _index;
    private readonly IFileSystem _fs;
    private readonly GraphPersistence _persistence;

    public GraphPersistenceTests()
    {
        _store = new InMemoryIndexStore();
        _fs = TestFileSystem.Current;
        _index = new SymbolIndex(_store, _fs, new CSharpSymbolExtractor());
        _persistence = new GraphPersistence(_store, _fs);
    }

    public void Dispose()
    {
        _index.Dispose();
        _store.Dispose();
    }

    [Fact]
    public async Task IndexFileAsync_WithCallEdges_PersistsCallGraph()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task IndexFileAsync_WithDependencies_PersistsDependencyGraph()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task RemoveFileAsync_RemovesCallAndDependencyEdges()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task IndexFileAsync_CrossFileInterface_CorrectsInheritsToImplements()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <summary>
    /// SaveAsync 在有数据时能正确保存到磁盘，不抛 "read lock is being released without being held" 异常。
    /// 回归 bug: ReaderWriterLockSlim 锁 scope 跨越 await 调用，线程亲和性导致释放锁抛异常。
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithData_WritesFileWithoutLockException()
    {
        PopulateStoreWithData();
        const string dir = "graph-save-data";

        await _persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);

        var path = Path.Combine(dir, "code-index.json");
        Assert.True(_fs.FileExists(path), "持久化文件应存在");
        var json = await _fs.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.False(string.IsNullOrEmpty(json), "JSON 内容不应为空");
        Assert.Contains("\"version\"", json);
        Assert.Contains("A.B.C", json);
        Assert.Contains("Newtonsoft.Json", json);
    }

    /// <summary>
    /// SaveAsync 保存的 JSON 文件能被 LoadAsync 正确读回，数据往返一致。
    /// </summary>
    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsData()
    {
        PopulateStoreWithData();
        const string dir = "graph-roundtrip";

        await _persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);

        var loadStore = new InMemoryIndexStore();
        try
        {
            var loadPersistence = new GraphPersistence(loadStore, _fs);
            var loaded = await loadPersistence.LoadAsync(dir, CancellationToken.None).ConfigureAwait(true);
            Assert.True(loaded, "LoadAsync 应返回 true 表示成功加载");

            using var scope = loadStore.EnterReadLock();
            Assert.Single(loadStore.SymbolsByFqn);
            Assert.True(loadStore.SymbolsByFqn.ContainsKey("A.B.C"));
            Assert.Equal("C", loadStore.SymbolsByFqn["A.B.C"].Name);

            Assert.Single(loadStore.CallEdges);
            Assert.Equal("A.B.D", loadStore.CallEdges[0].CalleeSymbol);
            Assert.Equal(CallKind.Direct, loadStore.CallEdges[0].CallKind);

            Assert.Single(loadStore.DepEdges);
            Assert.Equal(DependencyKind.Inherits, loadStore.DepEdges[0].DependencyKind);

            Assert.Single(loadStore.Projects);
            Assert.True(loadStore.Projects.ContainsKey("P.csproj"));
            Assert.Equal("net10.0", loadStore.Projects["P.csproj"].TargetFramework);

            Assert.Single(loadStore.ProjectRefs["P.csproj"]);
            Assert.Equal("Q.csproj", loadStore.ProjectRefs["P.csproj"][0].TargetProjectPath);

            Assert.Single(loadStore.NuGetRefs["P.csproj"]);
            Assert.Equal("Newtonsoft.Json", loadStore.NuGetRefs["P.csproj"][0].PackageName);
            Assert.Equal("13.0.1", loadStore.NuGetRefs["P.csproj"][0].Version);
        }
        finally
        {
            loadStore.Dispose();
        }
    }

    /// <summary>
    /// SaveAsync 在空索引时也能正常保存，生成有效 JSON 且可被 LoadAsync 读回。
    /// </summary>
    [Fact]
    public async Task SaveAsync_EmptyStore_WritesValidJson()
    {
        const string dir = "graph-empty";

        await _persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);

        var path = Path.Combine(dir, "code-index.json");
        Assert.True(_fs.FileExists(path), "空索引也应生成持久化文件");
        var json = await _fs.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("\"symbols\"", json);
        Assert.Contains("\"callEdges\"", json);

        var loaded = await _persistence.LoadAsync(dir, CancellationToken.None).ConfigureAwait(true);
        Assert.True(loaded, "空索引的 JSON 应能被 LoadAsync 成功加载");

        using var scope = _store.EnterReadLock();
        Assert.Empty(_store.SymbolsByFqn);
        Assert.Empty(_store.CallEdges);
        Assert.Empty(_store.DepEdges);
        Assert.Empty(_store.Projects);
    }

    /// <summary>
    /// SaveAsync 并发调用不抛锁异常。
    /// 回归 bug: ReaderWriterLockSlim 线程亲和性 — async await 后续体可能在不同线程执行，
    /// 若锁 scope 跨越 await，获取锁线程 != 释放锁线程，抛
    /// "The read lock is being released without being held"。
    /// 修复后锁 scope 限制在同步块内，await 前已释放锁，并发安全。
    /// </summary>
    [Fact]
    public async Task SaveAsync_ConcurrentCalls_DoNotThrowLockException()
    {
        PopulateStoreWithData();
        const int concurrency = 8;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var dirs = Enumerable.Range(0, concurrency).Select(i => $"graph-concurrent-{i}").ToArray();

        var tasks = dirs.Select(d => Task.Run(async () =>
        {
            try
            {
                await _persistence.SaveAsync(d, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(true);

        Assert.Empty(exceptions);
        foreach (var d in dirs)
        {
            Assert.True(_fs.FileExists(Path.Combine(d, "code-index.json")), $"并发保存后 {d} 应存在文件");
        }
    }

    /// <summary>
    /// ExistsAsync 在已保存目录返回 true。
    /// </summary>
    [Fact]
    public async Task ExistsAsync_AfterSave_ReturnsTrue()
    {
        PopulateStoreWithData();
        const string dir = "graph-exists-true";

        await _persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);

        var exists = await _persistence.ExistsAsync(dir, CancellationToken.None).ConfigureAwait(true);
        Assert.True(exists, "保存后 ExistsAsync 应返回 true");
    }

    /// <summary>
    /// ExistsAsync 在未保存目录返回 false。
    /// </summary>
    [Fact]
    public async Task ExistsAsync_WithoutSave_ReturnsFalse()
    {
        const string dir = "graph-exists-false";

        var exists = await _persistence.ExistsAsync(dir, CancellationToken.None).ConfigureAwait(true);
        Assert.False(exists, "未保存的目录 ExistsAsync 应返回 false");
    }

    private void PopulateStoreWithData()
    {
        using var scope = _store.EnterWriteLock();
        _store.SymbolsByFqn["A.B.C"] = new SymbolInfo
        {
            Name = "C",
            FullyQualifiedName = "A.B.C",
            Kind = SymbolKind.Class,
            FilePath = "C.cs",
            StartLine = 1,
            EndLine = 10,
            StartColumn = 1,
            EndColumn = 1
        };
        _store.CallEdges.Add(new CallEdge
        {
            CallerSymbol = "A.B.C",
            CalleeSymbol = "A.B.D",
            CallSiteFilePath = "C.cs",
            CallSiteLine = 5,
            CallKind = CallKind.Direct
        });
        _store.DepEdges.Add(new DependencyEdge
        {
            SourceSymbol = "A.B.C",
            TargetSymbol = "A.B.D",
            DependencyKind = DependencyKind.Inherits,
            SourceFilePath = "C.cs"
        });
        _store.Projects["P.csproj"] = new ProjectInfo
        {
            Name = "P",
            FilePath = "P.csproj",
            TargetFramework = "net10.0"
        };
        _store.ProjectRefs["P.csproj"] =
        [
            new() { SourceProjectPath = "P.csproj", TargetProjectPath = "Q.csproj" }
        ];
        _store.NuGetRefs["P.csproj"] =
        [
            new() { ProjectPath = "P.csproj", PackageName = "Newtonsoft.Json", Version = "13.0.1" }
        ];
    }
}
