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

    [Fact]
    public async Task DetectCommunitiesAsync_TwoClusters_ReturnsTwoCommunities()
    {
        InsertSymbol("A", "Ns.A", SymbolKind.Method, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Method, "b.cs", "Ns");
        InsertSymbol("C", "Ns.C", SymbolKind.Method, "c.cs", "Ns");
        InsertSymbol("D", "Ns.D", SymbolKind.Method, "d.cs", "Ns");
        InsertCallEdge("Ns.A", "Ns.B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("Ns.C", "Ns.D", "c.cs", 1, CallKind.Direct);

        var communities = await _analytics.DetectCommunitiesAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, communities.Count);
    }

    [Fact]
    public async Task GetHubNodesAsync_TopTwo_ReturnsOrderedByDegree()
    {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "B", "c.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "D", "b.cs", 1, CallKind.Direct);

        var hubs = await _analytics.GetHubNodesAsync(2, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, hubs.Count);
        Assert.Equal("B", hubs[0].SymbolName);
        Assert.Equal(3, hubs[0].TotalDegree);
    }

    [Fact]
    public async Task GetHubNodesAsync_TopNZero_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _analytics.GetHubNodesAsync(0, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task DetectDeadCodeAsync_PrivateMethodWithNoCaller_IsReported()
    {
        InsertMethod("Unused", "Ns.Unused", "file.cs", "private");

        var dead = await _analytics.DetectDeadCodeAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Single(dead);
        Assert.Equal("Ns.Unused", dead[0].SymbolName);
    }

    [Fact]
    public async Task DetectDeadCodeAsync_PublicMethod_IsNotReported()
    {
        InsertMethod("PublicApi", "Ns.PublicApi", "file.cs", "public");

        var dead = await _analytics.DetectDeadCodeAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(dead);
    }

    [Fact]
    public async Task DetectDeadCodeAsync_MethodWithCaller_IsNotReported()
    {
        InsertMethod("Used", "Ns.Used", "file.cs", "private");
        InsertCallEdge("Ns.Caller", "Ns.Used", "file.cs", 1, CallKind.Direct);

        var dead = await _analytics.DetectDeadCodeAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(dead);
    }

    [Fact]
    public async Task ExtractSubgraphAsync_TwoHops_ReturnsExpectedNodes()
    {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "C", "b.cs", 1, CallKind.Direct);

        var result = await _analytics.ExtractSubgraphAsync("A", 2, CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("A", result.Nodes);
        Assert.Contains("B", result.Nodes);
        Assert.Contains("C", result.Nodes);
        Assert.Equal(2, result.Edges.Count);
    }

    [Fact]
    public async Task AnalyzeChangeImpactAsync_ChangedFile_ReachesCallers()
    {
        InsertMethod("Changed", "Ns.Changed", "changed.cs", "public");
        InsertMethod("Caller", "Ns.Caller", "caller.cs", "public");
        InsertCallEdge("Ns.Caller", "Ns.Changed", "caller.cs", 1, CallKind.Direct);

        var result = await _analytics.AnalyzeChangeImpactAsync(["changed.cs"], CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("Ns.Changed", result.AffectedSymbols);
        Assert.Contains("Ns.Caller", result.AffectedSymbols);
        Assert.Contains("changed.cs", result.AffectedFiles);
        Assert.Contains("caller.cs", result.AffectedFiles);
    }

    [Fact]
    public async Task DetectCyclesAsync_NoCycles_ReturnsFalse()
    {
        InsertSymbol("A", "Ns.A", SymbolKind.Method, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Method, "b.cs", "Ns");
        InsertCallEdge("Ns.A", "Ns.B", "a.cs", 1, CallKind.Direct);

        var result = await _analytics.DetectCyclesAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.HasCallCycles);
        Assert.False(result.HasDependencyCycles);
    }

    [Fact]
    public async Task DetectCyclesAsync_CallCycle_Detected()
    {
        InsertSymbol("A", "Ns.A", SymbolKind.Method, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Method, "b.cs", "Ns");
        InsertCallEdge("Ns.A", "Ns.B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("Ns.B", "Ns.A", "b.cs", 1, CallKind.Direct);

        var result = await _analytics.DetectCyclesAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.HasCallCycles);
    }

    [Fact]
    public async Task TopologicalSortByLevelsAsync_LinearChain_ReturnsLevels()
    {
        InsertSymbol("A", "Ns.A", SymbolKind.Method, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Method, "b.cs", "Ns");
        InsertSymbol("C", "Ns.C", SymbolKind.Method, "c.cs", "Ns");
        InsertCallEdge("Ns.A", "Ns.B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("Ns.B", "Ns.C", "b.cs", 1, CallKind.Direct);

        var levels = await _analytics.TopologicalSortByLevelsAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(3, levels.Count);
    }

    [Fact]
    public async Task QueryAsync_NullQuery_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _analytics.QueryAsync(null!, 10, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task FindPathAsync_NullFrom_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _analytics.FindPathAsync(null!, "B", CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExplainAsync_NullSymbolName_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _analytics.ExplainAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    private void InsertMethod(string name, string fqn, string file, string accessibility)
    {
        var symbol = new SymbolInfo
        {
            Name = name,
            FullyQualifiedName = fqn,
            Kind = SymbolKind.Method,
            FilePath = file,
            StartLine = 1,
            EndLine = 1,
            StartColumn = 1,
            EndColumn = 1,
            Accessibility = accessibility,
        };

        using var scope = _store.EnterWriteLock();
        _store.SymbolsByFqn[fqn] = symbol;
        if (!_store.SymbolsByName.TryGetValue(name, out var nameList))
        {
            nameList = new List<SymbolInfo>();
            _store.SymbolsByName[name] = nameList;
        }
        nameList.Add(symbol);
        if (!_store.SymbolsByFile.TryGetValue(file, out var fileList))
        {
            fileList = new List<SymbolInfo>();
            _store.SymbolsByFile[file] = fileList;
        }
        fileList.Add(symbol);
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
