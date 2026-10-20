// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
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

    /// <summary>
    /// GitHub 422 Validation Failed 响应包含 errors 数组,ExtractErrorMessage 应提取具体原因
    /// </summary>
    [Fact]
    public async Task SendAsync_422_ExtractsErrorsArrayDetails() {
        var body = """{"message":"Validation Failed","errors":[{"message":"A pull request already exists for w1."}]}""";
        _handler.Response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent(body) };

        var result = await _client.SendAsync(HttpMethod.Post, "repos/foo/bar/pulls");

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        result.Error.Should().Contain("Validation Failed");
        result.Error.Should().Contain("A pull request already exists for w1.");
    }

    /// <summary>422 多个 errors 项全部提取</summary>
    [Fact]
    public async Task SendAsync_422_MultipleErrors_AllExtracted() {
        var body = """{"message":"Validation Failed","errors":[{"field":"title","code":"missing","message":"title is missing"},{"field":"head","code":"invalid","message":"head ref not found"}]}""";
        _handler.Response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent(body) };

        var result = await _client.SendAsync(HttpMethod.Post, "repos/foo/bar/pulls");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("title is missing");
        result.Error.Should().Contain("head ref not found");
        result.Error.Should().Contain("title: ");
        result.Error.Should().Contain("(code: missing)");
    }

    /// <summary>无 errors 数组时仅返回 message(回归保护)</summary>
    [Fact]
    public async Task SendAsync_422_NoErrorsArray_ReturnsMessageOnly() {
        var body = """{"message":"Validation Failed"}""";
        _handler.Response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent(body) };

        var result = await _client.SendAsync(HttpMethod.Post, "repos/foo/bar/pulls");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Validation Failed");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
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

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task SendAsync_FallsBackToGITHUB_TOKEN_WhenJCCMissing() {
        using var env = EnvVarScope.Set("JCC_GITHUB_TOKEN", null).Add("GITHUB_TOKEN", "fallback-token");
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

        await _client.SendAsync(HttpMethod.Get, "repos/foo/bar");

        _handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("fallback-token");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
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

    /// <summary>
    /// gzip 压缩的纯文本日志 — Content-Encoding: gzip 时应 GZipStream 解压后逐行 yield
    /// <para>背景: 跨境拉 GitHub 日志未压缩 ~27KB/s,Accept-Encoding: gzip 压缩率 5-10x 显著提速</para>
    /// </summary>
    [Fact]
    public async Task GetJobLogsAsync_GzipEncoded_PlainText_DecodesAndYieldsLines() {
        var raw = "gzip line1\ngzip line2\ngzip line3";
        var gzipped = GzipBytes(raw);
        var content = new ByteArrayContent(gzipped);
        content.Headers.Add("Content-Encoding", "gzip");
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        var lines = new List<string>();
        await foreach (var line in _client.GetJobLogsAsync("foo", "bar", 789)) {
            lines.Add(line);
        }

        lines.Should().HaveCount(3);
        lines[0].Should().Contain("gzip line1");
        lines[2].Should().Contain("gzip line3");
    }

    /// <summary>
    /// gzip 压缩的 zip 日志 — 双层解压(gzip → zip → entry 逐行)仍正确
    /// </summary>
    [Fact]
    public async Task GetJobLogsAsync_GzipEncoded_Zip_DoubleDecompressionWorks() {
        var zipBytes = await CreateLogZip([("job-log.txt", "gz zip line1\ngz zip line2")]);
        var gzipped = GzipBytes(zipBytes);
        var content = new ByteArrayContent(gzipped);
        content.Headers.Add("Content-Encoding", "gzip");
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        var lines = new List<string>();
        await foreach (var line in _client.GetJobLogsAsync("foo", "bar", 321)) {
            lines.Add(line);
        }

        lines.Should().HaveCount(2);
        lines[0].Should().Contain("gz zip line1");
        lines[1].Should().Contain("gz zip line2");
    }

    /// <summary>
    /// 请求头应声明 Accept-Encoding: gzip(未压缩响应时服务器忽略,向后兼容)
    /// </summary>
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: FakeHandler.LastRequest 测试内保证非空")]
    [Fact]
    public async Task GetJobLogsAsync_SendsAcceptEncodingGzip() {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent("plain line")
        };

        await foreach (var _ in _client.GetJobLogsAsync("foo", "bar", 654)) {
            break;
        }

        _handler.LastRequest!.Headers.AcceptEncoding.Should().Contain(a =>
            string.Equals(a.Value, "gzip", StringComparison.OrdinalIgnoreCase),
            "日志下载请求应声明 Accept-Encoding: gzip 让服务器返回压缩流");
    }

    /// <summary>构造 gzip 压缩字节</summary>
    private static byte[] GzipBytes(string raw) {
        var src = Encoding.UTF8.GetBytes(raw);
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) {
            gz.Write(src, 0, src.Length);
        }
        return ms.ToArray();
    }

    /// <summary>构造 gzip 压缩字节(重载:字节数组输入)</summary>
    private static byte[] GzipBytes(byte[] src) {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) {
            gz.Write(src, 0, src.Length);
        }
        return ms.ToArray();
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

    // ===== MergeJsonArrays 对象结构分页（缺陷1: 丢页修复）=====

    /// <summary>
    /// GitHub Actions /actions/runs/{id}/jobs 返回 {"jobs":[...],"total_count":N} 对象,
    /// 旧逻辑遇对象直接返回 bodies[0] 丢弃第 2 页,导致 expand=jobs 截断 + expand=failed 0 行
    /// </summary>
    [Fact]
    public void MergeJsonArrays_ObjectWithJobsArray_MergesAllPages() {
        var page1 = """{"jobs":[{"id":1,"name":"job-1"}],"total_count":2}""";
        var page2 = """{"jobs":[{"id":2,"name":"job-2"}],"total_count":2}""";

        var merged = _client.MergeJsonArrays([page1, page2]);

        using var doc = JsonDocument.Parse(merged);
        doc.RootElement.GetProperty("jobs").GetArrayLength().Should().Be(2);
        doc.RootElement.GetProperty("jobs")[0].GetProperty("id").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("jobs")[1].GetProperty("id").GetInt32().Should().Be(2);
    }

    /// <summary>check-runs 端点同对象结构,影响 gh pr checks</summary>
    [Fact]
    public void MergeJsonArrays_ObjectWithCheckRunsArray_MergesAllPages() {
        var page1 = """{"check_runs":[{"id":10,"name":"check-a"}],"total_count":2}""";
        var page2 = """{"check_runs":[{"id":11,"name":"check-b"}],"total_count":2}""";

        var merged = _client.MergeJsonArrays([page1, page2]);

        using var doc = JsonDocument.Parse(merged);
        doc.RootElement.GetProperty("check_runs").GetArrayLength().Should().Be(2);
    }

    /// <summary>合并后保留非数组元数据(如 incomplete_results),仅替换数组属性</summary>
    [Fact]
    public void MergeJsonArrays_Object_PreservesNonArrayMetadata() {
        var page1 = """{"jobs":[{"id":1}],"total_count":3,"incomplete_results":false}""";
        var page2 = """{"jobs":[{"id":2},{"id":3}],"total_count":3,"incomplete_results":false}""";

        var merged = _client.MergeJsonArrays([page1, page2]);

        using var doc = JsonDocument.Parse(merged);
        doc.RootElement.GetProperty("jobs").GetArrayLength().Should().Be(3);
        doc.RootElement.GetProperty("incomplete_results").GetBoolean().Should().BeFalse();
    }

    /// <summary>顶层数组合并(回归保护,确保对象分页修复不破坏原顶层数组路径)</summary>
    [Fact]
    public void MergeJsonArrays_TopLevelArray_StillMerges() {
        var page1 = """[{"id":1}]""";
        var page2 = """[{"id":2}]""";

        var merged = _client.MergeJsonArrays([page1, page2]);

        using var doc = JsonDocument.Parse(merged);
        doc.RootElement.GetArrayLength().Should().Be(2);
    }

    /// <summary>单页对象不丢数据(回归)</summary>
    [Fact]
    public void MergeJsonArrays_SingleObjectPage_PreservesStructure() {
        var page = """{"jobs":[{"id":1,"name":"only"}],"total_count":1}""";

        var merged = _client.MergeJsonArrays([page]);

        using var doc = JsonDocument.Parse(merged);
        doc.RootElement.GetProperty("jobs").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("total_count").GetInt32().Should().Be(1);
    }

    // ===== ZIP BOM 检测测试（改动 P3：GitHubApiClient ZIP BOM）=====

    /// <summary>
    /// 构造含 UTF-8 BOM 的 ZIP 条目（模拟 GitHub Actions 日志 ZIP 带 BOM 的情况）。
    /// </summary>
    private static byte[] CreateLogZipWithBom((string Name, string Content)[] entries, bool withBom) {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create)) {
            foreach (var (name, content) in entries) {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                if (withBom) {
                    var bom = new byte[] { 0xEF, 0xBB, 0xBF };
                    entryStream.Write(bom, 0, bom.Length);
                }
                var contentBytes = Encoding.UTF8.GetBytes(content);
                entryStream.Write(contentBytes, 0, contentBytes.Length);
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// 含 UTF-8 BOM 的 ZIP 条目能正确读取（BOM 被 StreamReader 的 detectEncodingFromByteOrderMarks 跳过）。
    /// </summary>
    [Fact]
    public async Task GetRunLogsAsync_Zip条目带Utf8Bom_应正确解码跳过Bom() {
        var zipBytes = CreateLogZipWithBom([("1_build.txt", "Build started\nBuild completed")], withBom: true);
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(zipBytes)
        };
        _handler.Response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        var lines = new List<string>();
        await foreach (var line in _client.GetRunLogsAsync("foo", "bar", 123)) {
            lines.Add(line);
        }

        lines.Should().HaveCount(2);
        // 行内容不应以 BOM 字符（U+FEFF）开头 — BOM 被 detectEncodingFromByteOrderMarks 跳过
        lines.Should().NotContain(l => l.StartsWith("\uFEFF"));
        lines[0].Should().Contain("Build started");
        lines[1].Should().Contain("Build completed");
    }

    /// <summary>
    /// 不含 BOM 的 ZIP 条目能正常读取（回归保护）。
    /// </summary>
    [Fact]
    public async Task GetRunLogsAsync_Zip条目不带Bom_应正常读取() {
        var zipBytes = CreateLogZipWithBom([("1_test.txt", "Test line 1\nTest line 2")], withBom: false);
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(zipBytes)
        };
        _handler.Response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        var lines = new List<string>();
        await foreach (var line in _client.GetRunLogsAsync("foo", "bar", 456)) {
            lines.Add(line);
        }

        lines.Should().HaveCount(2);
        lines[0].Should().Contain("Test line 1");
        lines[1].Should().Contain("Test line 2");
    }

    /// <summary>
    /// 含 BOM 的 ZIP 条目带中文内容能正确解码。
    /// </summary>
    [Fact]
    public async Task GetRunLogsAsync_Zip条目带Bom中文_应正确解码中文() {
        var zipBytes = CreateLogZipWithBom([("1_cn.txt", "中文日志第一行\n中文日志第二行")], withBom: true);
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(zipBytes)
        };
        _handler.Response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        var lines = new List<string>();
        await foreach (var line in _client.GetRunLogsAsync("foo", "bar", 789)) {
            lines.Add(line);
        }

        lines.Should().HaveCount(2);
        lines.Should().NotContain(l => l.StartsWith("\uFEFF"));
        lines[0].Should().Contain("中文日志第一行");
        lines[1].Should().Contain("中文日志第二行");
    }

    /// <summary>
    /// 多个 ZIP 条目（部分带 BOM、部分不带）都能正确读取。
    /// </summary>
    [Fact]
    public async Task GetRunLogsAsync_多条目混合Bom_都应正确读取() {
        var zipBytes = CreateLogZipWithBom([
            ("1_with_bom.txt", "BOM entry line"),
            ("2_no_bom.txt", "No BOM entry line")
        ], withBom: true);
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(zipBytes)
        };
        _handler.Response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        var lines = new List<string>();
        await foreach (var line in _client.GetRunLogsAsync("foo", "bar", 999)) {
            lines.Add(line);
        }

        lines.Should().HaveCount(2);
        lines.Should().NotContain(l => l.StartsWith("\uFEFF"));
        lines[0].Should().Contain("BOM entry line");
        lines[1].Should().Contain("No BOM entry line");
    }

    // === Job/Run 日志分片下载(IDownloader)路径 ===

    /// <summary>
    /// 注入 IDownloader 时 job 日志走分片下载 — 内容来自分片落盘文件,不走 HttpClient 单流。
    /// </summary>
    [Fact]
    public async Task GetJobLogsAsync_注入Downloader_应走分片下载并读取行() {
        var fs = new InMemoryFileSystem();
        var downloader = new FakeDownloader(fs) { FileContent = "shard line 1\nshard line 2\n"u8.ToArray() };
        await using var client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, fs, downloader: downloader);

        var lines = new List<string>();
        await foreach (var line in client.GetJobLogsAsync("foo", "bar", 42)) {
            lines.Add(line);
        }

        lines.Should().Equal("shard line 1", "shard line 2");
        downloader.DownloadedUrls.Should().ContainSingle()
            .Which.Should().Contain("repos/foo/bar/actions/jobs/42/logs");
        _handler.LastRequest.Should().BeNull("分片路径不应发 HttpClient 单流请求");
    }

    /// <summary>
    /// 分片下载应传递鉴权头(Authorization Bearer)给下载请求(302 到 blob 前的初始鉴权)。
    /// </summary>
    [Fact]
    public async Task GetJobLogsAsync_分片下载_应传递鉴权头() {
        var fs = new InMemoryFileSystem();
        var downloader = new FakeDownloader(fs);
        await using var client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, fs, downloader: downloader);

        await foreach (var _ in client.GetJobLogsAsync("foo", "bar", 42)) { }

        var headers = downloader.ReceivedHeaders.Should().ContainSingle().Which;
        headers.Should().NotBeNull();
        headers.Should().ContainKey("Authorization").WhoseValue.Should().Be("Bearer test-token");
    }

    /// <summary>
    /// 分片下载失败时回退单线程流式路径 — blob 偶发失败不应让日志完全不可用。
    /// </summary>
    [Fact]
    public async Task GetJobLogsAsync_Downloader失败_应回退单流路径() {
        var fs = new InMemoryFileSystem();
        var downloader = new FakeDownloader(fs) { NextSuccess = false };
        await using var client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, fs, downloader: downloader);
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("fallback line 1\nfallback line 2\n") };

        var lines = new List<string>();
        await foreach (var line in client.GetJobLogsAsync("foo", "bar", 42)) {
            lines.Add(line);
        }

        lines.Should().Equal("fallback line 1", "fallback line 2");
        downloader.DownloadedUrls.Should().HaveCount(1, "分片尝试应发生一次后回退");
    }

    /// <summary>
    /// 分片下载消费完后临时目录应被清理(TempDirScope 幂等清理)。
    /// </summary>
    [Fact]
    public async Task GetJobLogsAsync_分片完成_应清理临时目录() {
        var fs = new InMemoryFileSystem();
        var downloader = new FakeDownloader(fs);
        await using var client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, fs, downloader: downloader);

        await foreach (var _ in client.GetJobLogsAsync("foo", "bar", 42)) { }

        var dir = System.IO.Path.GetDirectoryName(downloader.LastFilePath);
        dir.Should().NotBeNull("分片文件路径应有父目录");
        fs.DirectoryExists(dir ?? "").Should().BeFalse("临时目录应被 TempDirScope 清理");
    }

    /// <summary>
    /// run 日志(zip)同样走分片下载 — zip 魔数检测在分片落盘文件流上工作。
    /// </summary>
    [Fact]
    public async Task GetRunLogsAsync_注入Downloader_应走分片下载并解压zip() {
        var fs = new InMemoryFileSystem();
        var zipBytes = CreateLogZipWithBom([("1_build.txt", "Build started\nBuild completed")], withBom: false);
        var downloader = new FakeDownloader(fs) { FileContent = zipBytes };
        await using var client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, fs, downloader: downloader);

        var lines = new List<string>();
        await foreach (var line in client.GetRunLogsAsync("foo", "bar", 123)) {
            lines.Add(line);
        }

        lines.Should().HaveCount(2);
        lines[0].Should().Contain("Build started");
        lines[1].Should().Contain("Build completed");
        downloader.DownloadedUrls.Should().ContainSingle()
            .Which.Should().Contain("repos/foo/bar/actions/runs/123/logs");
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

    /// <summary>模拟 IDownloader — 把预设内容写入目标文件,记录 URL 与请求头</summary>
    private sealed class FakeDownloader(IFileSystem fs) : IDownloader {
        public List<string> DownloadedUrls { get; } = [];
        public List<IReadOnlyDictionary<string, string>?> ReceivedHeaders { get; } = [];
        public byte[] FileContent { get; set; } = "fake log\n"u8.ToArray();
        public bool NextSuccess { get; set; } = true;
        public string? LastFilePath { get; private set; }

        public IDownloadSession StartDownload(string url, string filePath, DownloadOptions? options = null, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) {
            DownloadedUrls.Add(url);
            ReceivedHeaders.Add(options?.Headers);
            LastFilePath = filePath;
            if (NextSuccess) {
                using var stream = fs.CreateStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(FileContent, 0, FileContent.Length);
            }
            return new FakeDownloadSession(NextSuccess, filePath);
        }
    }

    /// <summary>模拟下载会话 — 立即返回预设结果</summary>
    private sealed class FakeDownloadSession(bool success, string filePath) : IDownloadSession {
        public DownloadState State => success ? DownloadState.Completed : DownloadState.Failed;
        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CancelAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<DownloadResult> WaitForCompletionAsync(CancellationToken ct = default)
            => Task.FromResult(new DownloadResult(success, filePath, 100, success ? 100 : 0, TimeSpan.Zero, State, success ? null : "fake download failure"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}