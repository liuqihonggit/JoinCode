namespace Core.Tests;

public class SnipLogicTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly SnipLogic _snipLogic;

    public SnipLogicTests()
    {
        _snipLogic = new SnipLogic(_fs);
    }

    [Fact]
    public async Task SnipLinesAsync_ValidRange_ReturnsCorrectContent()
    {
        var filePath = CreateTempFile(10);

        var result = await _snipLogic.SnipLinesAsync(filePath, startLine: 3, lineCount: 4).ConfigureAwait(true);

        Assert.Contains("Line 4", result);
        Assert.Contains("Line 7", result);
        Assert.DoesNotContain("Line 2", result);
        Assert.DoesNotContain("Line 8", result);
    }

    [Fact]
    public async Task SnipLinesAsync_FromStart_ReturnsFirstLines()
    {
        var filePath = CreateTempFile(10);

        var result = await _snipLogic.SnipLinesAsync(filePath, startLine: 0, lineCount: 3).ConfigureAwait(true);

        Assert.Contains("Line 1", result);
        Assert.Contains("Line 3", result);
        Assert.DoesNotContain("Line 4", result);
    }

    [Fact]
    public async Task SnipLinesAsync_ToEnd_LimitsCorrectly()
    {
        var filePath = CreateTempFile(5);

        var result = await _snipLogic.SnipLinesAsync(filePath, startLine: 3, lineCount: 100).ConfigureAwait(true);

        Assert.Contains("Line 4", result);
        Assert.Contains("Line 5", result);
    }

    [Fact]
    public async Task SnipLinesAsync_StartLineOutOfRange_ReturnsEmpty()
    {
        var filePath = CreateTempFile(3);

        var result = await _snipLogic.SnipLinesAsync(filePath, startLine: 100, lineCount: 2).ConfigureAwait(true);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task SnipLinesAsync_EmptyFile_ReturnsEmpty()
    {
        var filePath = "/test/empty.txt";
        _fs.WriteAllText(filePath, "");

        var result = await _snipLogic.SnipLinesAsync(filePath, startLine: 0, lineCount: 5).ConfigureAwait(true);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task SnipLinesAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var filePath = "/test/nonexistent.txt";

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _snipLogic.SnipLinesAsync(filePath, startLine: 0, lineCount: 5)).ConfigureAwait(true);
    }

    [Fact]
    public async Task SnipLinesAsync_NegativeStartLine_ReturnsFromBeginning()
    {
        var filePath = CreateTempFile(5);

        var result = await _snipLogic.SnipLinesAsync(filePath, startLine: -1, lineCount: 2).ConfigureAwait(true);

        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
    }

    [Fact]
    public async Task SnipLinesAsync_ZeroLineCount_ReturnsEmpty()
    {
        var filePath = CreateTempFile(5);

        var result = await _snipLogic.SnipLinesAsync(filePath, startLine: 0, lineCount: 0).ConfigureAwait(true);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task SnipOffsetAsync_ValidRange_ReturnsCorrectContent()
    {
        var filePath = CreateTempFile(10);

        var result = await _snipLogic.SnipOffsetAsync(filePath, offset: 2, limit: 4).ConfigureAwait(true);

        Assert.Contains("Line 3", result);
        Assert.Contains("Line 6", result);
        Assert.DoesNotContain("Line 2", result);
        Assert.DoesNotContain("Line 7", result);
    }

    [Fact]
    public async Task SnipOffsetAsync_FromStart_ReturnsFirstLines()
    {
        var filePath = CreateTempFile(10);

        var result = await _snipLogic.SnipOffsetAsync(filePath, offset: 0, limit: 3).ConfigureAwait(true);

        Assert.Contains("Line 1", result);
        Assert.Contains("Line 3", result);
    }

    [Fact]
    public async Task SnipOffsetAsync_OutOfRange_ReturnsEmpty()
    {
        var filePath = CreateTempFile(3);

        var result = await _snipLogic.SnipOffsetAsync(filePath, offset: 100, limit: 2).ConfigureAwait(true);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task GetPreviewAsync_ReturnsFileInfoAndPreview()
    {
        var content = GenerateContent(20);
        var filePath = "/test/preview.txt";
        _fs.WriteAllText(filePath, content);

        var preview = await _snipLogic.GetPreviewAsync(filePath, maxPreviewLines: 5).ConfigureAwait(true);

        Assert.Equal(filePath, preview.FilePath);
        Assert.Equal(content.Length, preview.FileSize);
        Assert.InRange(preview.TotalLines, 15, 25);
        Assert.True(preview.PreviewContent.Split('\n').Length <= 6);
        Assert.Contains("Line 1", preview.PreviewContent);
    }

    [Fact]
    public async Task GetPreviewAsync_ShortFile_ReturnsAllLines()
    {
        var filePath = CreateTempFile(3);

        var preview = await _snipLogic.GetPreviewAsync(filePath, maxPreviewLines: 10).ConfigureAwait(true);

        Assert.Contains("Line 1", preview.PreviewContent);
        Assert.Contains("Line 3", preview.PreviewContent);
    }

    [Fact]
    public async Task SnipLinesAsync_LargeFile_HandlesEfficiently()
    {
        var filePath = "/test/large.txt";
        var sb = new StringBuilder();
        for (var i = 0; i < 10000; i++)
            sb.AppendLine($"Line {i + 1}: " + new string('x', 100));
        _fs.WriteAllText(filePath, sb.ToString());

        var result = await _snipLogic.SnipLinesAsync(filePath, startLine: 9990, lineCount: 5).ConfigureAwait(true);

        Assert.Contains("Line 9991", result);
        Assert.Contains("Line 9995", result);
        Assert.DoesNotContain("Line 9996", result);
    }

    [Fact]
    public async Task GetPreviewAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var filePath = "/test/nonexistent.txt";

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _snipLogic.GetPreviewAsync(filePath, maxPreviewLines: 5)).ConfigureAwait(true);
    }

    [Fact]
    public async Task SnipLinesAsync_PreservesOriginalLineNumbers()
    {
        var filePath = CreateTempFile(5);

        var result = await _snipLogic.SnipLinesAsync(filePath, startLine: 1, lineCount: 2).ConfigureAwait(true);

        Assert.Contains("Line 2", result);
        Assert.Contains("Line 3", result);
    }

    private string CreateTempFile(int lineCount)
    {
        var filePath = $"/test/test_{Guid.NewGuid():N}.txt";
        _fs.WriteAllText(filePath, GenerateContent(lineCount));
        return filePath;
    }

    private static string GenerateContent(int lineCount)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= lineCount; i++)
            sb.AppendLine($"Line {i}: Content for line number {i} in the test file.");
        return sb.ToString();
    }
}