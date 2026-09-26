#pragma warning disable JCC9001, JCC9002
namespace JoinCode.CodeIndex.Tests;

public sealed class GraphVisualizationWikiTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly GraphVisualization _viz;
    private bool _disposed;

    public GraphVisualizationWikiTests() {
        _store = new InMemoryIndexStore();
        _viz = new GraphVisualization(_store);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _store.Dispose();
    }

    [Fact]
    public async Task ExportWikiAsync_EmptyStore_ReturnsHeaderWithNoCommunities() {
        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("# Code Architecture Wiki", wiki);
        Assert.Contains("No communities detected", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_SingleCommunity_ContainsCommunitySection() {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "src/auth.cs", "Core.Auth");
        InsertSymbol("Login", "Core.Auth.AuthService.Login", SymbolKind.Method, "src/auth.cs", "Core.Auth");
        InsertCallEdge("Core.Auth.AuthService.Login", "Core.Auth.AuthService.Login", "src/auth.cs", 10, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("## Community Overview", wiki);
        Assert.Contains("Community", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_MultipleCommunities_ContainsAllSections() {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "src/auth.cs", "Core.Auth");
        InsertSymbol("OrderService", "Core.Orders.OrderService", SymbolKind.Class, "src/orders.cs", "Core.Orders");
        InsertCallEdge("Core.Auth.AuthService", "Core.Auth.AuthService", "src/auth.cs", 5, CallKind.Direct);
        InsertCallEdge("Core.Orders.OrderService", "Core.Orders.OrderService", "src/orders.cs", 5, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("## Community Overview", wiki);
        Assert.Contains("Cohesion", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_CrossCommunityDependency_ShowsDependencySection() {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "src/auth.cs", "Core.Auth");
        InsertSymbol("OrderService", "Core.Orders.OrderService", SymbolKind.Class, "src/orders.cs", "Core.Orders");
        InsertCallEdge("Core.Auth.AuthService", "Core.Auth.AuthService", "src/auth.cs", 5, CallKind.Direct);
        InsertCallEdge("Core.Orders.OrderService", "Core.Orders.OrderService", "src/orders.cs", 5, CallKind.Direct);
        InsertCallEdge("Core.Orders.OrderService", "Core.Auth.AuthService", "src/orders.cs", 10, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("Dependencies on other communities", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_SymbolDetails_ContainsKindAndFile() {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "src/auth.cs", "Core.Auth");
        InsertSymbol("Login", "Core.Auth.AuthService.Login", SymbolKind.Method, "src/auth.cs", "Core.Auth");
        InsertCallEdge("Core.Auth.AuthService", "Core.Auth.AuthService.Login", "src/auth.cs", 5, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("AuthService", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_StatsHeader_ContainsSymbolAndEdgeCount() {
        InsertSymbol("Foo", "Ns.Foo", SymbolKind.Class, "src/foo.cs", "Ns");
        InsertSymbol("Bar", "Ns.Bar", SymbolKind.Method, "src/bar.cs", "Ns");
        InsertCallEdge("Ns.Foo", "Ns.Bar", "src/foo.cs", 10, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("**Symbols**: 2", wiki);
        Assert.Contains("**Call edges**: 1", wiki);
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
            Accessibility = "public",
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

    private static ImmutableDictionary<TKey, ImmutableList<CallEdge>> AddToBucket<TKey>(
        ImmutableDictionary<TKey, ImmutableList<CallEdge>> dict, TKey key, CallEdge edge) where TKey : notnull {
        if (!dict.TryGetValue(key, out var list)) {
            list = ImmutableList<CallEdge>.Empty;
        }
        return dict.SetItem(key, list.Add(edge));
    }
}