namespace Sync.Tests.ToolHandlers;

public sealed class CodeIndexToolHandlersTests
{
    private readonly Mock<ICodeIndexer> _indexer = new();
    private readonly CodeIndexToolHandlers _handler;

    public CodeIndexToolHandlersTests()
    {
        _handler = new CodeIndexToolHandlers(_indexer.Object);
    }

    [Fact]
    public async Task SearchComprehensiveAsync_ReturnsMatchedSymbolsAndReferences()
    {
        // Arrange: 综合检索返回 1 个匹配符号 + 1 个引用 + 1 个调用方
        var matched = CreateSymbol("GetUser", "App.Services.GetUser", SymbolKind.Method, "svc.cs");
        var reference = CreateSymbol("GetUser", "App.Services.GetUser", SymbolKind.Method, "caller.cs");
        var caller = new CallEdge
        {
            CallerSymbol = "Controller.Handle",
            CalleeSymbol = "GetUser",
            CallSiteFilePath = "ctrl.cs",
            CallSiteLine = 42,
            CallKind = CallKind.Direct
        };

        var result = new ComprehensiveSearchResult
        {
            MatchedSymbols = [matched],
            TotalMatchedCount = 1,
            References = [reference],
            Callers = [caller],
            Callees = [],
            EstimatedTokens = 120,
            Truncated = false,
            TruncatedCount = 0,
            ElapsedMs = 5
        };

        _indexer.Setup(x => x.SearchComprehensiveAsync("GetUser", 2000, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(result);

        // Act
        var toolResult = await _handler.SearchComprehensiveAsync("GetUser").ConfigureAwait(true);

        // Assert: 输出应包含匹配符号、引用、调用方
        Assert.False(toolResult.IsError);
        var text = toolResult.GetTextContent();
        Assert.Contains("GetUser", text);
        Assert.Contains("Controller.Handle", text);
        Assert.Contains("120", text);  // EstimatedTokens
    }

    [Fact]
    public async Task SearchComprehensiveAsync_EmptyPattern_ReturnsError()
    {
        var toolResult = await _handler.SearchComprehensiveAsync("").ConfigureAwait(true);

        Assert.True(toolResult.IsError);
    }

    [Fact]
    public async Task SearchComprehensiveAsync_Truncated_ShowTruncationHint()
    {
        var result = new ComprehensiveSearchResult
        {
            MatchedSymbols = [CreateSymbol("Test", "App.Test", SymbolKind.Method, "t.cs")],
            TotalMatchedCount = 1,
            References = [],
            Callers = [],
            Callees = [],
            EstimatedTokens = 2000,
            Truncated = true,
            TruncatedCount = 7,
            ElapsedMs = 10
        };

        _indexer.Setup(x => x.SearchComprehensiveAsync("Test", 2000, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(result);

        var toolResult = await _handler.SearchComprehensiveAsync("Test").ConfigureAwait(true);

        Assert.False(toolResult.IsError);
        var text = toolResult.GetTextContent();
        Assert.Contains("Test", text);
        // 截断时应显示提示(中文关键词) + 截断条数
        Assert.True(text.Contains("截断") || text.Contains("Truncat"), $"应包含截断提示,实际: {text}");
        Assert.Contains("7", text);  // TruncatedCount
    }

    [Fact]
    public async Task SearchComprehensiveAsync_NoMatches_ReturnsNotFoundMessage()
    {
        var result = new ComprehensiveSearchResult
        {
            MatchedSymbols = [],
            TotalMatchedCount = 0,
            References = [],
            Callers = [],
            Callees = [],
            EstimatedTokens = 0,
            Truncated = false,
            TruncatedCount = 0,
            ElapsedMs = 3
        };

        _indexer.Setup(x => x.SearchComprehensiveAsync("NotExist", 2000, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(result);

        var toolResult = await _handler.SearchComprehensiveAsync("NotExist").ConfigureAwait(true);

        Assert.False(toolResult.IsError);
        var text = toolResult.GetTextContent();
        // 真正无匹配 (TotalMatchedCount==0): 应返回"未找到"提示
        Assert.True(text.Contains("未找到") || text.Contains("No matching"), $"TotalMatchedCount=0 应返回未找到提示,实际: {text}");
    }

    /// <summary>
    /// 回归测试: 当预算过小载不下1个符号时,TotalMatchedCount>0 但 MatchedSymbols.Count==0
    /// 应显示截断提示(而非"未找到") — E2E 测试发现 Async+budget=30 错误返回"未找到匹配"
    /// </summary>
    [Fact]
    public async Task SearchComprehensiveAsync_TinyBudget_TruncatesAllSymbols_ShowsTruncationNotNotFound()
    {
        // 场景: pattern 匹配 50 个符号,但 budget=30 载不下1个 → MatchedSymbols.Count==0 但 TotalMatchedCount=50
        var result = new ComprehensiveSearchResult
        {
            MatchedSymbols = [],   // 全部被截断
            TotalMatchedCount = 50, // 实际匹配 50 个
            References = [],
            Callers = [],
            Callees = [],
            EstimatedTokens = 0,
            Truncated = true,
            TruncatedCount = 50,    // 50 个全被截断
            ElapsedMs = 5
        };

        _indexer.Setup(x => x.SearchComprehensiveAsync("Async", 30, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(result);

        var toolResult = await _handler.SearchComprehensiveAsync("Async", max_token_budget: 30).ConfigureAwait(true);

        Assert.False(toolResult.IsError);
        var text = toolResult.GetTextContent();
        // 应显示截断提示(而非"未找到") — 因为 TotalMatchedCount=50>0
        Assert.True(text.Contains("截断") || text.Contains("Truncat"),
            $"TotalMatchedCount>0 但 MatchedSymbols.Count==0 时应显示截断提示,实际: {text}");
        // 不应显示"未找到"(因为实际有匹配,只是被截断了)
        Assert.False(text.Contains("未找到"),
            $"TotalMatchedCount>0 时不应显示未找到,实际: {text}");
    }

    [Fact]
    public async Task SearchComprehensiveAsync_PassesCustomTokenBudget()
    {
        var result = new ComprehensiveSearchResult
        {
            MatchedSymbols = [CreateSymbol("A", "App.A", SymbolKind.Method, "a.cs")],
            TotalMatchedCount = 1,
            References = [],
            Callers = [],
            Callees = [],
            EstimatedTokens = 10,
            Truncated = false,
            TruncatedCount = 0,
            ElapsedMs = 1
        };

        _indexer.Setup(x => x.SearchComprehensiveAsync("A", 500, It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(result)
            .Verifiable();

        await _handler.SearchComprehensiveAsync("A", max_token_budget: 500).ConfigureAwait(true);

        _indexer.Verify();
    }

    [Fact]
    public async Task SearchComprehensiveAsync_IncludeAstFalse_PassesParameter()
    {
        var result = new ComprehensiveSearchResult
        {
            MatchedSymbols = [CreateSymbol("B", "App.B", SymbolKind.Method, "b.cs")],
            TotalMatchedCount = 1,
            References = [],
            Callers = [],
            Callees = [],
            EstimatedTokens = 5,
            Truncated = false,
            TruncatedCount = 0,
            ElapsedMs = 1
        };

        _indexer.Setup(x => x.SearchComprehensiveAsync("B", 2000, It.IsAny<CancellationToken>(), false))
            .ReturnsAsync(result)
            .Verifiable();

        await _handler.SearchComprehensiveAsync("B", include_ast: false).ConfigureAwait(true);

        _indexer.Verify();
    }

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
}
