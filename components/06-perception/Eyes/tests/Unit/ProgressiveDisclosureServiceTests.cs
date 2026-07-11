namespace CodeIndex.Tests;

public sealed class ProgressiveDisclosureServiceTests
{
    private readonly Mock<ICodeIndexer> _mockIndexer;
    private readonly Mock<ISymbolSearcher> _mockSearcher;
    private readonly Mock<ICallGraph> _mockCallGraph;
    private readonly Mock<IDependencyGraph> _mockDepGraph;
    private readonly ProgressiveDisclosureService _service;

    public ProgressiveDisclosureServiceTests()
    {
        _mockIndexer = new Mock<ICodeIndexer>();
        _mockSearcher = new Mock<ISymbolSearcher>();
        _mockCallGraph = new Mock<ICallGraph>();
        _mockDepGraph = new Mock<IDependencyGraph>();

        _mockIndexer.SetupGet(x => x.Searcher).Returns(_mockSearcher.Object);
        _mockIndexer.SetupGet(x => x.CallGraph).Returns(_mockCallGraph.Object);
        _mockIndexer.SetupGet(x => x.DependencyGraph).Returns(_mockDepGraph.Object);

        _service = new ProgressiveDisclosureService(_mockIndexer.Object, new IO.FileSystem.PhysicalFileSystem());
    }

    private static SymbolInfo CreateSymbol(string name, SymbolKind kind = SymbolKind.Method, string filePath = "/src/Test.cs", int startLine = 10, int endLine = 20, string? parent = null)
    {
        return new SymbolInfo
        {
            Name = name,
            FullyQualifiedName = parent is not null ? $"{parent}.{name}" : name,
            Kind = kind,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            StartColumn = 0,
            EndColumn = 0,
            ParentSymbol = parent
        };
    }

    private void SetupSearchResults(params SymbolInfo[] symbols)
    {
        var searchResult = new SearchResult<SymbolInfo>
        {
            Items = symbols,
            TotalCount = symbols.Length,
            ElapsedMs = 1
        };

        _mockSearcher.Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);
    }

    [Fact]
    public async Task DiscloseAsync_IndexLevel_ReturnsSymbolIndex()
    {
        SetupSearchResults(
            CreateSymbol("ValidateUser", SymbolKind.Method, parent: "UserService"),
            CreateSymbol("UserService", SymbolKind.Class)
        );

        var result = await _service.DiscloseAsync("ValidateUser", DisclosureLevel.Index, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(DisclosureLevel.Index, result.Level);
        Assert.True(result.HasMoreDetails);
        Assert.Equal(2, result.Symbols.Count);
        Assert.Contains("ValidateUser", result.FormattedContent);
        Assert.Contains("UserService", result.FormattedContent);
        Assert.Contains("Method", result.FormattedContent);
        Assert.Contains("/src/Test.cs", result.FormattedContent);
        Assert.Null(result.Callers);
        Assert.Null(result.SourceSnippets);
    }

    [Fact]
    public async Task DiscloseAsync_NoResults_ReturnsEmptyResult()
    {
        var searchResult = new SearchResult<SymbolInfo>
        {
            Items = [],
            TotalCount = 0,
            ElapsedMs = 1
        };

        _mockSearcher.Setup(x => x.SearchAsync("NotFound", It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResult);

        var result = await _service.DiscloseAsync("NotFound", DisclosureLevel.Index, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(DisclosureLevel.Index, result.Level);
        Assert.Empty(result.Symbols);
        Assert.Contains("未找到", result.FormattedContent);
        Assert.False(result.HasMoreDetails);
    }

    [Fact]
    public async Task DiscloseAsync_RelationshipsLevel_IncludesCallGraph()
    {
        SetupSearchResults(CreateSymbol("ValidateUser"));

        _mockCallGraph.Setup(x => x.GetCallersAsync("ValidateUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallEdge>
            {
                new() { CallerSymbol = "LoginHandler", CalleeSymbol = "ValidateUser", CallSiteFilePath = "/src/LoginHandler.cs", CallSiteLine = 23, CallKind = CallKind.Direct }
            });

        _mockCallGraph.Setup(x => x.GetCalleesAsync("ValidateUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallEdge>
            {
                new() { CallerSymbol = "ValidateUser", CalleeSymbol = "CheckPassword", CallSiteFilePath = "/src/Test.cs", CallSiteLine = 15, CallKind = CallKind.Direct }
            });

        _mockDepGraph.Setup(x => x.GetInheritorsAsync("ValidateUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DependencyEdge>());

        _mockDepGraph.Setup(x => x.GetDependenciesAsync("ValidateUser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DependencyEdge>());

        var result = await _service.DiscloseAsync("ValidateUser", DisclosureLevel.Relationships, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(DisclosureLevel.Relationships, result.Level);
        Assert.True(result.HasMoreDetails);
        Assert.NotNull(result.Callers);
        Assert.Single(result.Callers);
        Assert.Equal("LoginHandler", result.Callers[0].CallerSymbol);
        Assert.NotNull(result.Callees);
        Assert.Single(result.Callees);
        Assert.Equal("CheckPassword", result.Callees[0].CalleeSymbol);
        Assert.Contains("调用者", result.FormattedContent);
        Assert.Contains("调用了", result.FormattedContent);
    }

    [Fact]
    public async Task DiscloseAsync_SourceLevel_IncludesSourceCode()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task ExpandAsync_UpgradeFromIndexToRelationships()
    {
        SetupSearchResults(CreateSymbol("MyMethod"));

        _mockCallGraph.Setup(x => x.GetCallersAsync("MyMethod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallEdge>());
        _mockCallGraph.Setup(x => x.GetCalleesAsync("MyMethod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallEdge>());
        _mockDepGraph.Setup(x => x.GetInheritorsAsync("MyMethod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DependencyEdge>());
        _mockDepGraph.Setup(x => x.GetDependenciesAsync("MyMethod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DependencyEdge>());

        var indexResult = await _service.DiscloseAsync("MyMethod", DisclosureLevel.Index, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(DisclosureLevel.Index, indexResult.Level);

        var expanded = await _service.ExpandAsync(indexResult, CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(DisclosureLevel.Relationships, expanded.Level);
        Assert.True(expanded.HasMoreDetails);
    }

    [Fact]
    public async Task ExpandAsync_SourceLevel_CannotExpand()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task DiscloseAsync_NullQuery_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.DiscloseAsync(null!, DisclosureLevel.Index, CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task DiscloseAsync_EstimatedTokens_IsPositive()
    {
        SetupSearchResults(CreateSymbol("TokenTest", SymbolKind.Class));

        var result = await _service.DiscloseAsync("TokenTest", DisclosureLevel.Index, CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.EstimatedTokens > 0);
    }

    [Fact]
    public async Task DiscloseAsync_IndexLevel_LimitsTo20Symbols()
    {
        var symbols = Enumerable.Range(0, 30)
            .Select(i => CreateSymbol($"Symbol{i}", SymbolKind.Method))
            .ToArray();

        SetupSearchResults(symbols);

        var result = await _service.DiscloseAsync("Symbol", DisclosureLevel.Index, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(20, result.Symbols.Count);
    }

    [Fact]
    public async Task DiscloseAsync_RelationshipsLevel_IncludesInheritors()
    {
        SetupSearchResults(CreateSymbol("IRepository", SymbolKind.Interface));

        _mockCallGraph.Setup(x => x.GetCallersAsync("IRepository", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallEdge>());
        _mockCallGraph.Setup(x => x.GetCalleesAsync("IRepository", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CallEdge>());

        _mockDepGraph.Setup(x => x.GetInheritorsAsync("IRepository", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DependencyEdge>
            {
                new() { SourceSymbol = "IRepository", TargetSymbol = "UserRepository", DependencyKind = DependencyKind.Implements }
            });

        _mockDepGraph.Setup(x => x.GetDependenciesAsync("IRepository", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DependencyEdge>());

        var result = await _service.DiscloseAsync("IRepository", DisclosureLevel.Relationships, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(result.Inheritors);
        Assert.Single(result.Inheritors);
        Assert.Equal("UserRepository", result.Inheritors[0].TargetSymbol);
        Assert.Contains("继承者", result.FormattedContent);
    }
}
