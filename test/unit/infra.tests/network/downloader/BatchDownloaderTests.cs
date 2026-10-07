namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// BatchDownloader 单元测试 — 验证批量并行下载、空列表、部分失败、默认选项
/// </summary>
public sealed class BatchDownloaderTests {

    [Fact]
    public async Task DownloadAllAsync_EmptyList_ReturnsEmpty() {
        await using var fs = new InMemoryFileSystem();
        await using var downloader = CreateRangeDownloader(_ => new HttpResponseMessage(HttpStatusCode.OK), fs);
        await using var batch = new BatchDownloader(downloader);

        var results = await batch.DownloadAllAsync([]);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAllAsync_MultipleItems_AllSucceed() {
        var data1 = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        var data2 = Enumerable.Range(0, 2048).Select(i => (byte)(i % 256)).ToArray();
        var handler = new MultiUrlStubHandler();
        handler.Setup("https://example.com/a.bin", data1);
        handler.Setup("https://example.com/b.bin", data2);
        await using var fs = new InMemoryFileSystem();
        await using var downloader = CreateRangeDownloader(handler, fs);
        await using var batch = new BatchDownloader(downloader);

        var items = new List<BatchDownloadItem> {
            new("https://example.com/a.bin", "/tmp/a.bin"),
            new("https://example.com/b.bin", "/tmp/b.bin"),
        };
        var results = await batch.DownloadAllAsync(items, maxConcurrency: 4);

        results.Should().HaveCount(2);
        results.All(r => r.Success).Should().BeTrue();
        fs.FileExists("/tmp/a.bin").Should().BeTrue();
        fs.FileExists("/tmp/b.bin").Should().BeTrue();
        (await fs.ReadAllBytes("/tmp/a.bin")).Should().Equal(data1);
        (await fs.ReadAllBytes("/tmp/b.bin")).Should().Equal(data2);
    }

    [Fact]
    public async Task DownloadAllAsync_OneItemFails_OthersStillSucceed() {
        var dataOk = new byte[] { 1, 2, 3, 4 };
        var handler = new MultiUrlStubHandler();
        handler.Setup("https://example.com/ok.bin", dataOk);
        await using var fs = new InMemoryFileSystem();
        await using var downloader = CreateRangeDownloader(handler, fs);
        await using var batch = new BatchDownloader(downloader);

        var items = new List<BatchDownloadItem> {
            new("https://example.com/ok.bin", "/tmp/ok.bin"),
            new("https://example.com/missing.bin", "/tmp/missing.bin"),
        };
        var results = await batch.DownloadAllAsync(items, maxConcurrency: 2);

        results.Should().HaveCount(2);
        results.First(r => r.FilePath == "/tmp/ok.bin").Success.Should().BeTrue();
        results.First(r => r.FilePath == "/tmp/missing.bin").Success.Should().BeFalse();
        fs.FileExists("/tmp/ok.bin").Should().BeTrue();
    }

    [Fact]
    public async Task DownloadAllAsync_DefaultOptions_MultiThreadAndResume() {
        var data = Enumerable.Range(0, 4096).Select(i => (byte)(i % 256)).ToArray();
        var handler = new MultiUrlStubHandler();
        handler.Setup("https://example.com/file.bin", data);
        await using var fs = new InMemoryFileSystem();
        await using var downloader = CreateRangeDownloader(handler, fs);
        await using var batch = new BatchDownloader(downloader);

        var items = new List<BatchDownloadItem> {
            new("https://example.com/file.bin", "/tmp/file.bin"),
        };
        var results = await batch.DownloadAllAsync(items);

        results.Should().HaveCount(1);
        results[0].Success.Should().BeTrue();
        (await fs.ReadAllBytes("/tmp/file.bin")).Should().Equal(data);
    }

    [Fact]
    public async Task DownloadAllAsync_ResultsOrderMatchesInput() {
        var handler = new MultiUrlStubHandler();
        handler.Setup("https://example.com/1.bin", new byte[] { 1 });
        handler.Setup("https://example.com/2.bin", new byte[] { 2 });
        handler.Setup("https://example.com/3.bin", new byte[] { 3 });
        await using var fs = new InMemoryFileSystem();
        await using var downloader = CreateRangeDownloader(handler, fs);
        await using var batch = new BatchDownloader(downloader);

        var items = new List<BatchDownloadItem> {
            new("https://example.com/1.bin", "/tmp/1.bin"),
            new("https://example.com/2.bin", "/tmp/2.bin"),
            new("https://example.com/3.bin", "/tmp/3.bin"),
        };
        var results = await batch.DownloadAllAsync(items, maxConcurrency: 3);

        results.Select(r => r.FilePath).Should().Equal("/tmp/1.bin", "/tmp/2.bin", "/tmp/3.bin");
    }

    // === 辅助 ===

    private static RangeDownloader CreateRangeDownloader(HttpMessageHandler handler, InMemoryFileSystem fs)
        => new(new TestHttpClientProvider(new HttpClient(handler)), fs);

    private static RangeDownloader CreateRangeDownloader(Func<HttpRequestMessage, HttpResponseMessage> handler, InMemoryFileSystem fs)
        => CreateRangeDownloader(new StubHandler(handler), fs);

    private sealed class StubHandler : HttpMessageHandler {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        internal StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed class MultiUrlStubHandler : HttpMessageHandler {
        private readonly Dictionary<string, byte[]> _data = new();
        internal void Setup(string url, byte[] data) => _data[url] = data;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) {
            var url = req.RequestUri!.ToString();
            if (!_data.TryGetValue(url, out var data)) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = req });
            }
            if (req.Method == HttpMethod.Head) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    RequestMessage = req,
                    Headers = { AcceptRanges = { "bytes" } },
                    Content = new ByteArrayContent([]) { Headers = { ContentLength = data.Length } }
                });
            }
            var range = req.Headers.Range?.Ranges.FirstOrDefault();
            if (range is null) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    RequestMessage = req,
                    Content = new ByteArrayContent(data)
                });
            }
            var start = (int)(range.From ?? 0);
            var end = (int)(range.To ?? data.Length - 1);
            var length = end - start + 1;
            var chunk = new byte[length];
            Array.Copy(data, start, chunk, 0, length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) {
                RequestMessage = req,
                Content = new ByteArrayContent(chunk)
            });
        }
    }
}
