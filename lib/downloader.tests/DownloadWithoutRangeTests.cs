namespace Downloader.Tests;

/// <summary>
/// DownloadSession 单线程回退测试 — 验证服务器不支持 Range 或未知 ContentLength 时走单线程整体下载
/// </summary>
public sealed class DownloadWithoutRangeTests {
    private const string Url = "https://example.com/file.bin";
    private const string FilePath = "/tmp/file.bin";

    [Fact]
    public async Task NoAcceptRangesHeader_FallsBackToSingleThreadDownload() {
        var data = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        var (downloader, fs) = CreateNoRangeDownloader(data);

        await using var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 4 });
        var result = await session.WaitForCompletionAsync();

        result.Success.Should().BeTrue();
        result.FinalState.Should().Be(DownloadState.Completed);
        fs.FileExists(FilePath).Should().BeTrue();
        (await fs.ReadAllBytes(FilePath)).Should().Equal(data);
    }

    [Fact]
    public async Task UnknownContentLength_FallsBackToSingleThreadDownload() {
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x42 };
        var handler = new StubHandler(req => {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK) {
                    RequestMessage = req,
                    Content = new ByteArrayContent([]),
                };
            return new HttpResponseMessage(HttpStatusCode.OK) {
                RequestMessage = req,
                Content = new ByteArrayContent(data),
            };
        });
        await using var fs = new InMemoryFileSystem();
        await using var downloader = new RangeDownloader(new TestHttpClientProvider(new HttpClient(handler)), fs);

        await using var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 4 });
        var result = await session.WaitForCompletionAsync();

        result.Success.Should().BeTrue();
        result.FinalState.Should().Be(DownloadState.Completed);
        (await fs.ReadAllBytes(FilePath)).Should().Equal(data);
    }

    [Fact]
    public async Task SingleThreadDownload_WithHeaders_Succeeds() {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        string? receivedAuth = null;
        var handler = new StubHandler(req => {
            receivedAuth = req.Headers.TryGetValues("Authorization", out var vals) ? string.Join(",", vals) : null;
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK) {
                    RequestMessage = req,
                    Content = new ByteArrayContent([]) { Headers = { ContentLength = data.Length } },
                };
            return new HttpResponseMessage(HttpStatusCode.OK) {
                RequestMessage = req,
                Content = new ByteArrayContent(data),
            };
        });
        await using var fs = new InMemoryFileSystem();
        await using var downloader = new RangeDownloader(new TestHttpClientProvider(new HttpClient(handler)), fs);

        var options = new DownloadOptions {
            MaxThreads = 4,
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer test-token" },
        };
        await using var session = downloader.StartDownload(Url, FilePath, options);
        var result = await session.WaitForCompletionAsync();

        result.Success.Should().BeTrue();
        receivedAuth.Should().Be("Bearer test-token");
        (await fs.ReadAllBytes(FilePath)).Should().Equal(data);
    }

    [Fact]
    public async Task SingleThreadDownload_ReportsProgress() {
        var data = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray();
        var (downloader, fs) = CreateNoRangeDownloader(data);

        var progressReports = new List<DownloadProgress>();
        var progress = new Progress<DownloadProgress>(p => progressReports.Add(p));

        await using var session = downloader.StartDownload(Url, FilePath, new DownloadOptions { MaxThreads = 4 }, progress);
        await session.WaitForCompletionAsync();

        progressReports.Should().NotBeEmpty();
        progressReports.All(p => p.State == DownloadState.Downloading).Should().BeTrue();
    }

    // === 辅助 ===

    private static (RangeDownloader downloader, InMemoryFileSystem fs) CreateNoRangeDownloader(byte[] data) {
        var handler = new StubHandler(req => {
            if (req.Method == HttpMethod.Head)
                return new HttpResponseMessage(HttpStatusCode.OK) {
                    RequestMessage = req,
                    Content = new ByteArrayContent([]) { Headers = { ContentLength = data.Length } },
                };
            return new HttpResponseMessage(HttpStatusCode.OK) {
                RequestMessage = req,
                Content = new ByteArrayContent(data),
            };
        });
        var fs = new InMemoryFileSystem();
        var downloader = new RangeDownloader(new TestHttpClientProvider(new HttpClient(handler)), fs);
        return (downloader, fs);
    }

    private sealed class StubHandler : HttpMessageHandler {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        internal StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
