namespace Hands.Tests.ToolHandlers;

using JoinCode.Abstractions.Models.Search;

/// <summary>
/// SearchToolHandlers 扩展工具（search_code/search_text/search_files/SearchCodebase/code_search/symbol_search）单元测试
/// 验证 6 个新工具正确复用 Grep/Glob 搜索逻辑
/// </summary>
public sealed class SearchToolHandlersExtendedTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOpService;
    private readonly Mock<ISearchService> _searchServiceMock;
    private readonly SearchToolHandlers _handler;
    private readonly string _testDir;

    public SearchToolHandlersExtendedTests()
    {
        _fileOpService = new InMemoryFileOperationService();
        _searchServiceMock = new Mock<ISearchService>();
        _handler = new SearchToolHandlers(_searchServiceMock.Object, _fileOpService);
        _testDir = "/test/search";
        _fileOpService.CreateDirectory(_testDir);
    }

    public void Dispose() => _fileOpService.Dispose();

    private static string GetText(ToolResult result) => result.GetTextContent();

    /// <summary>
    /// search_code 工具应调用 GrepSearchAsync 并返回成功结果
    /// </summary>
    [Fact]
    public async Task SearchCodeAsync_WithValidQuery_CallsGrepSearchAndReturnsSuccess()
    {
        // Arrange
        var filenames = new List<string> { $"{_testDir}/file1.cs", $"{_testDir}/file2.cs" };
        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrepSearchResult.SuccessResult("files_with_matches", filenames));

        var options = new SearchCodeOptions { Query = "class" };

        // Act
        var result = await _handler.SearchCodeAsync(options).ConfigureAwait(true);

        // Assert
        result.IsError.Should().BeFalse();
        _searchServiceMock.Verify(
            s => s.GrepSearchAsync(It.Is<GrepSearchInput>(i => i.Pattern == "class"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// search_text 工具应调用 GrepSearchAsync 并返回成功结果
    /// </summary>
    [Fact]
    public async Task SearchTextAsync_WithValidPattern_CallsGrepSearchAndReturnsSuccess()
    {
        // Arrange
        var filenames = new List<string> { $"{_testDir}/file1.cs" };
        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrepSearchResult.SuccessResult("files_with_matches", filenames));

        var options = new SearchTextOptions { Pattern = "TODO" };

        // Act
        var result = await _handler.SearchTextAsync(options).ConfigureAwait(true);

        // Assert
        result.IsError.Should().BeFalse();
        _searchServiceMock.Verify(
            s => s.GrepSearchAsync(It.Is<GrepSearchInput>(i => i.Pattern == "TODO"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// search_files 工具应调用 GlobSearchAsync 并返回成功结果
    /// </summary>
    [Fact]
    public async Task SearchFilesAsync_WithValidPattern_CallsGlobSearchAndReturnsSuccess()
    {
        // Arrange
        var filenames = new List<string> { $"{_testDir}/file1.cs", $"{_testDir}/file2.cs" };
        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(10, filenames, false));

        var options = new SearchFilesOptions { Pattern = "*.cs" };

        // Act
        var result = await _handler.SearchFilesAsync(options).ConfigureAwait(true);

        // Assert
        result.IsError.Should().BeFalse();
        _searchServiceMock.Verify(
            s => s.GlobSearchAsync("*.cs", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// SearchCodebase 工具应调用 GrepSearchAsync 并返回成功结果
    /// </summary>
    [Fact]
    public async Task SearchCodebaseAsync_WithValidQuery_CallsGrepSearchAndReturnsSuccess()
    {
        // Arrange
        var filenames = new List<string> { $"{_testDir}/ChatService.cs" };
        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrepSearchResult.SuccessResult("files_with_matches", filenames));

        var options = new SearchCodebaseOptions { Query = "ChatService" };

        // Act
        var result = await _handler.SearchCodebaseAsync(options).ConfigureAwait(true);

        // Assert
        result.IsError.Should().BeFalse();
        _searchServiceMock.Verify(
            s => s.GrepSearchAsync(It.Is<GrepSearchInput>(i => i.Pattern == "ChatService"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// code_search 工具应调用 GrepSearchAsync 并返回成功结果
    /// </summary>
    [Fact]
    public async Task CodeSearchAsync_WithValidQuery_CallsGrepSearchAndReturnsSuccess()
    {
        // Arrange
        var filenames = new List<string> { $"{_testDir}/iface.cs" };
        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrepSearchResult.SuccessResult("files_with_matches", filenames));

        var options = new CodeSearchOptions { Query = "interface" };

        // Act
        var result = await _handler.CodeSearchAsync(options).ConfigureAwait(true);

        // Assert
        result.IsError.Should().BeFalse();
        _searchServiceMock.Verify(
            s => s.GrepSearchAsync(It.Is<GrepSearchInput>(i => i.Pattern == "interface"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// symbol_search 工具应调用 GrepSearchAsync 且 Pattern 包含符号定义模式（class/interface/struct/enum/void + symbol 名）
    /// </summary>
    [Fact]
    public async Task SymbolSearchAsync_WithValidSymbol_CallsGrepSearchWithSymbolPattern()
    {
        // Arrange
        var filenames = new List<string> { $"{_testDir}/Program.cs" };
        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrepSearchResult.SuccessResult("files_with_matches", filenames));

        var options = new SymbolSearchOptions { Symbol = "Main" };

        // Act
        var result = await _handler.SymbolSearchAsync(options).ConfigureAwait(true);

        // Assert
        result.IsError.Should().BeFalse();
        _searchServiceMock.Verify(
            s => s.GrepSearchAsync(
                It.Is<GrepSearchInput>(i => i.Pattern.Contains("Main", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// search_code 工具当 GrepSearchAsync 失败时应返回错误结果
    /// </summary>
    [Fact]
    public async Task SearchCodeAsync_WhenSearchFails_ReturnsError()
    {
        // Arrange
        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GrepSearchResult.FailureResult("search engine error"));

        var options = new SearchCodeOptions { Query = "class" };

        // Act
        var result = await _handler.SearchCodeAsync(options).ConfigureAwait(true);

        // Assert
        result.IsError.Should().BeTrue();
    }

    /// <summary>
    /// search_files 工具当 GlobSearchAsync 失败时应返回错误结果
    /// </summary>
    [Fact]
    public async Task SearchFilesAsync_WhenSearchFails_ReturnsError()
    {
        // Arrange
        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.FailureResult("glob error"));

        var options = new SearchFilesOptions { Pattern = "*.cs" };

        // Act
        var result = await _handler.SearchFilesAsync(options).ConfigureAwait(true);

        // Assert
        result.IsError.Should().BeTrue();
    }
}
