#pragma warning disable JCC9001, JCC9002
namespace JoinCode.CodeIndex.Tests;

public sealed class GraphAnalyticsTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly GraphAnalytics _analytics;
    private bool _disposed;

    public GraphAnalyticsTests() {
        _store = new InMemoryIndexStore();
        _analytics = new GraphAnalytics(_store);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _store.Dispose();
    }

    [Fact]
    public async Task QueryAsync_MatchesBySymbolName_ReturnsResults() {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "auth.cs", "Core.Auth");
        InsertSymbol("AuthController", "Web.AuthController", SymbolKind.Class, "controller.cs", "Web");
        InsertSymbol("TokenStore", "Core.Auth.TokenStore", SymbolKind.Class, "token.cs", "Core.Auth");

        var result = await _analytics.QueryAsync("auth", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Matches.Count >= 2);
        Assert.Contains(result.Matches, m => m.SymbolName == "Core.Auth.AuthService");
        Assert.Contains(result.Matches, m => m.SymbolName == "Web.AuthController");
    }

    [Fact]
    public async Task QueryAsync_MatchesByFilePath_ReturnsResults() {
        InsertSymbol("Process", "Svc.Process", SymbolKind.Method, "handlers/request.cs", "Svc");
        InsertSymbol("Validate", "Svc.Validate", SymbolKind.Method, "validators/check.cs", "Svc");

        var result = await _analytics.QueryAsync("request", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(result.Matches);
        Assert.Equal("Svc.Process", result.Matches[0].SymbolName);
    }

    [Fact]
    public async Task QueryAsync_NoMatches_ReturnsEmpty() {
        InsertSymbol("Foo", "Svc.Foo", SymbolKind.Class, "foo.cs", "Svc");

        var result = await _analytics.QueryAsync("nonexistent", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result.Matches);
        Assert.Equal(0, result.TotalMatches);
    }

    [Fact]
    public async Task QueryAsync_RespectsMaxResults() {
        for (var i = 0; i < 10; i++)
            InsertSymbol($"Handler{i}", $"Svc.Handler{i}", SymbolKind.Class, $"h{i}.cs", "Svc");

        var result = await _analytics.QueryAsync("Handler", 3, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(3, result.Matches.Count);
        Assert.Equal(10, result.TotalMatches);
    }

    [Fact]
    public async Task QueryAsync_IncludesRelatedSymbols() {
        InsertSymbol("Processor", "Svc.Processor", SymbolKind.Class, "processor.cs", "Svc");
        InsertSymbol("Repository", "Svc.Repository", SymbolKind.Class, "repo.cs", "Svc");
        InsertCallEdge("Svc.Processor", "Svc.Repository", "processor.cs", 1, CallKind.Direct);

        var result = await _analytics.QueryAsync("Processor", 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(result.Matches);
        Assert.Contains("Svc.Repository", result.Matches[0].RelatedSymbols);
    }

    [Fact]
    public async Task FindPathAsync_DirectCall_ReturnsPath() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);

        var result = await _analytics.FindPathAsync("A", "B", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.PathFound);
        Assert.Equal(["A", "B"], result.PathNodes);
        Assert.Equal(1, result.PathLength);
    }

    [Fact]
    public async Task FindPathAsync_TwoHopPath_ReturnsPath() {
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
    public async Task FindPathAsync_NoPath_ReturnsNotFound() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "D", "c.cs", 1, CallKind.Direct);

        var result = await _analytics.FindPathAsync("A", "D", CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.PathFound);
        Assert.Equal(-1, result.PathLength);
    }

    [Fact]
    public async Task FindPathAsync_SameSymbol_ReturnsZeroLengthPath() {
        var result = await _analytics.FindPathAsync("A", "A", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.PathFound);
        Assert.Equal(["A"], result.PathNodes);
        Assert.Equal(0, result.PathLength);
    }

    [Fact]
    public async Task FindPathAsync_ReverseDirection_ReturnsPath() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "C", "b.cs", 1, CallKind.Direct);

        var result = await _analytics.FindPathAsync("C", "A", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.PathFound);
        Assert.Equal(3, result.PathNodes.Count);
        Assert.Equal("C", result.PathNodes[0]);
        Assert.Equal("A", result.PathNodes[2]);
    }

    [Fact]
    public async Task ExplainAsync_ReturnsAllRelationships() {
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
    public async Task ExplainAsync_UnknownSymbol_ReturnsEmptyRelationships() {
        var result = await _analytics.ExplainAsync("NonExistent", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("NonExistent", result.SymbolName);
        Assert.Empty(result.Callers);
        Assert.Empty(result.Callees);
        Assert.Equal(0, result.InDegree);
        Assert.Equal(0, result.OutDegree);
    }

    [Fact]
    public async Task ExplainAsync_SameFileSymbols_IncludedInSameFile() {
        InsertSymbol("Alpha", "Svc.Alpha", SymbolKind.Class, "shared.cs", "Svc");
        InsertSymbol("Beta", "Svc.Beta", SymbolKind.Class, "shared.cs", "Svc");
        InsertSymbol("Gamma", "Svc.Gamma", SymbolKind.Class, "other.cs", "Svc");

        var result = await _analytics.ExplainAsync("Svc.Alpha", CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("Svc.Beta", result.SameFile);
        Assert.DoesNotContain("Svc.Gamma", result.SameFile);
    }

    [Fact]
    public async Task ExplainAsync_ByNameLookup_ReturnsRelationships() {
        InsertSymbol("Service", "Svc.Service", SymbolKind.Class, "svc.cs", "Svc");
        InsertCallEdge("Cli.Client", "Svc.Service", "client.cs", 1, CallKind.Direct);

        var result = await _analytics.ExplainAsync("Service", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("Svc.Service", result.SymbolName);
        Assert.Contains("Cli.Client", result.Callers);
    }

    private void InsertSymbol(string name, string fqn, SymbolKind kind, string filePath, string ns) {
        var symbol = new SymbolInfo {
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

        _store.Update(snap => {
            var symbolsByName = snap.SymbolsByName;
            if (!symbolsByName.TryGetValue(name, out var nameList)) {
                nameList = ImmutableList<SymbolInfo>.Empty;
            }
            symbolsByName = symbolsByName.SetItem(name, nameList.Add(symbol));

            var symbolsByFile = snap.SymbolsByFile;
            if (!symbolsByFile.TryGetValue(filePath, out var fileList)) {
                fileList = ImmutableList<SymbolInfo>.Empty;
            }
            symbolsByFile = symbolsByFile.SetItem(filePath, fileList.Add(symbol));

            return snap with {
                SymbolsByFqn = snap.SymbolsByFqn.SetItem(fqn, symbol),
                SymbolsByName = symbolsByName,
                SymbolsByFile = symbolsByFile,
            };
        });
    }

    private void InsertCallEdge(string caller, string callee, string file, int line, CallKind kind) {
        var edge = new CallEdge {
            CallerSymbol = caller,
            CalleeSymbol = callee,
            CallSiteFilePath = file,
            CallSiteLine = line,
            CallKind = kind
        };
        _store.Update(snap => {
            var callsByCaller = AddToBucket(snap.CallsByCaller, caller, edge);
            var callsByCallee = AddToBucket(snap.CallsByCallee, callee, edge);
            var callsByFile = AddToBucket(snap.CallsByFile, file, edge);
            return snap with {
                CallEdges = snap.CallEdges.Add(edge),
                CallsByCaller = callsByCaller,
                CallsByCallee = callsByCallee,
                CallsByFile = callsByFile,
            };
        });
    }

    [Fact]
    public async Task DetectCommunitiesAsync_TwoClusters_ReturnsTwoCommunities() {
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
    public async Task GetHubNodesAsync_TopTwo_ReturnsOrderedByDegree() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "B", "c.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "D", "b.cs", 1, CallKind.Direct);

        var hubs = await _analytics.GetHubNodesAsync(2, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, hubs.Count);
        Assert.Equal("B", hubs[0].SymbolName);
        Assert.Equal(3, hubs[0].TotalDegree);
    }

    [Fact]
    public async Task GetHubNodesAsync_TopNZero_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _analytics.GetHubNodesAsync(0, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task DetectDeadCodeAsync_PrivateMethodWithNoCaller_IsReported() {
        InsertMethod("Unused", "Ns.Unused", "file.cs", "private");

        var dead = await _analytics.DetectDeadCodeAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Single(dead);
        Assert.Equal("Ns.Unused", dead[0].SymbolName);
    }

    [Fact]
    public async Task DetectDeadCodeAsync_PublicMethod_IsNotReported() {
        InsertMethod("PublicApi", "Ns.PublicApi", "file.cs", "public");

        var dead = await _analytics.DetectDeadCodeAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(dead);
    }

    [Fact]
    public async Task DetectDeadCodeAsync_MethodWithCaller_IsNotReported() {
        InsertMethod("Used", "Ns.Used", "file.cs", "private");
        InsertCallEdge("Ns.Caller", "Ns.Used", "file.cs", 1, CallKind.Direct);

        var dead = await _analytics.DetectDeadCodeAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(dead);
    }

    [Fact]
    public async Task ExtractSubgraphAsync_TwoHops_ReturnsExpectedNodes() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "C", "b.cs", 1, CallKind.Direct);

        var result = await _analytics.ExtractSubgraphAsync("A", 2, CancellationToken.None).ConfigureAwait(true);

        Assert.Contains("A", result.Nodes);
        Assert.Contains("B", result.Nodes);
        Assert.Contains("C", result.Nodes);
        Assert.Equal(2, result.Edges.Count);
    }

    [Fact]
    public async Task AnalyzeChangeImpactAsync_ChangedFile_ReachesCallers() {
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
    public async Task DetectCyclesAsync_NoCycles_ReturnsFalse() {
        InsertSymbol("A", "Ns.A", SymbolKind.Method, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Method, "b.cs", "Ns");
        InsertCallEdge("Ns.A", "Ns.B", "a.cs", 1, CallKind.Direct);

        var result = await _analytics.DetectCyclesAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.HasCallCycles);
        Assert.False(result.HasDependencyCycles);
    }

    [Fact]
    public async Task DetectCyclesAsync_CallCycle_Detected() {
        InsertSymbol("A", "Ns.A", SymbolKind.Method, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Method, "b.cs", "Ns");
        InsertCallEdge("Ns.A", "Ns.B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("Ns.B", "Ns.A", "b.cs", 1, CallKind.Direct);

        var result = await _analytics.DetectCyclesAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.HasCallCycles);
    }

    [Fact]
    public async Task TopologicalSortByLevelsAsync_LinearChain_ReturnsLevels() {
        InsertSymbol("A", "Ns.A", SymbolKind.Method, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Method, "b.cs", "Ns");
        InsertSymbol("C", "Ns.C", SymbolKind.Method, "c.cs", "Ns");
        InsertCallEdge("Ns.A", "Ns.B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("Ns.B", "Ns.C", "b.cs", 1, CallKind.Direct);

        var levels = await _analytics.TopologicalSortByLevelsAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(3, levels.Count);
    }

    [Fact]
    public async Task QueryAsync_NullQuery_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _analytics.QueryAsync(null!, 10, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task FindPathAsync_NullFrom_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _analytics.FindPathAsync(null!, "B", CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ExplainAsync_NullSymbolName_Throws() {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _analytics.ExplainAsync(null!, CancellationToken.None)).ConfigureAwait(true);
    }

    private void InsertMethod(string name, string fqn, string file, string accessibility) {
        var symbol = new SymbolInfo {
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

        _store.Update(snap => {
            var symbolsByName = snap.SymbolsByName;
            if (!symbolsByName.TryGetValue(name, out var nameList)) {
                nameList = ImmutableList<SymbolInfo>.Empty;
            }
            symbolsByName = symbolsByName.SetItem(name, nameList.Add(symbol));

            var symbolsByFile = snap.SymbolsByFile;
            if (!symbolsByFile.TryGetValue(file, out var fileList)) {
                fileList = ImmutableList<SymbolInfo>.Empty;
            }
            symbolsByFile = symbolsByFile.SetItem(file, fileList.Add(symbol));

            return snap with {
                SymbolsByFqn = snap.SymbolsByFqn.SetItem(fqn, symbol),
                SymbolsByName = symbolsByName,
                SymbolsByFile = symbolsByFile,
            };
        });
    }

    private static ImmutableHamT<TKey, ImmutableList<CallEdge>> AddToBucket<TKey>(
        ImmutableHamT<TKey, ImmutableList<CallEdge>> dict, TKey key, CallEdge edge) where TKey : notnull {
        if (!dict.TryGetValue(key, out var list)) {
            list = ImmutableList<CallEdge>.Empty;
        }
        return dict.SetItem(key, list.Add(edge));
    }

    // ============ LabelPropagation 确定性测试 ============

    private static ImmutableHamT<string, ImmutableList<CallEdge>> EmptyCallIndex()
        => ImmutableHamT<string, ImmutableList<CallEdge>>.Empty.WithComparers(StringComparer.Ordinal);

    [Fact]
    public void LabelPropagation_EmptyGraph_ReturnsEmptyLabels() {
        var labels = GraphAnalytics.LabelPropagation(EmptyCallIndex(), EmptyCallIndex());
        Assert.Empty(labels);
    }

    [Fact]
    public void LabelPropagation_SingleEdge_TwoSymbolsSameCommunity() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        var snap = _store.GetSnapshot();

        var labels = GraphAnalytics.LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);

        Assert.Equal(2, labels.Count);
        // A 和 B 应收敛到同一标签
        Assert.Equal(labels["A"], labels["B"]);
    }

    [Fact]
    public void LabelPropagation_Chain_ThreeSymbolsConverge() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "C", "b.cs", 1, CallKind.Direct);
        var snap = _store.GetSnapshot();

        var labels = GraphAnalytics.LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);

        Assert.Equal(3, labels.Count);
        // 链式 A→B→C 应收敛到同一社区
        Assert.Equal(labels["A"], labels["B"]);
        Assert.Equal(labels["B"], labels["C"]);
    }

    [Fact]
    public void LabelPropagation_Cycle_TwoSymbolsSameCommunity() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "A", "b.cs", 1, CallKind.Direct);
        var snap = _store.GetSnapshot();

        var labels = GraphAnalytics.LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);

        Assert.Equal(labels["A"], labels["B"]);
    }

    [Fact]
    public void LabelPropagation_TwoDisconnectedClusters_DifferentLabels() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "D", "c.cs", 1, CallKind.Direct);
        var snap = _store.GetSnapshot();

        var labels = GraphAnalytics.LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);

        Assert.Equal(4, labels.Count);
        // A-B 同社区,C-D 同社区,但两个社区不同
        Assert.Equal(labels["A"], labels["B"]);
        Assert.Equal(labels["C"], labels["D"]);
        Assert.NotEqual(labels["A"], labels["C"]);
    }

    [Fact]
    public void LabelPropagation_TerminatesWithin20Iterations() {
        // 构造较大图验证不无限循环(20次迭代上限)
        for (var i = 0; i < 10; i++)
            InsertCallEdge($"N{i}", $"N{(i + 1) % 10}", $"f{i}.cs", 1, CallKind.Direct);
        var snap = _store.GetSnapshot();

        var labels = GraphAnalytics.LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);

        Assert.Equal(10, labels.Count);
    }

    // ============ BuildCommunities 确定性测试 ============

    [Fact]
    public void BuildCommunities_SingleCommunity_StatsCorrect() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "A", "b.cs", 1, CallKind.Direct);
        var snap = _store.GetSnapshot();

        var labels = GraphAnalytics.LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);
        var communities = GraphAnalytics.BuildCommunities(labels, snap.CallsByCaller, snap.CallsByCallee);

        Assert.Single(communities);
        Assert.Equal(2, communities[0].MemberCount);
        // A→B 和 B→A 都是内部边
        Assert.Equal(2, communities[0].InternalEdges);
        Assert.Equal(0, communities[0].ExternalEdges);
    }

    [Fact]
    public void BuildCommunities_TwoCommunities_ReturnsBoth() {
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "D", "c.cs", 1, CallKind.Direct);
        var snap = _store.GetSnapshot();

        var labels = GraphAnalytics.LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);
        var communities = GraphAnalytics.BuildCommunities(labels, snap.CallsByCaller, snap.CallsByCallee);

        Assert.Equal(2, communities.Count);
        // 按成员数降序
        Assert.True(communities[0].MemberCount >= communities[1].MemberCount);
    }

    [Fact]
    public void BuildCommunities_ExternalEdgesCounted() {
        // 两个独立社区 + 跨社区边 A→C
        InsertCallEdge("A", "B", "a.cs", 1, CallKind.Direct);
        InsertCallEdge("B", "A", "b.cs", 1, CallKind.Direct);
        InsertCallEdge("C", "D", "c.cs", 1, CallKind.Direct);
        InsertCallEdge("D", "C", "d.cs", 1, CallKind.Direct);
        InsertCallEdge("A", "C", "a.cs", 2, CallKind.Direct); // 跨社区边
        var snap = _store.GetSnapshot();

        var labels = GraphAnalytics.LabelPropagation(snap.CallsByCaller, snap.CallsByCallee);
        var communities = GraphAnalytics.BuildCommunities(labels, snap.CallsByCaller, snap.CallsByCallee);

        // A→C 跨社区,至少一个社区有外部边
        Assert.Contains(communities, c => c.ExternalEdges > 0);
    }

    [Fact]
    public void BuildCommunities_EmptyGraph_ReturnsEmpty() {
        var communities = GraphAnalytics.BuildCommunities([], EmptyCallIndex(), EmptyCallIndex());
        Assert.Empty(communities);
    }

    // ============ IsEntryPoint 确定性测试 ============

    private static SymbolInfo SymForEntry(string name, SymbolKind kind) => new() {
        Name = name, FullyQualifiedName = name, Kind = kind,
        FilePath = "t.cs", StartLine = 1, EndLine = 1, StartColumn = 1, EndColumn = 1,
    };

    [Theory]
    [InlineData("Main")]
    [InlineData("MainAsync")]
    [InlineData("Program")]
    public void IsEntryPoint_WellKnownNames_ReturnsTrue(string name) {
        Assert.True(GraphAnalytics.IsEntryPoint(SymForEntry(name, SymbolKind.Method)));
    }

    [Fact]
    public void IsEntryPoint_OnPrefixMethod_ReturnsTrue() {
        Assert.True(GraphAnalytics.IsEntryPoint(SymForEntry("OnClick", SymbolKind.Method)));
        Assert.True(GraphAnalytics.IsEntryPoint(SymForEntry("OnPropertyChanged", SymbolKind.Method)));
    }

    [Fact]
    public void IsEntryPoint_OnPrefixNonMethod_ReturnsFalse() {
        // On 前缀但 Kind 不是 Method → false
        Assert.False(GraphAnalytics.IsEntryPoint(SymForEntry("OnClick", SymbolKind.Class)));
    }

    [Fact]
    public void IsEntryPoint_RegularMethod_ReturnsFalse() {
        Assert.False(GraphAnalytics.IsEntryPoint(SymForEntry("Process", SymbolKind.Method)));
        Assert.False(GraphAnalytics.IsEntryPoint(SymForEntry("Validate", SymbolKind.Method)));
    }

    [Fact]
    public void IsEntryPoint_LowerCaseOn_ReturnsFalse() {
        // StartsWith("On") 大小写敏感
        Assert.False(GraphAnalytics.IsEntryPoint(SymForEntry("onClick", SymbolKind.Method)));
    }

    // ============ FindFilePath 确定性测试 ============

    [Fact]
    public void FindFilePath_FqnMatch_ReturnsFilePath() {
        InsertSymbol("Foo", "Ns.Foo", SymbolKind.Class, "foo.cs", "Ns");
        var snap = _store.GetSnapshot();

        Assert.Equal("foo.cs", GraphAnalytics.FindFilePath(snap, "Ns.Foo"));
    }

    [Fact]
    public void FindFilePath_NameMatch_ReturnsFirstFilePath() {
        InsertSymbol("Foo", "Ns.Foo", SymbolKind.Class, "foo.cs", "Ns");
        var snap = _store.GetSnapshot();

        Assert.Equal("foo.cs", GraphAnalytics.FindFilePath(snap, "Foo"));
    }

    [Fact]
    public void FindFilePath_NoMatch_ReturnsNull() {
        InsertSymbol("Foo", "Ns.Foo", SymbolKind.Class, "foo.cs", "Ns");
        var snap = _store.GetSnapshot();

        Assert.Null(GraphAnalytics.FindFilePath(snap, "NonExistent"));
    }

    [Fact]
    public void FindFilePath_EmptySnapshot_ReturnsNull() {
        var snap = _store.GetSnapshot();
        Assert.Null(GraphAnalytics.FindFilePath(snap, "Anything"));
    }

    // ============ BuildCallDag 确定性测试 ============

    [Fact]
    public void BuildCallDag_SymbolsBecomeNodes() {
        InsertSymbol("A", "Ns.A", SymbolKind.Method, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Method, "b.cs", "Ns");
        var snap = _store.GetSnapshot();

        var dag = GraphAnalytics.BuildCallDag(snap);

        Assert.Equal(2, dag.Nodes.Count);
        Assert.True(dag.Nodes.ContainsKey("Ns.A"));
        Assert.True(dag.Nodes.ContainsKey("Ns.B"));
    }

    [Fact]
    public void BuildCallDag_CallEdgesBecomeDagEdges() {
        InsertSymbol("A", "Ns.A", SymbolKind.Method, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Method, "b.cs", "Ns");
        InsertCallEdge("Ns.A", "Ns.B", "a.cs", 1, CallKind.Direct);
        var snap = _store.GetSnapshot();

        var dag = GraphAnalytics.BuildCallDag(snap);

        Assert.True(dag.TryGetEdge("Ns.A", "Ns.B", out var edge));
        Assert.Equal("Direct", edge.Label);
    }

    [Fact]
    public void BuildCallDag_EdgeOnlySymbols_BecomeNodes() {
        // 调用边引用了不在 SymbolsByFqn 中的符号 → 仍应成为节点
        InsertCallEdge("Ghost", "Phantom", "g.cs", 1, CallKind.Direct);
        var snap = _store.GetSnapshot();

        var dag = GraphAnalytics.BuildCallDag(snap);

        Assert.True(dag.Nodes.ContainsKey("Ghost"));
        Assert.True(dag.Nodes.ContainsKey("Phantom"));
    }

    [Fact]
    public void BuildCallDag_EmptySnapshot_NoNodesNoEdges() {
        var snap = _store.GetSnapshot();
        var dag = GraphAnalytics.BuildCallDag(snap);
        Assert.Empty(dag.Nodes);
        Assert.Empty(dag.Edges);
    }

    // ============ BuildDependencyDag 确定性测试 ============

    [Fact]
    public void BuildDependencyDag_SymbolsBecomeNodes() {
        InsertSymbol("A", "Ns.A", SymbolKind.Class, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Class, "b.cs", "Ns");
        var snap = _store.GetSnapshot();

        var dag = GraphAnalytics.BuildDependencyDag(snap);

        Assert.Equal(2, dag.Nodes.Count);
        Assert.True(dag.Nodes.ContainsKey("Ns.A"));
        Assert.True(dag.Nodes.ContainsKey("Ns.B"));
    }

    [Fact]
    public void BuildDependencyDag_DepEdgesBecomeDagEdges() {
        InsertSymbol("A", "Ns.A", SymbolKind.Class, "a.cs", "Ns");
        InsertSymbol("B", "Ns.B", SymbolKind.Class, "b.cs", "Ns");
        _store.Update(snap => snap with {
            DepEdges = snap.DepEdges.Add(new DependencyEdge {
                SourceSymbol = "Ns.A", TargetSymbol = "Ns.B",
                DependencyKind = DependencyKind.Inherits, SourceFilePath = "a.cs",
            }),
        });
        var snap = _store.GetSnapshot();

        var dag = GraphAnalytics.BuildDependencyDag(snap);

        Assert.True(dag.TryGetEdge("Ns.A", "Ns.B", out var edge));
        Assert.Equal("Inherits", edge.Label);
    }

    [Fact]
    public void BuildDependencyDag_EdgeOnlySymbols_BecomeNodes() {
        _store.Update(snap => snap with {
            DepEdges = snap.DepEdges.Add(new DependencyEdge {
                SourceSymbol = "Ghost", TargetSymbol = "Phantom",
                DependencyKind = DependencyKind.Uses, SourceFilePath = "g.cs",
            }),
        });
        var snap = _store.GetSnapshot();

        var dag = GraphAnalytics.BuildDependencyDag(snap);

        Assert.True(dag.Nodes.ContainsKey("Ghost"));
        Assert.True(dag.Nodes.ContainsKey("Phantom"));
    }

    [Fact]
    public void BuildDependencyDag_EmptySnapshot_NoNodesNoEdges() {
        var snap = _store.GetSnapshot();
        var dag = GraphAnalytics.BuildDependencyDag(snap);
        Assert.Empty(dag.Nodes);
        Assert.Empty(dag.Edges);
    }
}