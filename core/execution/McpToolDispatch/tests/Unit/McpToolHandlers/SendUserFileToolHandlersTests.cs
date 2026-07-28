namespace Sync.Tests.ToolHandlers;

public class SendUserFileToolHandlersTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly SendUserFileToolHandlers _handler;

    public SendUserFileToolHandlersTests()
    {
        _handler = new SendUserFileToolHandlers(_fs, NullLogger<SendUserFileToolHandlers>.Instance);
    }

    [Fact]
    public async Task SendUserFileAsync_EmptyPath_ReturnsError()
    {
        var result = await _handler.SendUserFileAsync("", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("不能为空", result.GetTextContent());
    }

    [Fact]
    public async Task SendUserFileAsync_NonexistentFile_ReturnsError()
    {
        var result = await _handler.SendUserFileAsync("nonexistent_file_12345.txt", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("文件不存在", result.GetTextContent());
    }

    [Fact]
    public async Task SendUserFileAsync_ExistingFile_ReturnsSuccess()
    {
        var tempFile = "/test/tmp_" + Guid.NewGuid().ToString("N") + ".tmp";
        await _fs.WriteAllTextAsync(tempFile, "hello world").ConfigureAwait(true);
        var result = await _handler.SendUserFileAsync(tempFile, cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("文件已发送", result.GetTextContent());
        Assert.Contains(tempFile, result.GetTextContent());
    }
}
