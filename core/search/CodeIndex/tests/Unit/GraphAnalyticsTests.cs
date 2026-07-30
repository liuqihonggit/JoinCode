#pragma warning disable JCC9001, JCC9002
namespace JoinCode.CodeIndex.Tests;

public sealed class GraphAnalyticsTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly GraphAnalytics _analytics;

    public GraphAnalyticsTests()
    {
        _store = new InMemoryIndexStore();
        _analytics = new GraphAnalytics(_store);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task QueryAsync_MatchesBySymbolName_ReturnsResults()
    {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "auth.cs", "Core.Auth");
        InsertSymbol("AuthController", "Web.AuthController", SymbolKind.Class, "controller.cs", "Web");
        InsertSymbol("TokenStore", "Core.Auth.TokenStore", SymbolKind.Class, "token.cs", "Core.Auth");

        var result = await _analytics.QueryAsync("auth", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Matches.Count >= 2);
        Assert.Contains(result.Matches, m => m.SymbolName == "Core.Auth.AuthService");
        Assert.Contains(result.Matches, m => m.SymbolName == "Web.AuthController");
    }

    [Fact]
    public async Task QueryAsync_MatchesByFilePath_ReturnsResults()
    {
        InsertSymbol("Process", "Svc.Process", SymbolKind.Method, "handlers/request.cs", "Svc");
        InsertSymbol("Validate", "Svc.Validate", SymbolKind.Method, "validators/check.cs", "Svc");

        var result = await _analytics.QueryAsync("request", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(result.Matches);
        Assert.Equal("Svc.Process", result.Matches[0].SymbolName);
    }

    [Fact]
    public async Task QueryAsync_NoMatches_ReturnsEmpty()
    {
        InsertSymbol("Foo", "Svc.Foo", SymbolKind.Class, "foo.cs", "Svc");

        var result = await _analytics.QueryAsync("nonexistent", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result.Matches);
        Assert.Equal(0, result.TotalMatches);
    }

    [Fact]
    public async Task QueryAsync_RespectsMaxResults()
    {
        for (int i = 0; i < 10; i++)
            InsertSymbol($"Handler{i}", $"Svc.Handler{i}", SymbolKind.Class, $"h{i}.cs", "Svc");

        var result = await _analytics.QueryAsync("Handler", 3, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(3, result.Matches.Count);
        Assert.Equal(10, result.TotalMatches);
    }

    [Fact]
    public async Task QueryAsync_IncludesRelatedSymbols()
    {
        InsertSymbol("Processor", "Svc.Processor", SymbolKind.Class, "processor.cs", "Svc");
        InsertSymbol("Repository", "Svc.Repository", SymbolKind.Class, "repo.cs", "Svc");
        InsertCallEdge("Svc.Processor", "Svc.Repository", "processor.cs", 1, CallKind.Direct);

        var result = await _analytics.QueryAsync("Processor", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(result.Matches);
        Assert.Contains("Svc.Repository", result.Matches[0].RelatedSymbols);
    }

    [Fact]
    public async Task FindPathAsync_DirectCall_ReturnsPath()
    {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);

        var result = await _analytics.FindPathAsync("A", "B", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.PathFound);
        Assert.Equal(["A", "B"], result.PathNodes);
        Assert.Equal(1, result.PathLength);
    }

    [Fact]
    public async Task FindPathAsync_TwoHopPath_ReturnsPath()
    {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "C", "b.cs", 1, CallKind.Direct);

        var result = await _analytics.FindPathAsync("A", "C", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.PathFound);
        Assert.Equal(3, result.PathNodes.Count);
        Assert.Equal("A", result.PathNodes[0]);
        Assert.Equal("C", result.PathNodes[2]);
        Assert.Equal(2, result.PathLength);
    }

    [Fact]
    public async Task FindPathAsync_NoPath_ReturnsNotFound()
    {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "D", "c.cs", 1, CallKind.Direct);

        var result = await _analytics.FindPathAsync("A", "D", CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.PathFound);
        Assert.Equal(-1, result.PathLength);
    }

    [Fact]
    public async Task FindPathAsync_SameSymbol_ReturnsZeroLengthPath()
    {
        var result = await _analytics.FindPathAsync("A", "A", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.PathFound);
        Assert.Equal(["A"], result.PathNodes);
        Assert.Equal(0, result.PathLength);
    }

    [Fact]
    public async Task FindPathAsync_ReverseDirection_ReturnsPath()
    {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "C", "b.cs", 1, CallKind.Direct);

        var result = await _analytics.FindPathAsync("C", "A", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.PathFound);
        Assert.Equal(3, result.PathNodes.Count);
        Assert.Equal("C", result.PathNodes[0]);
        Assert.Equal("A", result.PathNodes[2]);
    }

    [Fact]
    public async Task ExplainAsync_ReturnsAllRelationships()
    {
        InsertSymbol("Service", "Svc.Service", SymbolKind.Class, "svc.cs", "Svc");
        InsertSymbol("Repo", "Svc.Repo", SymbolKind.Class, "svc.cs", "Svc");
        InsertCallEdge("Ctrl.Controller", "Svc.Service", "ctrl.cs", 1, CallKind.Direct);
        InsertCallEdge("Svc.Service", "Svc.Repo", "svc.cs", 2, CallKind.Direct);

        var result = await _analytics.ExplainAsync("Svc.Service", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("Svc.Service", result.SymbolName);
        Assert.Equal("svc.cs", result.FilePath);
        Assert.Equal("Class", result.Kind);
        Assert.Contains("Ctrl.Controller", result.Callers);
        Assert.Contains("Svc.Repo", result.Callees);
        Assert.Equal(1, result.InDegree);
        Assert.Equal(1, result.OutDegree);
    }

    [Fact]
    public async Task ExplainAsync_UnknownSymbol_ReturnsEmptyRelationships()
    {
        var result = await _analytics.ExplainAsync("NonExistent", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("NonExistent", result.SymbolName);
        Assert.Empty(result.Callers);
        Assert.Empty(result.Callees);
        Assert.Equal(0, result.InDegree);
        Assert.Equal(0, result.OutDegree);
    }

    [Fact]
    public async Task ExplainAsync_SameFileSymbols_IncludedInSameFile()
    {
        InsertSymbol("Alpha", "Svc.Alpha", SymbolKind.Class, "shared.cs", "Svc");
        InsertSymbol("Beta", "Svc.Beta", SymbolKind.Class, "shared.cs", "Svc");
        InsertSymbol("Gamma", "Svc.Gamma", SymbolKind.Class, "other.cs", "Svc");

        var result = await _analytics.ExplainAsync("Svc.Alpha", CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("Svc.Beta", result.SameFile);
        Assert.DoesNotContain("Svc.Gamma", result.SameFile);
    }

    [Fact]
    public async Task ExplainAsync_ByNameLookup_ReturnsRelationships()
    {
        InsertSymbol("Service", "Svc.Service", SymbolKind.Class, "svc.cs", "Svc");
        InsertCallEdge("Cli.Client", "Svc.Service", "client.cs", 1, CallKind.Direct);

        var result = await _analytics.ExplainAsync("Service", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("Svc.Service", result.SymbolName);
        Assert.Contains("Cli.Client", result.Callers);
    }

    private void InsertSymbol(string name, string fqn, SymbolKind kind, string filePath, string ns)
    {
        var symbol = new SymbolInfo
        {
            Name = name,
            FullyQualifiedName = fqn,
            Kind = kind,
            FilePath = filePath,
            StartLine = 1,
            EndLine = 10,
            StartColumn = 1,
            EndColumn = 1,
            Namespace = ns,
        };

        using var scope = _store.EnterWriteLock();
        _store.SymbolsByFqn[fqn] = symbol;
        if (!_store.SymbolsByName.TryGetValue(name, out var nameList))
        {
            nameList = new List<SymbolInfo>();
            _store.SymbolsByName[name] = nameList;
        }
        nameList.Add(symbol);
        if (!_store.SymbolsByFile.TryGetValue(filePath, out var fileList))
        {
            fileList = new List<SymbolInfo>();
            _store.SymbolsByFile[filePath] = fileList;
        }
        fileList.Add(symbol);
    }

    private void InsertCallEdge(string caller, string callee, string file, int line, CallKind kind)
    {
        var edge = new CallEdge
        {
            CallerSymbol = caller,
            CalleeSymbol = callee,
            CallSiteFilePath = file,
            CallSiteLine = line,
            CallKind = kind
        };
        using var scope = _store.EnterWriteLock();
        _store.CallEdges.Add(edge);
        AddToBucket(_store.CallsByCaller, caller, edge);
        AddToBucket(_store.CallsByCallee, callee, edge);
        AddToBucket(_store.CallsByFile, file, edge);
    }

    private static void AddToBucket<TKey>(Dictionary<TKey, List<CallEdge>> dict, TKey key, CallEdge edge) where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<CallEdge>();
            dict[key] = list;
        }
        list.Add(edge);
    }
}
