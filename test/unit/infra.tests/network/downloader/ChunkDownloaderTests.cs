namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// ChunkDownloader 单元测试 — 验证单分片下载:Range头/续传偏移/写入.part/更新Downloaded/HTTP错误/完成标记
/// <para>用 StubHandler mock HTTP + InMemoryFileSystem 零磁盘 IO</para>
/// </summary>
public sealed class ChunkDownloaderTests
{
    private const string Url = "https://example.com/file.bin";
    private const string PartPath = "/tmp/file.part0";

    // === Range 请求头正确 ===

    [Fact]
    public async Task Download_SendsCorrectRangeHeader()
    {
        var handler = new StubHandler(_ => OkPartial(new byte[1024]));
        var downloader = CreateDownloader(handler);

        var chunk = new DownloadChunk { Index = 0, Start = 0, End = 1023 };
        await downloader.DownloadAsync(Url, chunk, PartPath);

        var range = handler.LastRequest!.Headers.Range!.Ranges.First();
        range.From.Should().Be(0);
        range.To.Should().Be(1023);
    }

    // === 续传偏移:Downloaded=500 时 Range 从 500 开始 ===

    [Fact]
    public async Task Download_WithExistingDownloaded_RangeStartsFromOffset()
    {
        var handler = new StubHandler(_ => OkPartial(new byte[524]));
        var downloader = CreateDownloader(handler);

        var chunk = new DownloadChunk { Index = 0, Start = 0, End = 1023, Downloaded = 500 };
        await downloader.DownloadAsync(Url, chunk, PartPath);

        var range = handler.LastRequest!.Headers.Range!.Ranges.First();
        range.From.Should().Be(500);
        range.To.Should().Be(1023);
    }

    // === 写入 .part 文件 ===

    [Fact]
    public async Task Download_WritesContentToPartFile()
    {
        var data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        var handler = new StubHandler(_ => OkPartial(data));
        var fs = new InMemoryFileSystem();
        var downloader = new ChunkDownloader(new HttpClient(handler), fs);

        var chunk = new DownloadChunk { Index = 0, Start = 0, End = 1023 };
        await downloader.DownloadAsync(Url, chunk, PartPath);

        fs.FileExists(PartPath).Should().BeTrue();
        var written = fs.ReadAllBytes(PartPath);
        written.Should().Equal(data);
    }

    // === 更新 chunk.Downloaded ===

    [Fact]
    public async Task Download_UpdatesChunkDownloaded()
    {
        var handler = new StubHandler(_ => OkPartial(new byte[1024]));
        var downloader = CreateDownloader(handler);

        var chunk = new DownloadChunk { Index = 0, Start = 0, End = 1023 };
        var result = await downloader.DownloadAsync(Url, chunk, PartPath);

        result.Success.Should().BeTrue();
        result.BytesDownloaded.Should().Be(1024);
        chunk.Downloaded.Should().Be(1024);
        chunk.Completed.Should().BeTrue();
    }

    // === 续传:Downloaded 累加 ===

    [Fact]
    public async Task Download_Resume_AccumulatesDownloaded()
    {
        var handler = new StubHandler(_ => OkPartial(new byte[524]));
        var downloader = CreateDownloader(handler);

        var chunk = new DownloadChunk { Index = 0, Start = 0, End = 1023, Downloaded = 500 };
        var result = await downloader.DownloadAsync(Url, chunk, PartPath);

        result.BytesDownloaded.Should().Be(524);
        chunk.Downloaded.Should().Be(1024);
        chunk.Completed.Should().BeTrue();
    }

    // === HTTP 错误 ===

    [Fact]
    public async Task Download_HttpError_ReturnsFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var downloader = CreateDownloader(handler);

        var chunk = new DownloadChunk { Index = 0, Start = 0, End = 1023 };
        var result = await downloader.DownloadAsync(Url, chunk, PartPath);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("[DOWN008]");
        result.ChunkIndex.Should().Be(0);
    }

    // === 取消令牌 ===

    [Fact]
    public async Task Download_Cancelled_ThrowsOperationCancelled()
    {
        var handler = new StubHandler(_ => OkPartial(new byte[1024]));
        var downloader = CreateDownloader(handler);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var chunk = new DownloadChunk { Index = 0, Start = 0, End = 1023 };
        var act = () => downloader.DownloadAsync(Url, chunk, PartPath, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // === 辅助 ===

    private static ChunkDownloader CreateDownloader(StubHandler handler) =>
        new(new HttpClient(handler), new InMemoryFileSystem());

    private static HttpResponseMessage OkPartial(byte[] data) =>
        new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data) };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        internal HttpRequestMessage? LastRequest { get; private set; }

        internal StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }
}
