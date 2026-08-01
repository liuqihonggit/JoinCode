namespace JoinCode.CodeIndex.Tests;

public sealed class InMemoryIndexStoreTests : IDisposable
{
    private readonly InMemoryIndexStore _store;

    public InMemoryIndexStoreTests()
    {
        _store = new InMemoryIndexStore();
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public void Clear_RemovesAllData()
    {
        using (var scope = _store.EnterWriteLock())
        {
            _store.SymbolsByFqn["A"] = new SymbolInfo
            {
                Name = "A",
                FullyQualifiedName = "A",
                Kind = SymbolKind.Class,
                FilePath = "A.cs",
                StartLine = 1,
                EndLine = 1,
                StartColumn = 1,
                EndColumn = 1
            };
            _store.CallEdges.Add(new CallEdge
            {
                CallerSymbol = "A",
                CalleeSymbol = "B",
                CallSiteFilePath = "A.cs",
                CallSiteLine = 1,
                CallKind = CallKind.Direct
            });
            _store.Projects["P"] = new ProjectInfo { Name = "P", FilePath = "P.csproj" };
            _store.FileTracking["f.cs"] = new FileTrackingEntry { FilePath = "f.cs", Hash = "h", SymbolCount = 1, LastModified = DateTimeOffset.UtcNow };
            _store.LastUpdated = DateTimeOffset.UtcNow;
        }

        _store.Clear();

        using (var scope = _store.EnterReadLock())
        {
            Assert.Empty(_store.SymbolsByFqn);
            Assert.Empty(_store.CallEdges);
            Assert.Empty(_store.Projects);
            Assert.Empty(_store.FileTracking);
            Assert.Equal(DateTimeOffset.MinValue, _store.LastUpdated);
        }
    }

    [Fact]
    public void EnterWriteLock_AfterDispose_Throws()
    {
        _store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _store.EnterWriteLock());
    }

    [Fact]
    public void EnterReadLock_AfterDispose_Throws()
    {
        _store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _store.EnterReadLock());
    }

    [Fact]
    public void EnterUpgradeableReadLock_AfterDispose_Throws()
    {
        _store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _store.EnterUpgradeableReadLock());
    }

    [Fact]
    public void Clear_AfterDispose_Throws()
    {
        _store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _store.Clear());
    }

    [Fact]
    public void ReadLock_AllowsConcurrentReads()
    {
        using var scope1 = _store.EnterReadLock();
        using var scope2 = _store.EnterReadLock();

        Assert.True(true);
    }
}
