namespace JoinCode.CodeIndex.Tests;

public sealed class DependencyGraphTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly DependencyGraph _depGraph;
    private bool _disposed;

    public DependencyGraphTests() {
        _store = new InMemoryIndexStore();
        _depGraph = new DependencyGraph(_store);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _store.Dispose();
    }

    [Fact]
    public async Task GetInheritorsAsync_ReturnsTypesInheritingFromSymbol() {
        InsertDepEdge("Dog", "Animal", DependencyKind.Inherits);
        InsertDepEdge("Cat", "Animal", DependencyKind.Inherits);

        var inheritors = await _depGraph.GetInheritorsAsync("Animal", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, inheritors.Count);
        Assert.Contains(inheritors, d => d.SourceSymbol == "Dog");
        Assert.Contains(inheritors, d => d.SourceSymbol == "Cat");
    }

    [Fact]
    public async Task GetDependenciesAsync_ReturnsAllDependenciesOfSymbol() {
        InsertDepEdge("Service", "Logger", DependencyKind.Uses);
        InsertDepEdge("Service", "IRepository", DependencyKind.Implements);

        var deps = await _depGraph.GetDependenciesAsync("Service", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, deps.Count);
    }

    [Fact]
    public async Task GetAffectedFilesAsync_ReturnsFilesAffectedByChange() {
        InsertSymbol("Animal", SymbolKind.Class, "animals.cs");
        InsertSymbol("Dog", SymbolKind.Class, "animals.cs");
        InsertSymbol("Service", SymbolKind.Class, "service.cs");
        InsertDepEdge("Dog", "Animal", DependencyKind.Inherits);
        InsertDepEdge("Service", "Animal", DependencyKind.Uses);

        var files = await _depGraph.GetAffectedFilesAsync("animals.cs", CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("service.cs", files);
    }

    [Fact]
    public async Task GetInheritorsAsync_NoInheritors_ReturnsEmpty() {
        var inheritors = await _depGraph.GetInheritorsAsync("NonExistent", CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(inheritors);
    }

    [Fact]
    public async Task InvalidateCacheForFile_SourceFileInvalidated_RemovesEdgesFromSource() {
        InsertSymbol("Animal", SymbolKind.Class, "base.cs");
        InsertSymbol("Dog", SymbolKind.Class, "derived.cs");
        InsertDepEdge("Dog", "Animal", DependencyKind.Inherits, "derived.cs");

        var inheritorsBefore = await _depGraph.GetInheritorsAsync("Animal", CancellationToken.None).ConfigureAwait(true);
        Assert.Single(inheritorsBefore);

        DeleteDepEdgesForFile("derived.cs");

        await _depGraph.InvalidateCacheForFileAsync("derived.cs", CancellationToken.None).ConfigureAwait(true);

        var inheritorsAfter = await _depGraph.GetInheritorsAsync("Animal", CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(inheritorsAfter);
    }

    [Fact]
    public async Task InvalidateCacheForFile_TargetFileInvalidated_KeepsEdgesFromOtherFiles() {
        InsertSymbol("Animal", SymbolKind.Class, "base.cs");
        InsertSymbol("Dog", SymbolKind.Class, "derived.cs");
        InsertDepEdge("Dog", "Animal", DependencyKind.Inherits, "derived.cs");

        var inheritorsBefore = await _depGraph.GetInheritorsAsync("Animal", CancellationToken.None).ConfigureAwait(true);
        Assert.Single(inheritorsBefore);

        await _depGraph.InvalidateCacheForFileAsync("base.cs", CancellationToken.None).ConfigureAwait(true);

        var inheritorsAfter = await _depGraph.GetInheritorsAsync("Animal", CancellationToken.None).ConfigureAwait(true);
        Assert.Single(inheritorsAfter);
    }

    [Fact]
    public async Task InvalidateCacheForFile_SourceFileInvalidated_ThenReloadWithNewEdges() {
        InsertSymbol("Animal", SymbolKind.Class, "base.cs");
        InsertSymbol("Dog", SymbolKind.Class, "derived.cs");
        InsertDepEdge("Dog", "Animal", DependencyKind.Inherits, "derived.cs");

        DeleteDepEdgesForFile("derived.cs");
        await _depGraph.InvalidateCacheForFileAsync("derived.cs", CancellationToken.None).ConfigureAwait(true);

        InsertDepEdge("Cat", "Animal", DependencyKind.Inherits, "derived2.cs");
        InsertSymbol("Cat", SymbolKind.Class, "derived2.cs");

        await _depGraph.InvalidateCacheForFileAsync("derived2.cs", CancellationToken.None).ConfigureAwait(true);

        var inheritors = await _depGraph.GetInheritorsAsync("Animal", CancellationToken.None).ConfigureAwait(true);
        Assert.Single(inheritors);
        Assert.Equal("Cat", inheritors[0].SourceSymbol);
    }

    [Fact]
    public async Task InvalidateCacheForFile_SourceAndTargetInSameFile_ClearsBothDirections() {
        InsertSymbol("Base", SymbolKind.Class, "file.cs");
        InsertSymbol("Derived", SymbolKind.Class, "file.cs");
        InsertDepEdge("Derived", "Base", DependencyKind.Inherits, "file.cs");

        var inheritorsBefore = await _depGraph.GetInheritorsAsync("Base", CancellationToken.None).ConfigureAwait(true);
        Assert.Single(inheritorsBefore);

        DeleteDepEdgesForFile("file.cs");

        await _depGraph.InvalidateCacheForFileAsync("file.cs", CancellationToken.None).ConfigureAwait(true);

        var inheritorsAfter = await _depGraph.GetInheritorsAsync("Base", CancellationToken.None).ConfigureAwait(true);
        Assert.Empty(inheritorsAfter);
    }

    private void InsertDepEdge(string source, string target, DependencyKind kind, string? sourceFile = null) {
        var edge = new DependencyEdge {
            SourceSymbol = source,
            TargetSymbol = target,
            DependencyKind = kind,
            SourceFilePath = sourceFile
        };
        _store.Update(snap => {
            var depsBySource = AddToBucket(snap.DepsBySource, source, edge);
            var depsByTarget = AddToBucket(snap.DepsByTarget, target, edge);
            var depsByFile = snap.DepsByFile;
            if (!string.IsNullOrEmpty(sourceFile)) {
                depsByFile = AddToBucket(depsByFile, sourceFile, edge);
            }
            return snap with {
                DepEdges = snap.DepEdges.Add(edge),
                DepsBySource = depsBySource,
                DepsByTarget = depsByTarget,
                DepsByFile = depsByFile,
            };
        });
    }

    private void InsertSymbol(string name, SymbolKind kind, string filePath) {
        var symbol = new SymbolInfo {
            Name = name,
            FullyQualifiedName = name,
            Kind = kind,
            FilePath = filePath,
            StartLine = 1,
            EndLine = 1,
            StartColumn = 0,
            EndColumn = 0
        };
        _store.Update(snap => {
            var symbolsByName = AddToBucket(snap.SymbolsByName, name, symbol);
            var symbolsByFile = AddToBucket(snap.SymbolsByFile, filePath, symbol);
            var symbolsByKind = AddToBucket(snap.SymbolsByKind, kind, symbol);
            return snap with {
                SymbolsByFqn = snap.SymbolsByFqn.SetItem(name, symbol),
                SymbolsByName = symbolsByName,
                SymbolsByFile = symbolsByFile,
                SymbolsByKind = symbolsByKind,
            };
        });
    }

    private void DeleteDepEdgesForFile(string filePath) {
        _store.Update(snap => {
            var depEdges = snap.DepEdges.Where(e => e.SourceFilePath != filePath).ToImmutableList();
            var depsBySource = snap.DepsBySource.ToImmutableDictionary(
                kv => kv.Key, kv => kv.Value.Where(e => e.SourceFilePath != filePath).ToImmutableList());
            var depsByTarget = snap.DepsByTarget.ToImmutableDictionary(
                kv => kv.Key, kv => kv.Value.Where(e => e.SourceFilePath != filePath).ToImmutableList());
            var depsByFile = snap.DepsByFile.Remove(filePath);
            return snap with {
                DepEdges = depEdges,
                DepsBySource = depsBySource,
                DepsByTarget = depsByTarget,
                DepsByFile = depsByFile,
            };
        });
    }

    private static ImmutableDictionary<TKey, ImmutableList<TValue>> AddToBucket<TKey, TValue>(
        ImmutableDictionary<TKey, ImmutableList<TValue>> dict, TKey key, TValue value) where TKey : notnull {
        if (!dict.TryGetValue(key, out var list)) {
            list = ImmutableList<TValue>.Empty;
        }
        return dict.SetItem(key, list.Add(value));
    }
}