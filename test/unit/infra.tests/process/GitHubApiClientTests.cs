namespace Infra.Tests.Process;

/// <summary>
/// GitHubApiClient 单元测试 — 验证 Token 解析 / 请求构造 / 响应解析 / 错误处理 / 分页
/// </summary>
public sealed class GitHubApiClientTest : IDisposable {
    private readonly FakeHandler _handler = new();
    private readonly GitHubApiClient _client;
    private readonly EnvVarScope _envScope;
    private bool _disposed;

    public GitHubApiClientTest() {
        _envScope = EnvVarScope.Set("JCC_GITHUB_TOKEN", "test-token").Add("JCC_GITHUB_API_URL", null);
        _client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, new InMemoryFileSystem());
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _envScope.DisposeSafe();
        _handler.Dispose();
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
    }

    [Fact]
    public async Task SendAsync_Success_ReturnsBody() {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":1}") };

        var result = await _client.SendAsync(HttpMethod.Get, "repos/foo/bar/pulls/1");

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task SendAsync_404_ReturnsErrorWithGitHubMessage() {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"message\":\"Not Found\"}") };

        var result = await _client.SendAsync(HttpMethod.Get, "repos/foo/bar/pulls/999");

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Be("Not Found");
    }

    [Fact]
    public async Task SendAsync_SetsAuthorizationBearerHeader() {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

        await _client.SendAsync(HttpMethod.Get, "repos/foo/bar/pulls");

        _handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        _handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("test-token");
    }

    [Fact]
    public async Task SendAsync_TokenMissing_ThrowsConfigurationException() {
        using var env = EnvVarScope.Set("JCC_GITHUB_TOKEN", null).Add("GITHUB_TOKEN", null);

        var client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, new InMemoryFileSystem(), ghTokenResolver: () => null);
        var act = async () => await client.SendAsync(HttpMethod.Get, "repos/foo/bar");

        await act.Should().ThrowAsync<ConfigurationException>();
    }

    [Fact]
    public async Task SendAsync_FallsBackToGITHUB_TOKEN_WhenJCCMissing() {
        using var env = EnvVarScope.Set("JCC_GITHUB_TOKEN", null).Add("GITHUB_TOKEN", "fallback-token");
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

        await _client.SendAsync(HttpMethod.Get, "repos/foo/bar");

        _handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("fallback-token");
    }

    [Fact]
    public async Task SendAsync_QueryParameters_AppendedToUrl() {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

        await _client.SendAsync(HttpMethod.Get, "repos/foo/bar/pulls", query: new Dictionary<string, string> { ["state"] = "open", ["limit"] = "10" });

        _handler.LastRequest!.RequestUri!.ToString().Should().Contain("state=open");
        _handler.LastRequest!.RequestUri!.ToString().Should().Contain("limit=10");
    }

    /// <summary>
    /// 纯文本日志流式读取 — 调用方 maxLines break 后,底层应停止下载,不应全量缓冲到 MemoryStream
    /// <para>红测试: 当前 ReadLogStreamLinesAsync 无条件 CopyToAsync 全量缓冲,此测试应失败</para>
    /// </summary>
    [Fact]
    public async Task GetJobLogsAsync_PlainText_StreamsLineByLine_StopsWhenCallerBreaks() {
        // 生成 10000 行纯文本日志(约 500KB)
        var sb = new StringBuilder();
        for (var i = 0; i < 10000; i++) {
            if (i > 0) sb.Append('\n');
            sb.Append($"log line {i} with some padding content to make it realistic enough");
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var trackingStream = new TrackingStream(new MemoryStream(bytes));

        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StreamContent(trackingStream)
        };

        // 只取前 5 行就 break(模拟上层 maxLines 限制)
        var lines = new List<string>();
        await foreach (var line in _client.GetJobLogsAsync("foo", "bar", 123)) {
            lines.Add(line);
            if (lines.Count >= 5) break;
        }

        lines.Should().HaveCount(5);
        // 核心断言: 纯文本流式读取时,调用方 break 后底层应停止下载
        // 全量缓冲会读完整个流(BytesRead == TotalLength),流式只读前几行(BytesRead << TotalLength)
        trackingStream.BytesRead.Should().BeLessThan(trackingStream.TotalLength,
            "纯文本流式读取时,调用方 break 后底层应停止下载,不应全量缓冲到 MemoryStream");
        // 进一步: 读取的字节数应远小于总大小(只读了几行,不是一半以上)
        trackingStream.BytesRead.Should().BeLessThan(trackingStream.TotalLength / 2,
            "只取 5 行,底层读取字节数应远小于总大小的一半");
    }

    /// <summary>
    /// ZIP 日志仍正常解压逐行 yield(回归保护,确保流式优化不破坏 zip 路径)
    /// </summary>
    [Fact]
    public async Task GetJobLogsAsync_Zip_StillWorks_AfterStreamingFix() {
        // 构造包含 3 行日志的 zip
        var zipBytes = await CreateLogZip([
            ("job-log.txt", "line1\nline2\nline3")
        ]);
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(zipBytes)
        };

        var lines = new List<string>();
        await foreach (var line in _client.GetJobLogsAsync("foo", "bar", 456)) {
            lines.Add(line);
        }

        lines.Should().HaveCount(3);
        lines[0].Should().Contain("line1");
        lines[2].Should().Contain("line3");
    }

    /// <summary>构造 GitHub Actions 日志格式的 zip(每个 entry 是一个 step 的日志)</summary>
    private static async Task<byte[]> CreateLogZip((string Name, string Content)[] entries) {
        await using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create)) {
            foreach (var (name, content) in entries) {
                var entry = archive.CreateEntry(name);
                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync(content).ConfigureAwait(true);
            }
        }
        return ms.ToArray();
    }

    private sealed class FakeHandler : HttpMessageHandler {
        public HttpRequestMessage? LastRequest;
        public HttpResponseMessage Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            LastRequest = request;
            return Task.FromResult(Response);
        }

        protected override void Dispose(bool disposing) {
            if (disposing)
                Response.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>包装流,记录实际读取的字节数,用于验证是否全量缓冲</summary>
    private sealed class TrackingStream : Stream {
        private readonly Stream _inner;
        public long BytesRead { get; private set; }
        public long TotalLength { get; }

        public TrackingStream(Stream inner) {
            _inner = inner;
            TotalLength = inner.Length;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override int Read(byte[] buffer, int offset, int count) {
            var n = _inner.Read(buffer, offset, count);
            BytesRead += n;
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) {
            var n = await _inner.ReadAsync(buffer, cancellationToken);
            BytesRead += n;
            return n;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}