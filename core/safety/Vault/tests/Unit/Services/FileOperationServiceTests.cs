
namespace Core.Tests.Services;

/// <summary>
/// FileOperationService 单元测试 - 使用内存文件系统实现高速测试
/// </summary>
public sealed class FileOperationServiceTests : IDisposable
{
    private readonly InMemoryFileOperationService _service;

    public FileOperationServiceTests()
    {
        _service = new InMemoryFileOperationService();
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public async Task ReadFileAsync_ExistingFile_ReturnsContent()
    {
        // Arrange
        var filePath = "test.txt";
        var content = "Line 1\nLine 2\nLine 3";
        await _service.WriteFileAsync(filePath, content).ConfigureAwait(true);

        // Act
        var result = await _service.ReadFileAsync(filePath).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(3, result.TotalLines);
        Assert.Equal("Line 1\nLine 2\nLine 3", result.Content);
    }

    [Fact]
    public async Task ReadFileAsync_WithOffsetAndLimit_ReturnsPartialContent()
    {
        // Arrange
        var filePath = "test.txt";
        var content = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        await _service.WriteFileAsync(filePath, content).ConfigureAwait(true);

        // Act
        var result = await _service.ReadFileAsync(filePath, offset: 1, limit: 2).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.NumLines);
        Assert.Equal(2, result.StartLine);
        Assert.Equal("Line 2\nLine 3", result.Content);
    }

    [Fact]
    public async Task ReadFileAsync_NonExistingFile_ReturnsFailure()
    {
        // Arrange
        var filePath = "nonexistent.txt";

        // Act
        var result = await _service.ReadFileAsync(filePath).ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task WriteFileAsync_NewFile_CreatesFile()
    {
        // Arrange
        var filePath = "newfile.txt";
        var content = "Hello, World!";

        // Act
        var result = await _service.WriteFileAsync(filePath, content).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("create", result.Operation);
        Assert.True(_service.FileExists(filePath));
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);
        Assert.Equal(content, readResult.Content);
    }

    [Fact]
    public async Task WriteFileAsync_ExistingFile_UpdatesFile()
    {
        // Arrange
        var filePath = "existing.txt";
        await _service.WriteFileAsync(filePath, "Original content").ConfigureAwait(true);
        var newContent = "Updated content";

        // Act
        var result = await _service.WriteFileAsync(filePath, newContent).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("update", result.Operation);
        Assert.Equal("Original content", result.OriginalContent);
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);
        Assert.Equal(newContent, readResult.Content);
    }

    [Fact]
    public async Task EditFileAsync_SingleReplace_ReplacesFirstOccurrence()
    {
        // Arrange
        var filePath = "edit.txt";
        await _service.WriteFileAsync(filePath, "alpha beta alpha").ConfigureAwait(true);

        // Act
        var result = await _service.EditFileAsync(filePath, "alpha", "omega", replaceAll: false).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.ReplaceCount);
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);
        Assert.Equal("omega beta alpha", readResult.Content);
    }

    [Fact]
    public async Task EditFileAsync_ReplaceAll_ReplacesAllOccurrences()
    {
        // Arrange
        var filePath = "edit.txt";
        await _service.WriteFileAsync(filePath, "alpha beta alpha").ConfigureAwait(true);

        // Act
        var result = await _service.EditFileAsync(filePath, "alpha", "omega", replaceAll: true).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.ReplaceCount);
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);
        Assert.Equal("omega beta omega", readResult.Content);
    }

    [Fact]
    public async Task EditFileAsync_StringNotFound_ReturnsFailure()
    {
        // Arrange
        var filePath = "edit.txt";
        await _service.WriteFileAsync(filePath, "some content").ConfigureAwait(true);

        // Act
        var result = await _service.EditFileAsync(filePath, "nonexistent", "replacement").ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task DeleteFileAsync_ExistingFile_DeletesFile()
    {
        // Arrange
        var filePath = "todelete.txt";
        await _service.WriteFileAsync(filePath, "content").ConfigureAwait(true);

        // Act
        var result = await _service.DeleteFileAsync(filePath).ConfigureAwait(true);

        // Assert
        Assert.True(result);
        Assert.False(_service.FileExists(filePath));
    }

    [Fact]
    public async Task DeleteFileAsync_NonExistingFile_ReturnsFalse()
    {
        // Arrange
        var filePath = "nonexistent.txt";

        // Act
        var result = await _service.DeleteFileAsync(filePath).ConfigureAwait(true);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ListDirectoryAsync_ReturnsFilesAndDirectories()
    {
        // Arrange - 使用具体路径而不是 "."
        var baseDir = "testdir";
        _service.CreateDirectory(baseDir);
        var subDir = Path.Combine(baseDir, "subdir");
        _service.CreateDirectory(subDir);
        var filePath = Path.Combine(baseDir, "file.txt");
        await _service.WriteFileAsync(filePath, "content").ConfigureAwait(true);

        // Act
        var result = await _service.ListDirectoryAsync(baseDir).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Directories);
        Assert.Equal("subdir", result.Directories[0].Name);
        Assert.Single(result.Files);
        Assert.Equal("file.txt", result.Files[0].Name);
    }

    [Fact]
    public async Task ListDirectoryAsync_Recursive_ReturnsNestedContent()
    {
        // Arrange - 使用具体路径而不是 "."
        var baseDir = "testdir2";
        _service.CreateDirectory(baseDir);
        var subDir = Path.Combine(baseDir, "subdir");
        _service.CreateDirectory(subDir);
        var nestedFile = Path.Combine(subDir, "nested.txt");
        await _service.WriteFileAsync(nestedFile, "content").ConfigureAwait(true);

        // Act
        var result = await _service.ListDirectoryAsync(baseDir, recursive: true).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Directories);
        Assert.Single(result.Files);
    }

    [Fact]
    public async Task ConcurrentReadOperations_Succeed()
    {
        // Arrange
        var filePath = "concurrent.txt";
        await _service.WriteFileAsync(filePath, "test content").ConfigureAwait(true);

        // Act
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _service.ReadFileAsync(filePath))
            .ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(true);

        // Assert
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task ReadWriteConcurrent_SequentialAccess()
    {
        // Arrange
        var filePath = "concurrent.txt";

        // Act - Write then read
        var writeResult = await _service.WriteFileAsync(filePath, "content").ConfigureAwait(true);
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);

        // Assert
        Assert.True(writeResult.Success);
        Assert.True(readResult.Success);
        Assert.Equal("content", readResult.Content);
    }

    [Fact]
    public async Task EditByLineRangeAsync_ReplaceMiddleLines_ReplacesCorrectly()
    {
        // Arrange
        var filePath = "editlines.txt";
        var originalContent = "Line 1\nLine 2\nLine 3\nLine 4\nLine 5";
        await _service.WriteFileAsync(filePath, originalContent).ConfigureAwait(true);
        var request = new LineRangeEditRequest(filePath, startLine: 2, endLine: 3, newContent: "New Line 2\nNew Line 3");

        // Act - Replace lines 2-3
        var result = await _service.EditByLineRangeAsync(request).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.StartLine);
        Assert.Equal(3, result.EndLine);
        Assert.Equal(2, result.ReplacedLinesCount);
        Assert.Equal("Line 2\nLine 3", result.OriginalContent);
        Assert.Equal("New Line 2\nNew Line 3", result.NewContent);
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);
        Assert.Equal("Line 1\nNew Line 2\nNew Line 3\nLine 4\nLine 5", readResult.Content);
    }

    [Fact]
    public async Task EditByLineRangeAsync_ReplaceFirstLine_ReplacesCorrectly()
    {
        // Arrange
        var filePath = "editlines.txt";
        var originalContent = "Line 1\nLine 2\nLine 3";
        await _service.WriteFileAsync(filePath, originalContent).ConfigureAwait(true);
        var request = new LineRangeEditRequest(filePath, startLine: 1, endLine: 1, newContent: "New First Line");

        // Act - Replace line 1
        var result = await _service.EditByLineRangeAsync(request).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.StartLine);
        Assert.Equal(1, result.EndLine);
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);
        Assert.Equal("New First Line\nLine 2\nLine 3", readResult.Content);
    }

    [Fact]
    public async Task EditByLineRangeAsync_ReplaceLastLine_ReplacesCorrectly()
    {
        // Arrange
        var filePath = "editlines.txt";
        var originalContent = "Line 1\nLine 2\nLine 3";
        await _service.WriteFileAsync(filePath, originalContent).ConfigureAwait(true);
        var request = new LineRangeEditRequest(filePath, startLine: 3, endLine: 3, newContent: "New Last Line");

        // Act - Replace line 3
        var result = await _service.EditByLineRangeAsync(request).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(3, result.StartLine);
        Assert.Equal(3, result.EndLine);
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);
        Assert.Equal("Line 1\nLine 2\nNew Last Line", readResult.Content);
    }

    [Fact]
    public async Task EditByLineRangeAsync_ReplaceAllLines_ReplacesCorrectly()
    {
        // Arrange
        var filePath = "editlines.txt";
        var originalContent = "Line 1\nLine 2\nLine 3";
        await _service.WriteFileAsync(filePath, originalContent).ConfigureAwait(true);
        var request = new LineRangeEditRequest(filePath, startLine: 1, endLine: 3, newContent: "All New Content");

        // Act - Replace all lines
        var result = await _service.EditByLineRangeAsync(request).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.StartLine);
        Assert.Equal(3, result.EndLine);
        Assert.Equal(3, result.ReplacedLinesCount);
        Assert.Equal("All New Content", result.NewContent);
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);
        Assert.Equal("All New Content", readResult.Content);
    }

    [Fact]
    public async Task EditByLineRangeAsync_EndLineBeyondTotal_AdjustsAutomatically()
    {
        // Arrange
        var filePath = "editlines.txt";
        var originalContent = "Line 1\nLine 2\nLine 3";
        await _service.WriteFileAsync(filePath, originalContent).ConfigureAwait(true);
        var request = new LineRangeEditRequest(filePath, startLine: 2, endLine: 100, newContent: "New Lines");

        // Act - End line beyond total lines
        var result = await _service.EditByLineRangeAsync(request).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.StartLine);
        Assert.Equal(3, result.EndLine); // Adjusted to actual last line
        Assert.Equal(2, result.ReplacedLinesCount);
        var readResult = await _service.ReadFileAsync(filePath).ConfigureAwait(true);
        Assert.Equal("Line 1\nNew Lines", readResult.Content);
    }

    [Fact]
    public async Task EditByLineRangeAsync_NonExistingFile_ReturnsFailure()
    {
        // Arrange
        var filePath = "nonexistent.txt";
        var request = new LineRangeEditRequest(filePath, startLine: 1, endLine: 5, newContent: "New Content");

        // Act
        var result = await _service.EditByLineRangeAsync(request).ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task EditByLineRangeAsync_StartLineLessThanOne_ReturnsFailure()
    {
        // Arrange
        var filePath = "editlines.txt";
        await _service.WriteFileAsync(filePath, "Line 1\nLine 2").ConfigureAwait(true);
        var request = new LineRangeEditRequest(filePath, startLine: 0, endLine: 1, newContent: "New Content");

        // Act
        var result = await _service.EditByLineRangeAsync(request).ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("起始行号必须大于等于1", result.ErrorMessage);
    }

    [Fact]
    public async Task EditByLineRangeAsync_EndLineLessThanStartLine_ReturnsFailure()
    {
        // Arrange
        var filePath = "editlines.txt";
        await _service.WriteFileAsync(filePath, "Line 1\nLine 2").ConfigureAwait(true);
        var request = new LineRangeEditRequest(filePath, startLine: 3, endLine: 1, newContent: "New Content");

        // Act
        var result = await _service.EditByLineRangeAsync(request).ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("结束行号不能小于起始行号", result.ErrorMessage);
    }

    [Fact]
    public async Task EditByLineRangeAsync_StartLineBeyondTotal_ReturnsFailure()
    {
        // Arrange
        var filePath = "editlines.txt";
        await _service.WriteFileAsync(filePath, "Line 1\nLine 2").ConfigureAwait(true);
        var request = new LineRangeEditRequest(filePath, startLine: 10, endLine: 15, newContent: "New Content");

        // Act
        var result = await _service.EditByLineRangeAsync(request).ConfigureAwait(true);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("超出文件总行数", result.ErrorMessage);
    }

    [Fact]
    public async Task EditByLineRangeAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.EditByLineRangeAsync(null!)).ConfigureAwait(true);
    }
}
