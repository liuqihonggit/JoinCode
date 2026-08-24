namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// DownloadSession 状态流转测试 — 验证 Pause/Resume/资源变更/大文件并发
/// <para>用 DelayedStubHandler 确保下载进行中可 Pause,零真实网络</para>
/// </summary>
public sealed class DownloadSessionStateTransitionTests
{
    private const string Url = "https://example.com/file.bin";
    private const string FilePath = "/tmp/file.bin";

    // === Pause: Downloading → Paused ===

    [Fact]
    public async Task Pause_FromDownloading_ToPaused()
    {
        var handler = CreateDelayedHandler(1024, delayMs: 500, etag: "\"etag1\"");
        var fs = new InMemoryFileSystem();
        var downloader = new RangeDownloader(new HttpClient(handler), fs);

        var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 1 });
        await Task.Delay(50);
        await session.PauseAsync();

        session.State.Should().Be(DownloadState.Paused);
    }

    // === Resume: Paused → Downloading → Completed ===

    [Fact]
    public async Task Resume_FromPaused_ToCompleted()
    {
        var data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        var handler = CreateDelayedHandlerWithData(data, delayMs: 300, etag: "\"etag1\"");
        var fs = new InMemoryFileSystem();
        var downloader = new RangeDownloader(new HttpClient(handler), fs);

        var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 1 });
        await Task.Delay(50);
        await session.PauseAsync();
        session.State.Should().Be(DownloadState.Paused);

        await session.ResumeAsync();
        var result = await session.WaitForCompletionAsync();

        result.Success.Should().BeTrue();
        result.FinalState.Should().Be(DownloadState.Completed);
        fs.ReadAllBytes(FilePath).Should().Equal(data);
    }

    // === 资源变更:ETag 不匹配 → 重新下载 ===

    [Fact]
    public async Task Resume_ResourceChanged_RetDownloadsAndCompletes()
    {
        var data = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray();
        var etag = "\"etag1\"";
        var handler = new DelayedStubHandler(async (req, ct) =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Headers = { AcceptRanges = { "bytes" }, ETag = new EntityTagHeaderValue(etag) },
                    Content = new ByteArrayContent([]) { Headers = { ContentLength = data.Length } }
                };
            await Task.Delay(200, ct);
            return RangeResponseFromData(req, data);
        });
        var fs = new InMemoryFileSystem();
        var downloader = new RangeDownloader(new HttpClient(handler), fs);

        var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 1 });
        await Task.Delay(50);
        await session.PauseAsync();

        etag = "\"etag2\"";

        await session.ResumeAsync();
        var result = await session.WaitForCompletionAsync();

        result.Success.Should().BeTrue();
        result.FinalState.Should().Be(DownloadState.Completed);
        fs.ReadAllBytes(FilePath).Should().Equal(data);
    }

    // === 大文件多线程并发 ===

    [Fact]
    public async Task LargeFile_MultiThread_ContentCorrect()
    {
        var data = Enumerable.Range(0, 256 * 1024).Select(i => (byte)(i % 256)).ToArray();
        var (downloader, fs) = CreateDownloaderWithData(data);

        var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 8 });
        var result = await session.WaitForCompletionAsync();

        result.Success.Should().BeTrue();
        result.TotalBytes.Should().Be(256 * 1024);
        fs.ReadAllBytes(FilePath).Should().Equal(data);
    }

    // === Pause 后元数据已保存 ===

    [Fact]
    public async Task Pause_PersistsMetadata()
    {
        var handler = CreateDelayedHandler(1024, delayMs: 500, etag: "\"etag1\"");
        var fs = new InMemoryFileSystem();
        var downloader = new RangeDownloader(new HttpClient(handler), fs);

        var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 1 });
        await Task.Delay(50);
        await session.PauseAsync();

        var metaPath = MetadataStore.GetMetadataPath(FilePath);
        fs.FileExists(metaPath).Should().BeTrue("Pause 后应持久化 .meta.json 供 Resume 恢复");
    }

    // === 辅助 ===

    private static (RangeDownloader downloader, InMemoryFileSystem fs) CreateDownloaderWithData(byte[] data)
    {
        var handler = new StubHandler(req =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Headers = { AcceptRanges = { "bytes" } },
                    Content = new ByteArrayContent([]) { Headers = { ContentLength = data.Length } }
                };
            return RangeResponseFromData(req, data);
        });
        var fs = new InMemoryFileSystem();
        return (new RangeDownloader(new HttpClient(handler), fs), fs);
    }

    private static DelayedStubHandler CreateDelayedHandler(int length, int delayMs, string etag)
    {
        var data = new byte[length];
        return new DelayedStubHandler(async (req, ct) =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Headers = { AcceptRanges = { "bytes" }, ETag = new EntityTagHeaderValue(etag) },
                    Content = new ByteArrayContent([]) { Headers = { ContentLength = length } }
                };
            await Task.Delay(delayMs, ct);
            return RangeResponseFromData(req, data);
        });
    }

    private static DelayedStubHandler CreateDelayedHandlerWithData(byte[] data, int delayMs, string etag)
    {
        return new DelayedStubHandler(async (req, ct) =>
        {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Headers = { AcceptRanges = { "bytes" }, ETag = new EntityTagHeaderValue(etag) },
                    Content = new ByteArrayContent([]) { Headers = { ContentLength = data.Length } }
                };
            await Task.Delay(delayMs, ct);
            return RangeResponseFromData(req, data);
        });
    }

    private static HttpResponseMessage RangeResponseFromData(HttpRequestMessage req, byte[] data)
    {
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
