namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// RangeDownloader + DownloadSession 集成单元测试 — 验证完整下载/多线程/Cancel/WaitForCompletion/状态流转
/// <para>用 StubHandler mock HTTP + InMemoryFileSystem 零磁盘 IO</para>
/// </summary>
public sealed class RangeDownloaderTests
{
    private const string Url = "https://example.com/file.bin";
    private const string FilePath = "/tmp/file.bin";

    // === 单线程完整下载 ===

    [Fact]
    public async Task StartDownload_SingleThread_CompletesAndWritesFile()
    {
        var data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        var (downloader, fs) = CreateDownloader(data);

        var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 1 });
        var result = await session.WaitForCompletionAsync();

        result.Success.Should().BeTrue();
        result.FinalState.Should().Be(DownloadState.Completed);
        result.TotalBytes.Should().Be(1024);
        fs.FileExists(FilePath).Should().BeTrue();
        fs.ReadAllBytes(FilePath).Should().Equal(data);
    }

    // === 多线程 PLINQ 并发下载 ===

    [Fact]
    public async Task StartDownload_MultiThread_CompletesAndWritesFile()
    {
        var data = Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();
        var (downloader, fs) = CreateDownloader(data);

        var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 4 });
        var result = await session.WaitForCompletionAsync();

        result.Success.Should().BeTrue();
        result.FinalState.Should().Be(DownloadState.Completed);
        fs.FileExists(FilePath).Should().BeTrue();
        fs.ReadAllBytes(FilePath).Should().Equal(data);
    }

    // === Cancel(用延迟 handler 确保下载进行中) ===

    [Fact]
    public async Task Cancel_TransitionsToCancelled()
    {
        var data = new byte[1024];
        var handler = new DelayedStubHandler(async (req, ct) =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Headers = { AcceptRanges = { "bytes" } },
                    Content = new ByteArrayContent([]) { Headers = { ContentLength = data.Length } }
                };
            await Task.Delay(500, ct);
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(data)
            };
        });
        var fs = new InMemoryFileSystem();
        var downloader = new RangeDownloader(new TestHttpClientProvider(new HttpClient(handler)), fs);

        var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 1 });
        await Task.Delay(50);
        await session.CancelAsync();

        session.State.Should().Be(DownloadState.Cancelled);
    }

    // === WaitForCompletion 返回结果 ===

    [Fact]
    public async Task WaitForCompletion_ReturnsSuccessResult()
    {
        var data = new byte[512];
        var (downloader, _) = CreateDownloader(data);

        var session = downloader.StartDownload(Url, FilePath);
        var result = await session.WaitForCompletionAsync();

        result.Should().NotBeNull();
        result.FilePath.Should().Be(FilePath);
    }

    // === 临时文件清理 ===

    [Fact]
    public async Task Complete_DeletesTempFiles()
    {
        var data = new byte[2048];
        var (downloader, fs) = CreateDownloader(data);

        var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 2 });
        await session.WaitForCompletionAsync();

        fs.FileExists($"{FilePath}.part0").Should().BeFalse();
        fs.FileExists($"{FilePath}.part1").Should().BeFalse();
        fs.FileExists(MetadataStore.GetMetadataPath(FilePath)).Should().BeFalse();
    }

    // === 辅助 ===

    private static (RangeDownloader downloader, InMemoryFileSystem fs) CreateDownloader(byte[] data)
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Headers = { AcceptRanges = { "bytes" } },
                    Content = new ByteArrayContent([]) { Headers = { ContentLength = data.Length } }
                };

            var range = req.Headers.Range!.Ranges.First();
            var start = (int)(range.From ?? 0);
            var end = (int)(range.To ?? data.Length - 1);
            var length = end - start + 1;
            var chunk = new byte[length];
            Array.Copy(data, start, chunk, 0, length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(chunk)
            };
        });
        var fs = new InMemoryFileSystem();
        var downloader = new RangeDownloader(new TestHttpClientProvider(new HttpClient(handler)), fs);
        return (downloader, fs);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        internal StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed class DelayedStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;
        internal DelayedStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
