namespace Hands.Tests.ToolHandlers;

using JoinCode.Abstractions.Models.Search;

/// <summary>
/// SearchToolHandlers maxResultSizeChars 截断测试
/// 验证对齐 TS generatePreview: 在换行符处截断，避免切断行内容
/// </summary>
public sealed class SearchToolHandlersSizeLimitTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOpService;
    private readonly Mock<ISearchService> _searchServiceMock;
    private readonly SearchToolHandlers _handler;
    private readonly string _testDir;

    public SearchToolHandlersSizeLimitTests()
    {
        _fileOpService = new InMemoryFileOperationService();
        _searchServiceMock = new Mock<ISearchService>();
        _handler = new SearchToolHandlers(_searchServiceMock.Object, _fileOpService);
        _testDir = "/test/search";
        _fileOpService.CreateDirectory(_testDir);
    }

    public void Dispose() => _fileOpService.Dispose();

    /// <summary>
    /// 获取 ToolResult 的文本内容
    /// </summary>
    private static string GetText(ToolResult result) => result.GetTextContent();

    [Fact]
    public async Task GlobSearchAsync_ResultExceedsMaxSize_TruncatesAtNewline()
    {
        // Arrange: 生成超过 GlobMaxResultSizeChars(100000) 的结果
        var fileCount = 5000;
        var filenames = new List<string>(fileCount);
        for (var i = 0; i < fileCount; i++)
        {
            filenames.Add($"{_testDir}/subdir{i % 10}/very_long_filename_to_increase_output_size_{i}.cs");
        }

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(100, filenames, false));

        // Act
        var result = await _handler.GlobSearchAsync("**/*.cs", _testDir).ConfigureAwait(true);

        // Assert: 结果应被截断
        result.IsError.Should().BeFalse();
        var text = GetText(result);
        text.Length.Should().BeLessThan(filenames.Sum(f => f.Length + Environment.NewLine.Length));
        text.Should().Contain("Result truncated");
    }

    [Fact]
    public async Task GlobSearchAsync_ResultWithinMaxSize_NoTruncation()
    {
        // Arrange: 少量文件，不超过限制
        var filenames = new List<string> { $"{_testDir}/file1.cs", $"{_testDir}/file2.cs" };

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(10, filenames, false));

        // Act
        var result = await _handler.GlobSearchAsync("*.cs", _testDir).ConfigureAwait(true);

        // Assert: 结果不应被截断
        result.IsError.Should().BeFalse();
        GetText(result).Should().NotContain("Result truncated");
    }

    [Fact]
    public async Task GrepSearchAsync_ContentMode_ExceedsMaxSize_TruncatesAtNewline()
    {
        // Arrange: 生成超过 GrepMaxResultSizeChars(20000) 的 content 模式结果
        var largeContent = new StringBuilder();
        for (var i = 0; i < 2000; i++)
        {
            largeContent.AppendLine($"{_testDir}/file{i}.cs:{i}:long line content here to make the output exceed the character limit for grep results");
        }

        var grepResult = GrepSearchResult.SuccessResult(
            "content", ["/fake/file.cs"], largeContent.ToString(), numMatches: 2000);

        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grepResult);

        var options = new GrepSearchOptions { Pattern = "content", OutputMode = "content" };

        // Act
        var result = await _handler.GrepSearchAsync(options).ConfigureAwait(true);

        // Assert: 结果应被截断
        result.IsError.Should().BeFalse();
        var text = GetText(result);
        text.Length.Should().BeLessThan(largeContent.Length);
        text.Should().Contain("Result truncated");
    }

    [Fact]
    public async Task GrepSearchAsync_ContentMode_WithinMaxSize_NoTruncation()
    {
        // Arrange: 少量内容，不超过限制
        var grepResult = GrepSearchResult.SuccessResult(
            "content", ["/fake/file.cs"], "file.cs:1:match found", numMatches: 1);

        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grepResult);

        var options = new GrepSearchOptions { Pattern = "match", OutputMode = "content" };

        // Act
        var result = await _handler.GrepSearchAsync(options).ConfigureAwait(true);

        // Assert: 结果不应被截断
        result.IsError.Should().BeFalse();
        GetText(result).Should().NotContain("Result truncated");
    }

    [Fact]
    public async Task GrepSearchAsync_FilesWithMatchesMode_ExceedsMaxSize_Truncates()
    {
        // Arrange: files_with_matches 模式，大量文件
        var fileCount = 5000;
        var filenames = new List<string>(fileCount);
        for (var i = 0; i < fileCount; i++)
        {
            filenames.Add($"{_testDir}/subdir/very_long_filename_to_exceed_limit_{i}.cs");
        }

        var grepResult = GrepSearchResult.SuccessResult("files_with_matches", filenames);

        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grepResult);

        var options = new GrepSearchOptions { Pattern = "test", OutputMode = "files_with_matches" };

        // Act
        var result = await _handler.GrepSearchAsync(options).ConfigureAwait(true);

        // Assert: 结果应被截断
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("Result truncated");
    }

    [Fact]
    public async Task GrepSearchAsync_CountMode_ExceedsMaxSize_Truncates()
    {
        // Arrange: count 模式，大量文件计数
        var largeContent = new StringBuilder();
        for (var i = 0; i < 2000; i++)
        {
            largeContent.AppendLine($"{_testDir}/file{i}.cs:{i + 1}");
        }

        var grepResult = GrepSearchResult.SuccessResult(
            "count", ["/fake/file.cs"], largeContent.ToString(), numMatches: 5000);

        _searchServiceMock
            .Setup(s => s.GrepSearchAsync(It.IsAny<GrepSearchInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(grepResult);

        var options = new GrepSearchOptions { Pattern = "test", OutputMode = "count" };

        // Act
        var result = await _handler.GrepSearchAsync(options).ConfigureAwait(true);

        // Assert: 结果应被截断
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("Result truncated");
    }
}
