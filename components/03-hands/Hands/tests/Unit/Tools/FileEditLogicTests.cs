namespace Core.Tests;

public class FileEditLogicTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly FileEditLogic _fileEditLogic;

    public FileEditLogicTests()
    {
        _fileEditLogic = new FileEditLogic(_fs);
    }

    [Fact]
    public async Task EditWithRegexAsync_SimplePattern_ReplacesCorrectly()
    {
        var filePath = CreateFile("Hello World, Hello Universe");

        var result = await _fileEditLogic.EditWithRegexAsync(filePath, @"Hello\s(\w+)", "Hi $1").ConfigureAwait(true);

        Assert.True(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("Hi World", content);
        Assert.Contains("Hi Universe", content);
    }

    [Fact]
    public async Task EditWithRegexAsync_ReplaceAll_ReplacesAllOccurrences()
    {
        var filePath = CreateFile("apple banana apple cherry apple");

        var result = await _fileEditLogic.EditWithRegexAsync(filePath, "apple", "orange", replaceAll: true).ConfigureAwait(true);

        Assert.True(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.DoesNotContain("apple", content);
        Assert.Equal(3, CountOccurrences(content, "orange"));
    }

    [Fact]
    public async Task EditWithRegexAsync_ReplaceFirst_OnlyFirstOccurrence()
    {
        var filePath = CreateFile("apple banana apple");

        var result = await _fileEditLogic.EditWithRegexAsync(filePath, "apple", "orange", replaceAll: false).ConfigureAwait(true);

        Assert.True(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Equal(1, CountOccurrences(content, "orange"));
        Assert.Contains("apple", content);
    }

    [Fact]
    public async Task EditWithRegexAsync_NoMatch_ReturnsFailure()
    {
        var filePath = CreateFile("Hello World");

        var result = await _fileEditLogic.EditWithRegexAsync(filePath, @"\d+", "number").ConfigureAwait(true);

        Assert.False(result.Success);
        Assert.Contains("未找到", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task EditWithRegexAsync_InvalidPattern_ReturnsFailure()
    {
        var filePath = CreateFile("Hello World");

        var result = await _fileEditLogic.EditWithRegexAsync(filePath, @"[unclosed", "replacement").ConfigureAwait(true);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task InsertLinesAfterAsync_ValidPosition_InsertsCorrectly()
    {
        var filePath = CreateFile("Line 1\nLine 2\nLine 3\n");

        var result = await _fileEditLogic.InsertLinesAfterAsync(filePath, afterLine: 1, "New Line A\nNew Line B").ConfigureAwait(true);

        Assert.True(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Line 1", lines[0]);
        Assert.Equal("Line 2", lines[1]);
        Assert.Contains("New Line A", lines[2]);
        Assert.Contains("New Line B", lines[3]);
        Assert.Contains("Line 3", lines[4]);
    }

    [Fact]
    public async Task InsertLinesAfterAsync_AfterLastLine_AppendsCorrectly()
    {
        var filePath = CreateFile("Line 1\nLine 2\nLine 3\n");

        var result = await _fileEditLogic.InsertLinesAfterAsync(filePath, afterLine: 3, "New Content").ConfigureAwait(true);

        Assert.True(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("New Content", content);
    }

    [Fact]
    public async Task InsertLinesAfterAsync_BeforeFirstLine_InsertsAtTop()
    {
        var filePath = CreateFile("Line 1\nLine 2\n");

        var result = await _fileEditLogic.InsertLinesAfterAsync(filePath, afterLine: 0, "Header").ConfigureAwait(true);

        Assert.True(result.Success);
        var lines = (await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Line 1", lines[0]);
        Assert.Contains("Header", lines[1]);
    }

    [Fact]
    public async Task InsertLinesAfterAsync_OutOfRange_ReturnsFailure()
    {
        var filePath = CreateFile("Line 1\n");

        var result = await _fileEditLogic.InsertLinesAfterAsync(filePath, afterLine: 100, "New").ConfigureAwait(true);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task DeleteLinesAsync_ValidRange_DeletesCorrectly()
    {
        var filePath = CreateFile("Line 1\nLine 2\nLine 3\nLine 4\nLine 5\n");

        var result = await _fileEditLogic.DeleteLinesAsync(filePath, startLine: 2, endLine: 3).ConfigureAwait(true);

        Assert.True(result.Success);
        var content = await _fs.ReadAllTextAsync(filePath).ConfigureAwait(true);
        Assert.Contains("Line 1", content);
        Assert.DoesNotContain("Line 2", content);
        Assert.DoesNotContain("Line 3", content);
        Assert.Contains("Line 4", content);
        Assert.Contains("Line 5", content);
    }

    [Fact]
    public async Task DeleteLinesAsync_OutOfRange_ReturnsFailure()
    {
        var filePath = CreateFile("Line 1\n");

        var result = await _fileEditLogic.DeleteLinesAsync(filePath, startLine: 100, endLine: 200).ConfigureAwait(true);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task DeleteLinesAsync_StartGreaterThanEnd_ReturnsFailure()
    {
        var filePath = CreateFile("Line 1\nLine 2\n");

        var result = await _fileEditLogic.DeleteLinesAsync(filePath, startLine: 5, endLine: 2).ConfigureAwait(true);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task BatchEditAsync_MultipleFiles_AllSucceed()
    {
        var file1 = CreateFile("Hello World");
        var file2 = CreateFile("Hello Universe");
        var paths = new[] { file1, file2 };

        var results = await _fileEditLogic.BatchEditAsync(paths, "Hello", "Hi", replaceAll: true).ConfigureAwait(true);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Result.Success));
        var content1 = await _fs.ReadAllTextAsync(file1).ConfigureAwait(true);
        var content2 = await _fs.ReadAllTextAsync(file2).ConfigureAwait(true);
        Assert.DoesNotContain("Hello", content1);
        Assert.DoesNotContain("Hello", content2);
    }

    [Fact]
    public async Task BatchEditAsync_PartialFailure_ReportsResults()
    {
        var validFile = CreateFile("Hello World");
        var invalidFile = "/test/nonexistent.txt";
        var paths = new[] { validFile, invalidFile };

        var results = await _fileEditLogic.BatchEditAsync(paths, "Hello", "Hi").ConfigureAwait(true);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Result.Success);
        Assert.False(results[1].Result.Success);
    }

    [Fact]
    public async Task BatchEditAsync_EmptyList_ReturnsEmpty()
    {
        var results = await _fileEditLogic.BatchEditAsync(Array.Empty<string>(), "old", "new").ConfigureAwait(true);

        Assert.Empty(results);
    }

    [Fact]
    public async Task EditWithRegexAsync_FileNotFound_ReturnsFailure()
    {
        var filePath = "/test/nonexistent.txt";

        var result = await _fileEditLogic.EditWithRegexAsync(filePath, "pattern", "replacement").ConfigureAwait(true);

        Assert.False(result.Success);
    }

    private string CreateFile(string content)
    {
        var filePath = $"/test/test_{Guid.NewGuid():N}.txt";
        _fs.WriteAllText(filePath, content);
        return filePath;
    }

    private static int CountOccurrences(string text, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}