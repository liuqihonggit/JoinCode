#pragma warning disable JCC9001, JCC9002
namespace JoinCode.CodeIndex.Tests;

public sealed class GraphVisualizationWikiTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly GraphVisualization _viz;

    public GraphVisualizationWikiTests()
    {
        _store = new InMemoryIndexStore();
        _viz = new GraphVisualization(_store);
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task ExportWikiAsync_EmptyStore_ReturnsHeaderWithNoCommunities()
    {
        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("# Code Architecture Wiki", wiki);
        Assert.Contains("No communities detected", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_SingleCommunity_ContainsCommunitySection()
    {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "src/auth.cs", "Core.Auth");
        InsertSymbol("Login", "Core.Auth.AuthService.Login", SymbolKind.Method, "src/auth.cs", "Core.Auth");
        InsertCallEdge("Core.Auth.AuthService.Login", "Core.Auth.AuthService.Login", "src/auth.cs", 10, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("## Community Overview", wiki);
        Assert.Contains("Community", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_MultipleCommunities_ContainsAllSections()
    {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "src/auth.cs", "Core.Auth");
        InsertSymbol("OrderService", "Core.Orders.OrderService", SymbolKind.Class, "src/orders.cs", "Core.Orders");
        InsertCallEdge("Core.Auth.AuthService", "Core.Auth.AuthService", "src/auth.cs", 5, CallKind.Direct);
        InsertCallEdge("Core.Orders.OrderService", "Core.Orders.OrderService", "src/orders.cs", 5, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("## Community Overview", wiki);
        Assert.Contains("Cohesion", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_CrossCommunityDependency_ShowsDependencySection()
    {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "src/auth.cs", "Core.Auth");
        InsertSymbol("OrderService", "Core.Orders.OrderService", SymbolKind.Class, "src/orders.cs", "Core.Orders");
        InsertCallEdge("Core.Auth.AuthService", "Core.Auth.AuthService", "src/auth.cs", 5, CallKind.Direct);
        InsertCallEdge("Core.Orders.OrderService", "Core.Orders.OrderService", "src/orders.cs", 5, CallKind.Direct);
        InsertCallEdge("Core.Orders.OrderService", "Core.Auth.AuthService", "src/orders.cs", 10, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("Dependencies on other communities", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_SymbolDetails_ContainsKindAndFile()
    {
        InsertSymbol("AuthService", "Core.Auth.AuthService", SymbolKind.Class, "src/auth.cs", "Core.Auth");
        InsertSymbol("Login", "Core.Auth.AuthService.Login", SymbolKind.Method, "src/auth.cs", "Core.Auth");
        InsertCallEdge("Core.Auth.AuthService", "Core.Auth.AuthService.Login", "src/auth.cs", 5, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("AuthService", wiki);
    }

    [Fact]
    public async Task ExportWikiAsync_StatsHeader_ContainsSymbolAndEdgeCount()
    {
        InsertSymbol("Foo", "Ns.Foo", SymbolKind.Class, "src/foo.cs", "Ns");
        InsertSymbol("Bar", "Ns.Bar", SymbolKind.Method, "src/bar.cs", "Ns");
        InsertCallEdge("Ns.Foo", "Ns.Bar", "src/foo.cs", 10, CallKind.Direct);

        var wiki = await _viz.ExportWikiAsync(CancellationToken.None);

        Assert.Contains("**Symbols**: 2", wiki);
        Assert.Contains("**Call edges**: 1", wiki);
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
            Accessibility = "public",
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
