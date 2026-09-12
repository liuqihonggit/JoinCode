namespace JoinCode.CodeIndex.Tests;

public sealed class SymbolSearcherTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly SymbolIndex _index;
    private readonly SymbolSearcher _searcher;

    public SymbolSearcherTests()
    {
        _store = new InMemoryIndexStore();
        _index = new SymbolIndex(_store, TestFileSystem.Current, new CSharpSymbolExtractor());
        _searcher = new SymbolSearcher(_store);
    }

    public void Dispose()
    {
        _index.Dispose();
        _store.Dispose();
    }

    [Fact]
    public async Task SearchAsync_ExactMatch_ReturnsCorrectSymbol()
    {
        InsertSymbol(CreateSymbol("ProcessOrder", "App.Services.ProcessOrder", SymbolKind.Method, "svc.cs"));

        var result = await _searcher.SearchAsync("ProcessOrder", CancellationToken.None).ConfigureAwait(true);

        Assert.Single(result.Items);
        Assert.Equal("ProcessOrder", result.Items[0].Name);
    }

    [Fact]
    public async Task SearchAsync_PrefixWildcard_ReturnsMatchingSymbols()
    {
        InsertSymbol(CreateSymbol("ProcessOrder", "App.Services.ProcessOrder", SymbolKind.Method, "svc.cs"));
        InsertSymbol(CreateSymbol("ProcessData", "App.Helpers.ProcessData", SymbolKind.Method, "helper.cs"));
        InsertSymbol(CreateSymbol("SaveOrder", "App.Services.SaveOrder", SymbolKind.Method, "svc.cs"));

        var result = await _searcher.SearchAsync("Process*", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, s => s.Name == "ProcessOrder");
        Assert.Contains(result.Items, s => s.Name == "ProcessData");
    }

    [Fact]
    public async Task SearchByKindAsync_FilterByClass_ReturnsOnlyClasses()
    {
        InsertSymbol(CreateSymbol("UserService", "App.UserService", SymbolKind.Class, "svc.cs"));
        InsertSymbol(CreateSymbol("Process", "App.Process", SymbolKind.Method, "svc.cs"));

        var result = await _searcher.SearchByKindAsync(SymbolKind.Class, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(result.Items);
        Assert.Equal(SymbolKind.Class, result.Items[0].Kind);
    }

    [Fact]
    public async Task FindDefinitionAsync_ExactName_ReturnsSymbol()
    {
        InsertSymbol(CreateSymbol("Target", "App.Target", SymbolKind.Method, "target.cs"));

        var result = await _searcher.FindDefinitionAsync("Target", CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.Equal("App.Target", result.FullyQualifiedName);
    }

    [Fact]
    public async Task FindDefinitionAsync_NonExistentName_ReturnsNull()
    {
        var result = await _searcher.FindDefinitionAsync("Missing", CancellationToken.None).ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindReferencesAsync_SameNameDifferentFiles_ReturnsAll()
    {
        InsertSymbol(CreateSymbol("BuildIndex", "App.BuildIndex", SymbolKind.Method, "core.cs"));
        var edge1 = new CallEdge { CallerSymbol = "CallerA", CalleeSymbol = "BuildIndex", CallSiteFilePath = "a.cs", CallSiteLine = 10, CallKind = CallKind.Direct };
        var edge2 = new CallEdge { CallerSymbol = "CallerB", CalleeSymbol = "BuildIndex", CallSiteFilePath = "b.cs", CallSiteLine = 20, CallKind = CallKind.Direct };
        _store.CallEdges.Add(edge1);
        _store.CallEdges.Add(edge2);

        var result = await _searcher.FindReferencesAsync("BuildIndex", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Name == "CallerA" && r.FilePath == "a.cs");
        Assert.Contains(result, r => r.Name == "CallerB" && r.FilePath == "b.cs");
    }

    [Fact]
    public async Task SearchAsync_NoResults_ReturnsEmptyList()
    {
        var result = await _searcher.SearchAsync("NonExistent", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_RecordsElapsedTime()
    {
        InsertSymbol(CreateSymbol("Foo", "App.Foo", SymbolKind.Method, "a.cs"));

        var result = await _searcher.SearchAsync("Foo", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public async Task SearchAsync_NullQuery_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _searcher.SearchAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task SearchAsync_MultipleTokens_AndMatch()
    {
        InsertSymbol(CreateSymbol("ProcessOrder", "App.Services.ProcessOrder", SymbolKind.Method, "svc.cs"));
        InsertSymbol(CreateSymbol("SaveOrder", "App.Services.SaveOrder", SymbolKind.Method, "svc.cs"));

        var result = await _searcher.SearchAsync("Process Order", CancellationToken.None).ConfigureAwait(true);

        Assert.Single(result.Items);
        Assert.Equal("ProcessOrder", result.Items[0].Name);
    }

    [Fact]
    public async Task SearchAsync_CapsAt200Results()
    {
        for (var i = 0; i < 250; i++)
        {
            InsertSymbol(CreateSymbol($"Foo{i}", $"App.Foo{i}", SymbolKind.Method, $"f{i}.cs"));
        }

        var result = await _searcher.SearchAsync("Foo", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(200, result.Items.Count);
    }

    [Fact]
    public async Task SearchByKindAsync_UnknownKind_ReturnsEmpty()
    {
        var result = await _searcher.SearchByKindAsync(SymbolKind.Operator, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task FindReferencesAsync_NonExistentName_ReturnsEmpty()
    {
        var result = await _searcher.FindReferencesAsync("Missing", CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchByPatternAsync_InvalidRegex_ReturnsEmpty()
    {
        InsertSymbol(CreateSymbol("Foo", "App.Foo", SymbolKind.Method, "a.cs"));

        var result = await _searcher.SearchByPatternAsync("[", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchByPatternAsync_MatchesByName_ReturnsSymbols()
    {
        InsertSymbol(CreateSymbol("ProcessOrder", "App.Services.ProcessOrder", SymbolKind.Method, "svc.cs"));
        InsertSymbol(CreateSymbol("ProcessData", "App.Helpers.ProcessData", SymbolKind.Method, "helper.cs"));
        InsertSymbol(CreateSymbol("SaveOrder", "App.Services.SaveOrder", SymbolKind.Method, "svc.cs"));

        var result = await _searcher.SearchByPatternAsync("Process", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, result.TotalCount);
        Assert.Contains(result.Items, s => s.Name == "ProcessOrder");
        Assert.Contains(result.Items, s => s.Name == "ProcessData");
    }

    [Fact]
    public async Task SearchByPatternAsync_RegexPattern_MatchesFqn()
    {
        InsertSymbol(CreateSymbol("Foo", "App.Services.Foo", SymbolKind.Method, "a.cs"));
        InsertSymbol(CreateSymbol("Bar", "App.Helpers.Bar", SymbolKind.Method, "b.cs"));
        InsertSymbol(CreateSymbol("Baz", "App.Services.Baz", SymbolKind.Method, "c.cs"));

        var result = await _searcher.SearchByPatternAsync(@"App\.Services\.", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, s => Assert.Contains("App.Services", s.FullyQualifiedName));
    }

    [Fact]
    public async Task SearchByPatternAsync_NoMatches_ReturnsEmpty()
    {
        InsertSymbol(CreateSymbol("Foo", "App.Foo", SymbolKind.Method, "a.cs"));

        var result = await _searcher.SearchByPatternAsync("NonExistent", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchByPatternAsync_RespectsMaxResults()
    {
        for (var i = 0; i < 5; i++)
        {
            InsertSymbol(CreateSymbol($"Process{i}", $"App.Process{i}", SymbolKind.Method, $"f{i}.cs"));
        }

        var result = await _searcher.SearchByPatternAsync("Process", 2, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task SearchByPatternAsync_NullPattern_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _searcher.SearchByPatternAsync(null!, 10, CancellationToken.None)).ConfigureAwait(true);
    }

    // ============ 测试辅助方法 ============

    private static SymbolInfo CreateSymbol(string name, string fqn, SymbolKind kind, string file)
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

    private void InsertSymbol(SymbolInfo symbol)
    {
        _store.SymbolsByFqn[symbol.FullyQualifiedName] = symbol;
        AddToBucket(_store.SymbolsByName, symbol.Name, symbol);
        AddToBucket(_store.SymbolsByFile, symbol.FilePath, symbol);
        AddToBucket(_store.SymbolsByKind, symbol.Kind, symbol);
    }

    private static void AddToBucket<TKey>(Dictionary<TKey, List<SymbolInfo>> dict, TKey key, SymbolInfo symbol) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<SymbolInfo>();
            dict[key] = list;
        }
        list.Add(symbol);
    }
}
