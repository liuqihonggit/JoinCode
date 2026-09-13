namespace Hands.Tests.Network;

public sealed class FileTransferServiceTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private readonly FileTransferService _service;

    public FileTransferServiceTests()
    {
        _service = new FileTransferService(_fs);
    }

    [Fact]
    public async Task SendFileAsync_FileDoesNotExist_ThrowsFileNotFoundException()
    {
        var act = async () => await _service.SendFileAsync("/missing/file.txt").ConfigureAwait(true);

        await act.Should().ThrowAsync<FileNotFoundException>().Where(ex => ex.Message.Contains("[HND005]")).ConfigureAwait(true);
    }

    [Fact]
    public async Task SendFileAsync_WithDescription_IncludesDescription()
    {
        var path = "/test/file.txt";
        _fs.WriteAllText(path, "hello");

        var result = await _service.SendFileAsync(path, "important file").ConfigureAwait(true);

        result.Should().Contain("文件已发送: file.txt");
        result.Should().Contain("说明: important file");
        result.Should().Contain("大小: 5 bytes");
    }

    [Fact]
    public async Task SendFileAsync_WithoutDescription_OmitsDescription()
    {
        var path = "/test/file.txt";
        _fs.WriteAllText(path, "hello");

        var result = await _service.SendFileAsync(path).ConfigureAwait(true);

        result.Should().Contain("路径: /test/file.txt");
        result.Should().NotContain("说明:");
    }

    [Fact]
    public async Task GenerateDownloadLinkAsync_FileDoesNotExist_ThrowsFileNotFoundException()
    {
        var act = async () => await _service.GenerateDownloadLinkAsync("/missing/file.txt").ConfigureAwait(true);

        await act.Should().ThrowAsync<FileNotFoundException>().Where(ex => ex.Message.Contains("[HND006]")).ConfigureAwait(true);
    }

    [Fact]
    public async Task GenerateDownloadLinkAsync_FileExists_GeneratesLocalLink()
    {
        var path = "/test/file.txt";
        _fs.WriteAllText(path, "hello");

        var result = await _service.GenerateDownloadLinkAsync(path).ConfigureAwait(true);

        result.Should().Contain("下载链接已生成:");
        result.Should().Contain("http://localhost:");
        result.Should().Contain("/download/file.txt");
        result.Should().Contain("文件: file.txt (5 bytes)");
    }
}
