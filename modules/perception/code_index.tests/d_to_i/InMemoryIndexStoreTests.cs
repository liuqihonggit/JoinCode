namespace JoinCode.CodeIndex.Tests;

public sealed class InMemoryIndexStoreTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private bool _disposed;

    public InMemoryIndexStoreTests() {
        _store = new InMemoryIndexStore();
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _store.Dispose();
    }

    [Fact]
    public void Clear_RemovesAllData() {
        _store.Update(snap => snap with {
            SymbolsByFqn = snap.SymbolsByFqn.SetItem("A", new SymbolInfo {
                Name = "A",
                FullyQualifiedName = "A",
                Kind = SymbolKind.Class,
                FilePath = "A.cs",
                StartLine = 1,
                EndLine = 1,
                StartColumn = 1,
                EndColumn = 1
            }),
            CallEdges = snap.CallEdges.Add(new CallEdge {
                CallerSymbol = "A",
                CalleeSymbol = "B",
                CallSiteFilePath = "A.cs",
                CallSiteLine = 1,
                CallKind = CallKind.Direct
            }),
            Projects = snap.Projects.SetItem("P", new ProjectInfo { Name = "P", FilePath = "P.csproj" }),
            FileTracking = snap.FileTracking.SetItem("f.cs", new FileTrackingEntry { FilePath = "f.cs", Hash = "h", SymbolCount = 1, LastModified = DateTimeOffset.UtcNow }),
            LastUpdated = DateTimeOffset.UtcNow,
        });

        _store.Clear();

        var after = _store.GetSnapshot();
        Assert.Empty(after.SymbolsByFqn);
        Assert.Empty(after.CallEdges);
        Assert.Empty(after.Projects);
        Assert.Empty(after.FileTracking);
        Assert.Equal(DateTimeOffset.MinValue, after.LastUpdated);
    }

    [Fact]
    public async Task Update_AfterDispose_Throws() {
        await _store.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => _store.Update(snap => snap));
    }

    [Fact]
    public async Task Clear_AfterDispose_Throws() {
        await _store.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => _store.Clear());
    }

    // ============ NormalizeKey 确定性测试 ============

    [Fact]
    public void NormalizeKey_UnifiesPathSeparators() {
        var sep = Path.DirectorySeparatorChar;
        Assert.Equal($"a{sep}b{sep}c", InMemoryIndexStore.NormalizeKey("a/b/c"));
        Assert.Equal($"a{sep}b{sep}c", InMemoryIndexStore.NormalizeKey($"a{sep}b{sep}c"));
        Assert.Equal($"a{sep}b{sep}c{sep}d", InMemoryIndexStore.NormalizeKey($"a/b{sep}c/d"));
        Assert.Equal("single", InMemoryIndexStore.NormalizeKey("single"));
        Assert.Equal("", InMemoryIndexStore.NormalizeKey(""));
    }

    [Fact]
    public void NormalizeKey_MixedSeparators_AllBecomePlatformSeparator() {
        var result = InMemoryIndexStore.NormalizeKey("src/lib\\core/test");
        var sep = Path.DirectorySeparatorChar;
        var nonSep = sep == '\\' ? '/' : '\\';
        Assert.DoesNotContain(nonSep.ToString(), result);
        Assert.Contains(sep.ToString(), result);
    }
}
