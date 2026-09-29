namespace JoinCode.CodeIndex.Tests;

public sealed class GraphPersistenceTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly SymbolIndex _index;
    private readonly IFileSystem _fs;
    private readonly GraphPersistence _persistence;
    private bool _disposed;

    public GraphPersistenceTests() {
        _store = new InMemoryIndexStore();
        _fs = TestFileSystem.Current;
        _index = new SymbolIndex(_store, _fs, new CSharpSymbolExtractor());
        _persistence = new GraphPersistence(_store, _fs);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _index.DisposeSafe();
        _store.Dispose();
    }

    [Fact]
    public async Task IndexFileAsync_WithCallEdges_PersistsCallGraph() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        await using var store = new InMemoryIndexStore();
        var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        await using var persistence = new GraphPersistence(store, fs);

        // 注入含调用边的文件
        await fs.WriteAllText("a.cs", "public class Foo { public void Bar() { Baz(); } public void Baz() { } }");
        await index.IndexFileAsync("a.cs", CancellationToken.None).ConfigureAwait(true);

        var preSnap = store.GetSnapshot();
        Assert.True(preSnap.CallEdges.Count > 0, "索引后应有调用边");

        // 保存 → 加载 → 验证调用边往返一致
        const string dir = "graph-calledges";
        await persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);
        await using var loadStore = new InMemoryIndexStore();
        await using var loadPersistence = new GraphPersistence(loadStore, fs);
        var loaded = await loadPersistence.LoadAsync(dir, CancellationToken.None).ConfigureAwait(true);

        Assert.True(loaded);
        var postSnap = loadStore.GetSnapshot();
        Assert.Equal(preSnap.CallEdges.Count, postSnap.CallEdges.Count);
        Assert.Contains(postSnap.CallEdges, e => e.CalleeSymbol.Contains("Baz") || e.CallerSymbol.Contains("Foo"));
    }

    [Fact]
    public async Task IndexFileAsync_WithDependencies_PersistsDependencyGraph() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        await using var store = new InMemoryIndexStore();
        var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        await using var persistence = new GraphPersistence(store, fs);

        // 注入含依赖关系的文件(继承)
        await fs.WriteAllText("a.cs", "public interface IFoo { } public class Foo : IFoo { }");
        await index.IndexFileAsync("a.cs", CancellationToken.None).ConfigureAwait(true);

        var preSnap = store.GetSnapshot();
        Assert.True(preSnap.DepEdges.Count > 0, "索引后应有依赖边");

        // 保存 → 加载 → 验证依赖边往返一致
        const string dir = "graph-depedges";
        await persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);
        await using var loadStore = new InMemoryIndexStore();
        await using var loadPersistence = new GraphPersistence(loadStore, fs);
        var loaded = await loadPersistence.LoadAsync(dir, CancellationToken.None).ConfigureAwait(true);

        Assert.True(loaded);
        var postSnap = loadStore.GetSnapshot();
        Assert.Equal(preSnap.DepEdges.Count, postSnap.DepEdges.Count);
    }

    [Fact]
    public async Task RemoveFileAsync_RemovesCallAndDependencyEdges() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        await using var store = new InMemoryIndexStore();
        var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        await using var persistence = new GraphPersistence(store, fs);

        // 索引两个文件
        await fs.WriteAllText("a.cs", "public class A { public void M() { } }");
        await fs.WriteAllText("b.cs", "public class B { public void N() { } }");
        await index.IndexFileAsync("a.cs", CancellationToken.None).ConfigureAwait(true);
        await index.IndexFileAsync("b.cs", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(2, store.GetSnapshot().FileTracking.Count);

        // 移除 a.cs
        await index.RemoveFileAsync("a.cs", CancellationToken.None).ConfigureAwait(true);

        var snap = store.GetSnapshot();
        Assert.False(snap.FileTracking.ContainsKey("a.cs"));
        Assert.True(snap.FileTracking.ContainsKey("b.cs"));
        // a.cs 的符号被移除
        Assert.DoesNotContain(snap.SymbolsByFqn, kvp => kvp.Value.FilePath == "a.cs");

        // 保存 → 加载 → 验证移除后状态一致
        const string dir = "graph-after-remove";
        await persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);
        await using var loadStore = new InMemoryIndexStore();
        await using var loadPersistence = new GraphPersistence(loadStore, fs);
        var loaded = await loadPersistence.LoadAsync(dir, CancellationToken.None).ConfigureAwait(true);

        Assert.True(loaded);
        var postSnap = loadStore.GetSnapshot();
        Assert.Single(postSnap.FileTracking);
        Assert.True(postSnap.FileTracking.ContainsKey("b.cs"));
    }

    [Fact]
    public async Task IndexFileAsync_CrossFileInterface_CorrectsInheritsToImplements() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        await using var store = new InMemoryIndexStore();
        var index = new SymbolIndex(store, fs, new CSharpSymbolExtractor());
        await using var persistence = new GraphPersistence(store, fs);

        // 接口和实现在不同文件
        await fs.WriteAllText("iface.cs", "public interface IFoo { void Bar(); }");
        await fs.WriteAllText("impl.cs", "public class FooImpl : IFoo { public void Bar() { } }");

        // 批量索引(单次 CorrectInheritsToImplements)
        var batch = new List<(string FilePath, string SourceCode, string Hash, ExtractionResult Extraction)>();
        foreach (var path in new[] { "iface.cs", "impl.cs" }) {
            var content = await fs.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true);
            var extraction = new CSharpSymbolExtractor().ExtractAll(content, path);
            batch.Add((path, content, "h", extraction));
        }
        await index.IndexFilesBatchAsync(batch, CancellationToken.None).ConfigureAwait(true);

        var preSnap = store.GetSnapshot();
        // 跨文件 Inherits→Implements 修正
        Assert.All(preSnap.DepEdges, e => Assert.NotEqual(DependencyKind.Inherits, e.DependencyKind));

        // 保存 → 加载 → 修正后状态一致
        const string dir = "graph-cross-file";
        await persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);
        await using var loadStore = new InMemoryIndexStore();
        await using var loadPersistence = new GraphPersistence(loadStore, fs);
        var loaded = await loadPersistence.LoadAsync(dir, CancellationToken.None).ConfigureAwait(true);

        Assert.True(loaded);
        var postSnap = loadStore.GetSnapshot();
        Assert.All(postSnap.DepEdges, e => Assert.NotEqual(DependencyKind.Inherits, e.DependencyKind));
    }

    /// <summary>
    /// SaveAsync 在有数据时能正确保存到磁盘，不抛 "read lock is being released without being held" 异常。
    /// 回归 bug: ReaderWriterLockSlim 锁 scope 跨越 await 调用，线程亲和性导致释放锁抛异常。
    /// </summary>
    [Fact]
    public async Task SaveAsync_WithData_WritesFileWithoutLockException() {
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
    public async Task SaveAsync_ThenLoadAsync_RoundTripsData() {
        PopulateStoreWithData();
        const string dir = "graph-roundtrip";

        await _persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);
        await using var loadStore = new InMemoryIndexStore();
        await using var loadPersistence = new GraphPersistence(loadStore, _fs);
        var loaded = await loadPersistence.LoadAsync(dir, CancellationToken.None).ConfigureAwait(true);
        Assert.True(loaded, "LoadAsync 应返回 true 表示成功加载");

        var snap = loadStore.GetSnapshot();
        Assert.Single(snap.SymbolsByFqn);
        Assert.True(snap.SymbolsByFqn.ContainsKey("A.B.C"));
        Assert.Equal("C", snap.SymbolsByFqn["A.B.C"].Name);

        Assert.Single(snap.CallEdges);
        Assert.Equal("A.B.D", snap.CallEdges[0].CalleeSymbol);
        Assert.Equal(CallKind.Direct, snap.CallEdges[0].CallKind);

        Assert.Single(snap.DepEdges);
        Assert.Equal(DependencyKind.Inherits, snap.DepEdges[0].DependencyKind);

        Assert.Single(snap.Projects);
        Assert.True(snap.Projects.ContainsKey("P.csproj"));
        Assert.Equal("net10.0", snap.Projects["P.csproj"].TargetFramework);

        Assert.Single(snap.ProjectRefs["P.csproj"]);
        Assert.Equal("Q.csproj", snap.ProjectRefs["P.csproj"][0].TargetProjectPath);

        Assert.Single(snap.NuGetRefs["P.csproj"]);
        Assert.Equal("Newtonsoft.Json", snap.NuGetRefs["P.csproj"][0].PackageName);
        Assert.Equal("13.0.1", snap.NuGetRefs["P.csproj"][0].Version);

    }

    /// <summary>
    /// SaveAsync 在空索引时也能正常保存，生成有效 JSON 且可被 LoadAsync 读回。
    /// </summary>
    [Fact]
    public async Task SaveAsync_EmptyStore_WritesValidJson() {
        const string dir = "graph-empty";

        await _persistence.SaveAsync(dir, CancellationToken.None).ConfigureAwait(true);

        var path = Path.Combine(dir, "code-index.json");
        Assert.True(_fs.FileExists(path), "空索引也应生成持久化文件");
        var json = await _fs.ReadAllTextAsync(path, CancellationToken.None).ConfigureAwait(true);
        Assert.Contains("\"symbols\"", json);
        Assert.Contains("\"callEdges\"", json);

        var loaded = await _persistence.LoadAsync(dir, CancellationToken.None).ConfigureAwait(true);
        Assert.True(loaded, "空索引的 JSON 应能被 LoadAsync 成功加载");

        var snap = _store.GetSnapshot();
        Assert.Empty(snap.SymbolsByFqn);
        Assert.Empty(snap.CallEdges);
        Assert.Empty(snap.DepEdges);
        Assert.Empty(snap.Projects);
    }

    /// <summary>
    /// SaveAsync 并发调用不抛锁异常。
    /// 回归 bug: ReaderWriterLockSlim 线程亲和性 — async await 后续体可能在不同线程执行，
    /// 若锁 scope 跨越 await，获取锁线程 != 释放锁线程，抛
    /// "The read lock is being released without being held"。
    /// 修复后锁 scope 限制在同步块内，await 前已释放锁，并发安全。
    /// </summary>
    [Fact]
    public async Task SaveAsync_ConcurrentCalls_DoNotThrowLockException() {
        PopulateStoreWithData();
        const int concurrency = 8;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var dirs = Enumerable.Range(0, concurrency).Select(i => $"graph-concurrent-{i}").ToArray();

        var tasks = dirs.Select(d => Task.Run(async () => {
            try {
                await _persistence.SaveAsync(d, CancellationToken.None).ConfigureAwait(true);
            } catch (Exception ex) {
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(true);

        Assert.Empty(exceptions);
        foreach (var d in dirs) {
            Assert.True(_fs.FileExists(Path.Combine(d, "code-index.json")), $"并发保存后 {d} 应存在文件");
        }
    }

    /// <summary>
    /// ExistsAsync 在已保存目录返回 true。
    /// </summary>
    [Fact]
    public async Task ExistsAsync_AfterSave_ReturnsTrue() {
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
    public async Task ExistsAsync_WithoutSave_ReturnsFalse() {
        const string dir = "graph-exists-false";

        var exists = await _persistence.ExistsAsync(dir, CancellationToken.None).ConfigureAwait(true);
        Assert.False(exists, "未保存的目录 ExistsAsync 应返回 false");
    }

    private void PopulateStoreWithData() {
        _store.Update(snap => snap with {
            SymbolsByFqn = snap.SymbolsByFqn.SetItem("A.B.C", new SymbolInfo {
                Name = "C",
                FullyQualifiedName = "A.B.C",
                Kind = SymbolKind.Class,
                FilePath = "C.cs",
                StartLine = 1,
                EndLine = 10,
                StartColumn = 1,
                EndColumn = 1
            }),
            CallEdges = snap.CallEdges.Add(new CallEdge {
                CallerSymbol = "A.B.C",
                CalleeSymbol = "A.B.D",
                CallSiteFilePath = "C.cs",
                CallSiteLine = 5,
                CallKind = CallKind.Direct
            }),
            DepEdges = snap.DepEdges.Add(new DependencyEdge {
                SourceSymbol = "A.B.C",
                TargetSymbol = "A.B.D",
                DependencyKind = DependencyKind.Inherits,
                SourceFilePath = "C.cs"
            }),
            Projects = snap.Projects.SetItem("P.csproj", new ProjectInfo {
                Name = "P",
                FilePath = "P.csproj",
                TargetFramework = "net10.0"
            }),
            ProjectRefs = snap.ProjectRefs.SetItem("P.csproj",
                ImmutableList.Create(new ProjectReferenceEdge { SourceProjectPath = "P.csproj", TargetProjectPath = "Q.csproj" })),
            NuGetRefs = snap.NuGetRefs.SetItem("P.csproj",
                ImmutableList.Create(new NuGetPackageReference { ProjectPath = "P.csproj", PackageName = "Newtonsoft.Json", Version = "13.0.1" })),
        });
    }
}