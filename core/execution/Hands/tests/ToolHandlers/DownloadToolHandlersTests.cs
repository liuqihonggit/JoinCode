namespace Hands.Tests.ToolHandlers;

/// <summary>
/// DownloadToolHandlers 单元测试 — 验证 download_file 工具的参数验证、成功、失败、取消
/// <para>用 Moq mock IDownloader + IDownloadSession,零真实网络</para>
/// </summary>
public class DownloadToolHandlersTests
{
    private const string Url = "https://example.com/file.bin";
    private const string FilePath = "/tmp/file.bin";

    // === 参数验证 ===

    [Fact]
    public async Task DownloadFile_EmptyUrl_ReturnsError()
    {
        var handler = new DownloadToolHandlers(Mock.Of<IDownloader>());
        var result = await handler.DownloadFileAsync("", FilePath);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadFile_EmptyFilePath_ReturnsError()
    {
        var handler = new DownloadToolHandlers(Mock.Of<IDownloader>());
        var result = await handler.DownloadFileAsync(Url, "");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadFile_MaxThreadsZero_ReturnsError()
    {
        var handler = new DownloadToolHandlers(Mock.Of<IDownloader>());
        var result = await handler.DownloadFileAsync(Url, FilePath, max_threads: 0);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadFile_MaxThreadsOverLimit_ReturnsError()
    {
        var handler = new DownloadToolHandlers(Mock.Of<IDownloader>());
        var result = await handler.DownloadFileAsync(Url, FilePath, max_threads: 33);
        result.IsError.Should().BeTrue();
    }

    // === 下载成功 ===

    [Fact]
    public async Task DownloadFile_Success_ReturnsSuccessResult()
    {
        var (handler, _) = CreateHandlerWithMockDownloader(
            new DownloadResult(true, FilePath, 1024, 1024, TimeSpan.FromSeconds(1), DownloadState.Completed, null));

        var result = await handler.DownloadFileAsync(Url, FilePath);

        result.IsError.Should().BeFalse();
        result.Content.Should().NotBeEmpty();
    }

    // === 下载失败 ===

    [Fact]
    public async Task DownloadFile_Failure_ReturnsErrorResult()
    {
        var (handler, _) = CreateHandlerWithMockDownloader(
            new DownloadResult(false, FilePath, 1024, 512, TimeSpan.FromSeconds(1), DownloadState.Failed, "HTTP 404"));

        var result = await handler.DownloadFileAsync(Url, FilePath);

        result.IsError.Should().BeTrue();
    }

    // === 下载取消 ===

    [Fact]
    public async Task DownloadFile_Cancelled_ReturnsErrorResult()
    {
        var (handler, _) = CreateHandlerWithMockDownloader(
            new DownloadResult(false, FilePath, 1024, 256, TimeSpan.FromSeconds(1), DownloadState.Cancelled, "已取消"));

        var result = await handler.DownloadFileAsync(Url, FilePath);

        result.IsError.Should().BeTrue();
    }

    // === 默认参数 ===

    [Fact]
    public async Task DownloadFile_DefaultOptions_CallsStartDownloadWithDefaults()
    {
        var (handler, mockDownloader) = CreateHandlerWithMockDownloader(
            new DownloadResult(true, FilePath, 100, 100, TimeSpan.Zero, DownloadState.Completed, null));

        await handler.DownloadFileAsync(Url, FilePath);

        mockDownloader.Verify(
            d => d.StartDownload(Url, FilePath, It.IsAny<DownloadOptions>(), It.IsAny<IProgress<DownloadProgress>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // === 辅助 ===

    private static (DownloadToolHandlers handler, Mock<IDownloader> mockDownloader) CreateHandlerWithMockDownloader(DownloadResult downloadResult)
    {
        var mockSession = new Mock<IDownloadSession>();
        mockSession.Setup(s => s.WaitForCompletionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(downloadResult);

        var mockDownloader = new Mock<IDownloader>();
        mockDownloader.Setup(d => d.StartDownload(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DownloadOptions>(),
                It.IsAny<IProgress<DownloadProgress>>(), It.IsAny<CancellationToken>()))
            .Returns(mockSession.Object);

        return (new DownloadToolHandlers(mockDownloader.Object), mockDownloader);
    }
}
