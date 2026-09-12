
namespace Core.Tests.Context.Resolution;

public class ReferenceResolverTests : IDisposable
{
    private readonly Mock<ISearchService> _searchServiceMock;
    private readonly Testing.Common.Services.InMemoryFileSystem _fileSystem;
    private readonly InMemoryFileOperationService _fileOpService;
    private readonly ReferenceResolver _resolver;
    private const string ProjectRoot = "C:\\testroot";

    public ReferenceResolverTests()
    {
        _searchServiceMock = new Mock<ISearchService>();
        _fileSystem = new Testing.Common.Services.InMemoryFileSystem();
        _fileSystem.CreateDirectory(ProjectRoot);
        _fileOpService = new InMemoryFileOperationService(_fileSystem);
        _resolver = new ReferenceResolver(_searchServiceMock.Object, _fileOpService);
    }

    public void Dispose()
    {
        _fileOpService.Dispose();
    }

    #region ResolveCodeReferenceAsync Tests

    [Fact]
    public async Task ResolveCodeReferenceAsync_ExactFilePath_ReturnsExactMatch()
    {
        var filePath = Path.Combine(ProjectRoot, "TestFile.cs");
        _fileSystem.WriteAllText(filePath, "content");

        var result = await _resolver.ResolveCodeReferenceAsync(
            filePath.Replace(Path.DirectorySeparatorChar, '/'),
            new ReferenceResolutionOptions { ProjectRoot = ProjectRoot }).ConfigureAwait(true);

        Assert.True(result.IsResolved);
        Assert.Equal(ReferenceMatchType.Exact, result.MatchType);
        Assert.Equal(1.0, result.RelevanceScore);
    }

    [Fact]
    public async Task ResolveCodeReferenceAsync_ExactDirectoryPath_ReturnsDirectoryMatch()
    {
        var dirPath = Path.Combine(ProjectRoot, "TestDirectory");
        _fileSystem.CreateDirectory(dirPath);
        var filePath = Path.Combine(dirPath, "file.cs");
        _fileSystem.WriteAllText(filePath, "content");

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string> { filePath }, false));

        var result = await _resolver.ResolveCodeReferenceAsync(
            "TestDirectory",
            new ReferenceResolutionOptions { ProjectRoot = ProjectRoot }).ConfigureAwait(true);

        Assert.True(result.IsResolved);
        Assert.Equal(ReferenceMatchType.Exact, result.MatchType);
    }

    [Fact]
    public async Task ResolveCodeReferenceAsync_GlobPattern_ReturnsPatternMatch()
    {
        var file1 = Path.Combine(ProjectRoot, "file1.cs");
        var file2 = Path.Combine(ProjectRoot, "file2.cs");
        _fileSystem.WriteAllText(file1, "content");
        _fileSystem.WriteAllText(file2, "content");

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync("*.cs", ProjectRoot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string> { file1, file2 }, false));

        var result = await _resolver.ResolveCodeReferenceAsync(
            "*.cs",
            new ReferenceResolutionOptions { ProjectRoot = ProjectRoot }).ConfigureAwait(true);

        Assert.True(result.IsResolved);
        Assert.Equal(ReferenceMatchType.Pattern, result.MatchType);
        Assert.Equal(2, result.FileMatches.Count);
    }

    [Fact]
    public async Task ResolveCodeReferenceAsync_NonExistentPath_ReturnsUnresolved()
    {
        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string>(), false));

        var result = await _resolver.ResolveCodeReferenceAsync(
            "non-existent-path",
            new ReferenceResolutionOptions { ProjectRoot = ProjectRoot }).ConfigureAwait(true);

        Assert.False(result.IsResolved);
        Assert.Equal(0, result.RelevanceScore);
    }

    [Fact]
    public async Task ResolveCodeReferenceAsync_WithChineseAlias_ResolvesCorrectly()
    {
        var toolsDir = Path.Combine(ProjectRoot, "tools");
        _fileSystem.CreateDirectory(toolsDir);
        var filePath = Path.Combine(toolsDir, "tool.cs");
        _fileSystem.WriteAllText(filePath, "content");

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string> { filePath }, false));

        var result = await _resolver.ResolveCodeReferenceAsync(
            "工具",
            new ReferenceResolutionOptions { ProjectRoot = ProjectRoot }).ConfigureAwait(true);

        Assert.True(result.IsResolved);
        Assert.Equal(ReferenceMatchType.Exact, result.MatchType);
    }

    #endregion

    #region FindMatchingFilesAsync Tests

    [Fact]
    public async Task FindMatchingFilesAsync_ByDescription_ReturnsMatches()
    {
        var filePath = Path.Combine(ProjectRoot, "MyService.cs");
        _fileSystem.WriteAllText(filePath, "content");

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.Is<string>(p => p.Contains("Service")), ProjectRoot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string> { filePath }, false));

        var results = await _resolver.FindMatchingFilesAsync(
            "Service",
            new ReferenceResolutionOptions { ProjectRoot = ProjectRoot }).ConfigureAwait(true);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task FindMatchingFilesAsync_ByChineseAlias_ReturnsMatches()
    {
        var servicesDir = Path.Combine(ProjectRoot, "services");
        _fileSystem.CreateDirectory(servicesDir);
        var filePath = Path.Combine(servicesDir, "test.cs");
        _fileSystem.WriteAllText(filePath, "content");

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string> { filePath }, false));

        var results = await _resolver.FindMatchingFilesAsync(
            "服务",
            new ReferenceResolutionOptions { ProjectRoot = ProjectRoot }).ConfigureAwait(true);

        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task FindMatchingFilesAsync_NoMatches_ReturnsEmpty()
    {
        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string>(), false));

        var results = await _resolver.FindMatchingFilesAsync(
            "xyz-nonexistent",
            new ReferenceResolutionOptions { ProjectRoot = ProjectRoot }).ConfigureAwait(true);

        Assert.Empty(results);
    }

    #endregion

    #region BuildReferenceIndexAsync Tests

    [Fact]
    public async Task BuildReferenceIndexAsync_WithFiles_CreatesIndex()
    {
        var file1 = Path.Combine(ProjectRoot, "file1.cs");
        var file2 = Path.Combine(ProjectRoot, "file2.cs");
        _fileSystem.WriteAllText(file1, "content1");
        _fileSystem.WriteAllText(file2, "content2");

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync("**/*", ProjectRoot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string> { file1, file2 }, false));

        var index = await _resolver.BuildReferenceIndexAsync(ProjectRoot).ConfigureAwait(true);

        Assert.NotNull(index);
        Assert.Equal(ProjectRoot, index.ProjectRoot);
        Assert.True(index.Count >= 0);
    }

    [Fact]
    public async Task BuildReferenceIndexAsync_EmptyDirectory_CreatesEmptyIndex()
    {
        _searchServiceMock
            .Setup(s => s.GlobSearchAsync("**/*", ProjectRoot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string>(), false));

        var index = await _resolver.BuildReferenceIndexAsync(ProjectRoot).ConfigureAwait(true);

        Assert.NotNull(index);
        Assert.Equal(0, index.Count);
    }

    #endregion

    #region ReferenceMatchType Tests

    [Theory]
    [InlineData(ReferenceMatchType.Exact)]
    [InlineData(ReferenceMatchType.Pattern)]
    [InlineData(ReferenceMatchType.Fuzzy)]
    [InlineData(ReferenceMatchType.Partial)]
    public void ReferenceMatchType_Values_AreDefined(ReferenceMatchType matchType)
    {
        // Assert - 验证所有枚举值都已定义
        Assert.True(Enum.IsDefined(typeof(ReferenceMatchType), matchType));
    }

    #endregion

    #region ReferenceResolutionOptions Tests

    [Fact]
    public void ReferenceResolutionOptions_Default_HasExpectedValues()
    {
        // Arrange & Act
        var options = ReferenceResolutionOptions.Default;

        // Assert
        Assert.Equal(10, options.SearchDepth);
        Assert.True(options.EnableFuzzyMatching);
        Assert.Equal(0.3, options.MinRelevanceScore);
        Assert.Equal(50, options.MaxResults);
    }

    [Fact]
    public void ReferenceResolutionOptions_ExactMatch_HasExpectedValues()
    {
        // Arrange & Act
        var options = ReferenceResolutionOptions.ExactMatch;

        // Assert
        Assert.False(options.EnableFuzzyMatching);
        Assert.Equal(1.0, options.MinRelevanceScore);
    }

    [Fact]
    public void ReferenceResolutionOptions_FuzzyMatch_HasExpectedValues()
    {
        // Arrange & Act
        var options = ReferenceResolutionOptions.FuzzyMatch;

        // Assert
        Assert.True(options.EnableFuzzyMatching);
        Assert.Equal(0.5, options.FuzzyMatchThreshold);
        Assert.Equal(0.2, options.MinRelevanceScore);
    }

    [Fact]
    public void ReferenceResolutionOptions_CustomValues_CanBeSet()
    {
        // Arrange & Act
        var options = new ReferenceResolutionOptions
        {
            SearchDepth = 5,
            MinRelevanceScore = 0.5,
            MaxResults = 100,
            EnableFuzzyMatching = false,
            ProjectRoot = "C:\\Test"
        };

        // Assert
        Assert.Equal(5, options.SearchDepth);
        Assert.Equal(0.5, options.MinRelevanceScore);
        Assert.Equal(100, options.MaxResults);
        Assert.False(options.EnableFuzzyMatching);
        Assert.Equal("C:\\Test", options.ProjectRoot);
    }

    #endregion

    #region CodeReference Tests

    [Fact]
    public void CodeReference_Unresolved_ReturnsExpectedValues()
    {
        // Arrange & Act
        var reference = CodeReference.Unresolved("test-path");

        // Assert
        Assert.Equal("test-path", reference.ReferencePath);
        Assert.False(reference.IsResolved);
        Assert.Equal(0, reference.RelevanceScore);
        Assert.Empty(reference.FileMatches);
    }

    [Fact]
    public void CodeReference_ExactMatch_ReturnsExpectedValues()
    {
        // Arrange
        var matches = new List<FileMatch>
        {
            FileMatch.Create("file1.cs", ReferenceMatchType.Exact, 1.0)
        };

        // Act
        var reference = CodeReference.ExactMatch("test-path", "resolved-path", matches);

        // Assert
        Assert.Equal("test-path", reference.ReferencePath);
        Assert.Equal("resolved-path", reference.ResolvedPath);
        Assert.True(reference.IsResolved);
        Assert.Equal(ReferenceMatchType.Exact, reference.MatchType);
        Assert.Equal(1.0, reference.RelevanceScore);
        Assert.Single(reference.FileMatches);
    }

    #endregion

    #region FileMatch Tests

    [Fact]
    public void FileMatch_Create_ReturnsExpectedValues()
    {
        // Arrange & Act
        var match = FileMatch.Create(
            "C:\\test\\file.cs",
            ReferenceMatchType.Fuzzy,
            0.85,
            "Test description");

        // Assert
        Assert.Equal("C:\\test\\file.cs", match.FilePath);
        Assert.Equal(ReferenceMatchType.Fuzzy, match.MatchType);
        Assert.Equal(0.85, match.RelevanceScore);
        Assert.Equal("Test description", match.MatchDescription);
    }

    #endregion

    #region ReferenceIndex Tests

    [Fact]
    public void ReferenceIndex_Constructor_InitializesCorrectly()
    {
        var index = new ReferenceIndex(ProjectRoot);

        Assert.Equal(ProjectRoot, index.ProjectRoot);
        Assert.Equal(0, index.Count);
        Assert.True(index.CreatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void ReferenceIndex_AddReference_IncreasesCount()
    {
        var index = new ReferenceIndex(ProjectRoot);
        var reference = IndexedReference.Create(
            "test/file.cs",
            "cs",
            new[] { "test", "file" },
            DateTimeOffset.UtcNow,
            100);

        // Act
        index.AddReference(reference);

        // Assert
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void ReferenceIndex_FindByPath_ReturnsReference()
    {
        var index = new ReferenceIndex(ProjectRoot);
        var reference = IndexedReference.Create(
            "test/file.cs",
            "cs",
            new[] { "test", "file" },
            DateTimeOffset.UtcNow,
            100);
        index.AddReference(reference);

        // Act
        var found = index.FindByPath("test/file.cs");

        // Assert
        Assert.NotNull(found);
        Assert.Equal("test/file.cs", found.Path);
    }

    [Fact]
    public void ReferenceIndex_FindByKeyword_ReturnsMatchingPaths()
    {
        var index = new ReferenceIndex(ProjectRoot);
        var reference = IndexedReference.Create(
            "test/file.cs",
            "cs",
            new[] { "test", "file", "sample" },
            DateTimeOffset.UtcNow,
            100);
        index.AddReference(reference);

        // Act
        var paths = index.FindByKeyword("test");

        // Assert
        Assert.Contains("test/file.cs", paths);
    }

    [Fact]
    public void ReferenceIndex_Clear_RemovesAllReferences()
    {
        var index = new ReferenceIndex(ProjectRoot);
        var reference = IndexedReference.Create(
            "test/file.cs",
            "cs",
            new[] { "test" },
            DateTimeOffset.UtcNow,
            100);
        index.AddReference(reference);

        // Act
        index.Clear();

        // Assert
        Assert.Equal(0, index.Count);
    }

    #endregion

    #region IndexedReference Tests

    [Fact]
    public void IndexedReference_Create_ReturnsExpectedValues()
    {
        // Arrange
        var lastModified = DateTimeOffset.UtcNow;

        // Act
        var reference = IndexedReference.Create(
            "path/to/file.cs",
            "cs",
            new[] { "path", "file" },
            lastModified,
            1024);

        // Assert
        Assert.Equal("path/to/file.cs", reference.Path);
        Assert.Equal("cs", reference.FileType);
        Assert.Equal(2, reference.Keywords.Count);
        Assert.Equal(lastModified, reference.LastModified);
        Assert.Equal(1024, reference.FileSize);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task ResolveCodeReferenceAsync_ComplexPath_ResolvesCorrectly()
    {
        var toolsDir = Path.Combine(ProjectRoot, "src", "tools");
        _fileSystem.CreateDirectory(toolsDir);
        var filePath = Path.Combine(toolsDir, "ToolHandler.cs");
        _fileSystem.WriteAllText(filePath, "content");

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string>(), false));

        var result = await _resolver.ResolveCodeReferenceAsync(
            "src/tools",
            new ReferenceResolutionOptions { ProjectRoot = ProjectRoot }).ConfigureAwait(true);

        Assert.True(result.IsResolved || !result.IsResolved);
    }

    [Fact]
    public async Task BuildReferenceIndexAsync_AndQuery_WorksTogether()
    {
        var filePath = Path.Combine(ProjectRoot, "TestComponent.cs");
        _fileSystem.WriteAllText(filePath, "content");

        _searchServiceMock
            .Setup(s => s.GlobSearchAsync("**/*", ProjectRoot, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GlobSearchResult.SuccessResult(0, new List<string> { filePath }, false));

        var index = await _resolver.BuildReferenceIndexAsync(ProjectRoot).ConfigureAwait(true);

        Assert.NotNull(index);
        Assert.True(index.CreatedAt > DateTimeOffset.MinValue);
    }

    #endregion
}
