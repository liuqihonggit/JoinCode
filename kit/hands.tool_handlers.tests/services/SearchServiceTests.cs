
namespace Core.Tests.Services;

/// <summary>
/// SearchService 单元测试 - 使用内存文件系统实现高速测试
/// </summary>
public sealed class SearchServiceTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOperationService;
    private readonly SearchService _service;
    private readonly string _testDir;

    public SearchServiceTests()
    {
        _fileOperationService = new InMemoryFileOperationService();
        _service = new SearchService(_fileOperationService, _fileOperationService.FileSystem);
        // 使用绝对路径
        _testDir = "/test/search";
        _fileOperationService.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        _fileOperationService.Dispose();
    }

    [Fact]
    public async Task GlobSearchAsync_ExistingFiles_ReturnsMatches()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file1.cs", "content");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file2.cs", "content");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file.txt", "content");

        // Act
        var result = await _service.GlobSearchAsync("*.cs", _testDir).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.NumFiles);
        Assert.All(result.Filenames, f => Assert.EndsWith(".cs", f));
    }

    [Fact]
    public async Task GlobSearchAsync_RecursivePattern_ReturnsNestedFiles()
    {
        // Arrange
        _fileOperationService.CreateDirectory($"{_testDir}/subdir");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/subdir/nested.cs", "content");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/root.cs", "content");

        // Act
        var result = await _service.GlobSearchAsync("**/*.cs", _testDir).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.NumFiles);
    }

    [Fact]
    public async Task GlobSearchAsync_NoMatches_ReturnsEmpty()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file.txt", "content");

        // Act
        var result = await _service.GlobSearchAsync("*.cs", _testDir).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.NumFiles);
        Assert.Empty(result.Filenames);
    }

    [Fact]
    public async Task GrepSearchAsync_FilesWithMatches_ReturnsMatchingFiles()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file1.cs", "class TestClass {}");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file2.cs", "class AnotherClass {}");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file.txt", "not a class");

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "class.*Class",
            Path = _testDir,
            OutputMode = SearchOutputMode.Files
        }).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.NumFiles);
    }

    [Fact]
    public async Task GrepSearchAsync_ContentMode_ReturnsMatchingLines()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/test.cs", "line 1\nclass Test {}\nline 3");

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "class",
            Path = _testDir,
            OutputMode = SearchOutputMode.Content,
            LineNumbers = true
        }).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Content);
        Assert.Contains("class", result.Content);
    }

    [Fact]
    public async Task GrepSearchAsync_CountMode_ReturnsMatchCount()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/test.cs", "class A {}\nclass B {}");

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "class",
            Path = _testDir,
            OutputMode = SearchOutputMode.Count
        }).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.NumMatches);
    }

    [Fact]
    public async Task GrepSearchAsync_WithGlobFilter_FiltersByPattern()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file.cs", "class Test {}");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file.txt", "class Another {}");

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "class",
            Path = _testDir,
            Glob = "*.cs",
            OutputMode = SearchOutputMode.Files
        }).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Filenames);
        Assert.EndsWith(".cs", result.Filenames[0]);
    }

    [Fact]
    public async Task GrepSearchAsync_CaseInsensitive_ReturnsMatches()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/test.cs", "CLASS Test {}");

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "class",
            Path = _testDir,
            CaseInsensitive = true,
            OutputMode = SearchOutputMode.Files
        }).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Filenames);
    }

    [Fact]
    public async Task GrepSearchAsync_WithContext_ReturnsSurroundingLines()
    {
        // Arrange
        var content = "line 1\nline 2\ntarget line\nline 4\nline 5";
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/test.cs", content);

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "target",
            Path = _testDir,
            OutputMode = SearchOutputMode.Content,
            Context = 1
        }).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Content);
        Assert.Contains("line 2", result.Content);
        Assert.Contains("target line", result.Content);
        Assert.Contains("line 4", result.Content);
    }

    [Fact]
    public async Task GrepSearchAsync_InvalidRegex_ReturnsFailure()
    {
        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "[invalid",
            Path = _testDir,
            OutputMode = SearchOutputMode.Files
        }).ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task GrepSearchAsync_WithHeadLimit_LimitsResults()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file{i}.cs", "class Test {}");
        }

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "class",
            Path = _testDir,
            OutputMode = SearchOutputMode.Files,
            HeadLimit = 5
        }).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(5, result.NumFiles);
        Assert.True(result.AppliedLimit.HasValue);
    }

    #region 内存文件系统兼容性回归测试

    /// <summary>
    /// 回归测试：SearchService 必须通过 IFileOperationService 枚举文件，
    /// 而非直接使用 DirectoryInfo（真实文件系统）。
    /// Matcher.Execute 只支持真实文件系统，必须改用 EnumerateFiles + Matcher.Match。
    /// 历史bug：Matcher.Execute(new DirectoryInfoWrapper(...)) 在内存文件系统下返回空结果。
    /// </summary>
    [Fact]
    public async Task GlobSearchAsync_InMemoryFileSystem_ReturnsMatches()
    {
        // Arrange — 使用 InMemoryFileOperationService（非真实文件系统）
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/mem1.cs", "content");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/mem2.cs", "content");

        // Act
        var result = await _service.GlobSearchAsync("*.cs", _testDir).ConfigureAwait(true);

        // Assert — 必须能从内存文件系统中找到文件
        Assert.True(result.Success, $"Expected success but got error: {result.ErrorMessage}");
        Assert.Equal(2, result.NumFiles);
    }

    /// <summary>
    /// 回归测试：SearchService 不能使用 Path.GetFullPath 转换内存文件系统路径。
    /// Path.GetFullPath("test\search\file.cs") 会转为 Windows 绝对路径 "d:\...\test\search\file.cs"，
    /// 导致后续 File.Exists 检查失败。
    /// 历史bug：Path.GetFullPath 把内存路径转为 Windows 绝对路径，GetFileLastWriteTime 找不到文件。
    /// </summary>
    [Fact]
    public async Task GlobSearchAsync_InMemoryFileSystem_PathsNotConvertedToAbsolute()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/path_test.cs", "content");

        // Act
        var result = await _service.GlobSearchAsync("*.cs", _testDir).ConfigureAwait(true);

        // Assert — 路径不应被转为 Windows 绝对路径（如 d:\...）
        Assert.True(result.Success, $"Expected success but got error: {result.ErrorMessage}");
        Assert.Single(result.Filenames);
        // 内存文件系统路径不应包含 Windows 盘符
        Assert.All(result.Filenames, f => Assert.False(f.Contains(":\\"), $"Path should not be Windows absolute: {f}"));
    }

    /// <summary>
    /// 回归测试：GrepSearch 在内存文件系统下必须正常工作。
    /// CollectSearchFiles 必须使用 IFileOperationService.EnumerateFiles + Matcher.Match，
    /// 而非 Matcher.Execute(DirectoryInfoWrapper)。
    /// </summary>
    [Fact]
    public async Task GrepSearchAsync_InMemoryFileSystem_FindsMatches()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/grep_test.cs", "class GrepTestClass {}");

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "GrepTestClass",
            Path = _testDir,
            OutputMode = SearchOutputMode.Files
        }).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success, $"Expected success but got error: {result.ErrorMessage}");
        Assert.Single(result.Filenames);
    }

    /// <summary>
    /// 回归测试：GrepSearch + Glob 过滤在内存文件系统下的 AND 逻辑。
    /// Matcher 的 AddInclude 是 OR 逻辑，但 ripgrep 的 --glob + --type 是 AND 逻辑。
    /// 必须通过后过滤实现 AND 语义。
    /// </summary>
    [Fact]
    public async Task GrepSearchAsync_InMemoryFileSystem_GlobAndFileTypeAndLogic()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/and_test.cs", "class AndTestClass {}");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/and_test.txt", "class AndTestText {}");

        // Act — glob 过滤只匹配 .cs 文件
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "AndTest",
            Path = _testDir,
            Glob = "*.cs",
            OutputMode = SearchOutputMode.Files
        }).ConfigureAwait(true);

        // Assert — 只有 .cs 文件应被匹配
        Assert.True(result.Success, $"Expected success but got error: {result.ErrorMessage}");
        Assert.Single(result.Filenames);
        Assert.EndsWith(".cs", result.Filenames[0]);
    }

    #endregion

    #region 超时保护测试（对齐 TS ripgrep 20s 超时）

    /// <summary>
    /// 验证 GrepSearch 在 CancellationToken 已取消时正确抛出 OperationCanceledException
    /// 对齐 TS RipgrepTimeoutError：搜索超时后应取消而非挂死
    /// </summary>
    [Fact]
    public async Task GrepSearchAsync_CancellationTokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        // Arrange — 使用已取消的 token 模拟超时
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        // Act & Assert — 已取消的 token 应立即抛出 OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.GrepSearchAsync(new()
            {
                Pattern = "test",
                Path = _testDir,
                OutputMode = SearchOutputMode.Files
            }, cts.Token)).ConfigureAwait(true);
    }

    /// <summary>
    /// 验证 GlobSearch 在 CancellationToken 已取消时正确抛出 OperationCanceledException
    /// </summary>
    [Fact]
    public async Task GlobSearchAsync_CancellationTokenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        // Arrange — 使用已取消的 token 模拟超时
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        // Act & Assert — 已取消的 token 应立即抛出 OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.GlobSearchAsync("**/*.cs", _testDir, cts.Token)).ConfigureAwait(true);
    }

    /// <summary>
    /// 验证正常搜索在合理时间内完成，CancellationToken 不影响结果
    /// </summary>
    [Fact]
    public async Task GrepSearchAsync_WithValidCancellationToken_ReturnsResults()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/cancel_test.cs", "class CancelTestClass {}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "CancelTestClass",
            Path = _testDir,
            OutputMode = SearchOutputMode.Files
        }, cts.Token).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Filenames);
    }

    /// <summary>
    /// 验证 GrepSearch 的 Read deny 排除模式过滤 — 对齐 TS getFileReadIgnorePatterns
    /// deny 模式匹配的文件不应出现在搜索结果中
    /// </summary>
    [Fact]
    public async Task GrepSearchAsync_WithDenyPatterns_ExcludesDeniedFiles()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/secret.env", "API_KEY=xxx");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/normal.cs", "class NormalClass {}");

        var denyPatterns = new List<string> { "secret.env" };

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "API_KEY|NormalClass",
            Path = _testDir,
            OutputMode = SearchOutputMode.Files,
            DenyPatterns = denyPatterns
        }).ConfigureAwait(true);

        // Assert — secret.env 应被排除
        Assert.True(result.Success);
        Assert.Single(result.Filenames);
        Assert.DoesNotContain("secret.env", result.Filenames[0]);
    }

    /// <summary>
    /// 验证 GrepSearch 的 deny 模式为空时不影响搜索结果
    /// </summary>
    [Fact]
    public async Task GrepSearchAsync_WithEmptyDenyPatterns_ReturnsAllMatches()
    {
        // Arrange
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file1.cs", "class Test1 {}");
        _fileOperationService.FileSystem.WriteAllText($"{_testDir}/file2.cs", "class Test2 {}");

        // Act
        var result = await _service.GrepSearchAsync(new()
        {
            Pattern = "class Test",
            Path = _testDir,
            OutputMode = SearchOutputMode.Files,
            DenyPatterns = []
        }).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.NumFiles);
    }

    #endregion
}
