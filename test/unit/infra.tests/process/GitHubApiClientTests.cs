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
        _client?.Dispose();
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

        await using var client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, new InMemoryFileSystem(), ghTokenResolver: () => null);
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

    // ===== 阶段2.9 拆分:纯计算方法测试 =====

    [Fact]
    public void IsZipPrefix_PkMagicBytes_ReturnsTrue() {
        var prefix = new byte[] { 0x50, 0x4B };
        GitHubApiClient.IsZipPrefix(prefix, prefixRead: 2).Should().BeTrue();
    }

    [Fact]
    public void IsZipPrefix_NonPkBytes_ReturnsFalse() {
        var prefix = new byte[] { 0x48, 0x54 }; // "HT"
        GitHubApiClient.IsZipPrefix(prefix, prefixRead: 2).Should().BeFalse();
    }

    [Fact]
    public void IsZipPrefix_PartialRead_SingleByte_ReturnsFalse() {
        var prefix = new byte[] { 0x50, 0x00 };
        GitHubApiClient.IsZipPrefix(prefix, prefixRead: 1).Should().BeFalse();
    }

    [Fact]
    public void IsZipPrefix_EmptyRead_ReturnsFalse() {
        var prefix = new byte[2];
        GitHubApiClient.IsZipPrefix(prefix, prefixRead: 0).Should().BeFalse();
    }

    [Fact]
    public void IsZipPrefix_PkFollowedByOtherBytes_StillTrue() {
        // ZIP 魔数只需前 2 字节为 PK,后续字节不影响
        var prefix = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        GitHubApiClient.IsZipPrefix(prefix, prefixRead: 4).Should().BeTrue();
    }

    [Fact]
    public void BuildEmptyResponseError_IncludesScopeAndZeroByteMessage() {
        var error = GitHubApiClient.BuildEmptyResponseError("run 123");
        error.Should().Contain("run 123");
        error.Should().Contain("0 字节");
        error.Should().StartWith("[ERROR]");
    }

    [Fact]
    public void BuildEmptyResponseError_DifferentScopes() {
        GitHubApiClient.BuildEmptyResponseError("run 42").Should().Contain("run 42");
        GitHubApiClient.BuildEmptyResponseError("job 99").Should().Contain("job 99");
    }

    [Fact]
    public void FormatZipEntryLine_PrefixesLineWithEntryName() {
        GitHubApiClient.FormatZipEntryLine("build.log", "step 1 started")
            .Should().Be("[build.log] step 1 started");
    }

    [Fact]
    public void FormatZipEntryLine_EmptyLine_PreservesEntryNameBracket() {
        GitHubApiClient.FormatZipEntryLine("test.log", "")
            .Should().Be("[test.log] ");
    }

    [Fact]
    public void FormatZipEntryLine_LineWithSpecialChars_PreservedAsIs() {
        GitHubApiClient.FormatZipEntryLine("step.log", "error: <tag> & \"quote\"")
            .Should().Be("[step.log] error: <tag> & \"quote\"");
    }

    [Fact]
    public void BuildZipDecompressionError_IncludesScopeExceptionAndHexPrefix() {
        var buffer = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00 };
        var ex = new InvalidOperationException("bad zip");

        var error = GitHubApiClient.BuildZipDecompressionError("run 7", ex, buffer, length: 5);

        error.Should().Contain("run 7");
        error.Should().Contain("bad zip");
        error.Should().Contain("响应大小=5 字节");
        error.Should().Contain("前4字节=504B0304");
        error.Should().Contain("gh run view --log");
    }

    [Fact]
    public void BuildZipDecompressionError_ShortBuffer_HexOnlyAvailableBytes() {
        // length < 4 时 hex 只显示 length 字节
        var buffer = new byte[] { 0x48, 0x54 };
        var ex = new Exception("corrupted");

        var error = GitHubApiClient.BuildZipDecompressionError("job 3", ex, buffer, length: 2);

        error.Should().Contain("响应大小=2 字节");
        error.Should().Contain("前4字节=4854"); // 只显示 2 字节 hex
    }

    [Fact]
    public void BuildZipDecompressionError_ZeroLength_EmptyHexPrefix() {
        var buffer = Array.Empty<byte>();
        var ex = new Exception("empty");

        var error = GitHubApiClient.BuildZipDecompressionError("run 0", ex, buffer, length: 0);

        error.Should().Contain("响应大小=0 字节");
        error.Should().Contain("前4字节="); // hex 为空字符串
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