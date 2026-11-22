namespace Sync.Tests.ToolHandlers;

/// <summary>
/// TarballDownloader 并行下载器单元测试 — mock HttpMessageHandler
/// </summary>
public sealed class TarballDownloaderTests {
    [Fact]
    public async Task DownloadAllAsync_Success_WritesTarballFiles() {
        await using var fs = new IO.FileSystem.PhysicalFileSystem();
        var handler = new StubHttpHandler(HttpStatusCode.OK, "fake-tarball"u8.ToArray());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };

        var downloader = new TarballDownloader(client, "fake-token", fs);
        var tempDir = fs.CombinePath(System.IO.Path.GetTempPath(), "tarball_test_" + Guid.NewGuid().ToString("N"));
        var requests = new List<SubmoduleDownloadRequest> {
            new("owner1", "repo1", "main", "libs/repo1"),
            new("owner2", "repo2", "develop", "libs/repo2"),
        };

        try {
            var results = await downloader.DownloadAllAsync(requests, tempDir, 4, CancellationToken.None);

            results.Should().HaveCount(2);
            results.Should().AllSatisfy(r => r.Success.Should().BeTrue());
            foreach (var r in results) {
                if (r.TarballPath is string path) {
                    fs.FileExists(path).Should().BeTrue();
                }
            }
        } finally {
            if (fs.DirectoryExists(tempDir)) fs.DeleteDirectory(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAllAsync_EmptyRequests_ReturnsEmpty() {
        await using var fs = new IO.FileSystem.PhysicalFileSystem();
        var handler = new StubHttpHandler(HttpStatusCode.OK, []);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };

        var downloader = new TarballDownloader(client, "", fs);
        var results = await downloader.DownloadAllAsync([], "/tmp/test", 4, CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task DownloadAllAsync_Http404_ReturnsFailedResult() {
        await using var fs = new IO.FileSystem.PhysicalFileSystem();
        var handler = new StubHttpHandler(HttpStatusCode.NotFound, "not found"u8.ToArray());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };

        var downloader = new TarballDownloader(client, "fake-token", fs);
        var tempDir = fs.CombinePath(System.IO.Path.GetTempPath(), "tarball_404_" + Guid.NewGuid().ToString("N"));

        try {
            var requests = new List<SubmoduleDownloadRequest> { new("owner", "repo", "main", "libs/repo") };
            var results = await downloader.DownloadAllAsync(requests, tempDir, 4, CancellationToken.None);

            results.Should().HaveCount(1);
            results[0].Success.Should().BeFalse();
            results[0].Error.Should().Contain("404");
        } finally {
            if (fs.DirectoryExists(tempDir)) fs.DeleteDirectory(tempDir, recursive: true);
        }
    }

    private sealed class StubHttpHandler : HttpMessageHandler {
        private readonly HttpStatusCode _status;
        private readonly byte[] _content;

        public StubHttpHandler(HttpStatusCode status, byte[] content) {
            _status = status;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var response = new HttpResponseMessage(_status) {
                Content = new ByteArrayContent(_content),
            };
            return Task.FromResult(response);
        }
    }
}
