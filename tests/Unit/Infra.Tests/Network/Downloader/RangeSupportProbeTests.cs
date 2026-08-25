namespace Infra.Services.Tests.Network.Downloader;

/// <summary>
/// RangeSupportProbe 单元测试 — 验证 Range 支持探测:HEAD/405回退GET/206判定/Accept-Ranges/ETag/LastModified
/// <para>用 StubHandler mock HTTP 响应,零真实网络请求</para>
/// </summary>
public sealed class RangeSupportProbeTests
{
    private const string Url = "https://example.com/file.bin";

    // === HEAD 成功 + Accept-Ranges: bytes ===

    [Fact]
    public async Task Probe_HeadWithAcceptRanges_ReturnsSupported()
    {
        var probe = CreateProbe(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Headers = { AcceptRanges = { "bytes" } },
            Content = new ByteArrayContent([]) { Headers = { ContentLength = 1024 } }
        });

        var result = await probe.ProbeAsync(Url);

        result.SupportsRange.Should().BeTrue();
        result.ContentLength.Should().Be(1024);
    }

    // === HEAD 成功但无 Accept-Ranges ===

    [Fact]
    public async Task Probe_HeadWithoutAcceptRanges_ReturnsNotSupported()
    {
        var probe = CreateProbe(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([]) { Headers = { ContentLength = 1024 } }
        });

        var result = await probe.ProbeAsync(Url);

        result.SupportsRange.Should().BeFalse();
        result.ContentLength.Should().Be(1024);
    }

    // === HEAD 405 回退 GET + Range,响应 206 → 支持 ===

    [Fact]
    public async Task Probe_Head405_FallbackGet206_ReturnsSupported()
    {
        var probe = CreateProbe(req => req.Method == HttpMethod.Head
            ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
            : new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([0x00])
                {
                    Headers = { ContentRange = new ContentRangeHeaderValue(0, 0, 1024) }
                }
            });

        var result = await probe.ProbeAsync(Url);

        result.SupportsRange.Should().BeTrue();
        result.ContentLength.Should().Be(1024);
    }

    // === HEAD 403 回退 GET + Range,响应 200 → 不支持 ===

    [Fact]
    public async Task Probe_Head403_FallbackGet200_ReturnsNotSupported()
    {
        var probe = CreateProbe(req => req.Method == HttpMethod.Head
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[1024]) { Headers = { ContentLength = 1024 } }
            });

        var result = await probe.ProbeAsync(Url);

        result.SupportsRange.Should().BeFalse();
        result.ContentLength.Should().Be(1024);
    }

    // === ETag 和 LastModified 解析 ===

    [Fact]
    public async Task Probe_HeadWithETagAndLastModified_ReturnsBoth()
    {
        var lm = DateTimeOffset.Parse("2026-08-25T10:00:00Z");
        var probe = CreateProbe(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Headers = { AcceptRanges = { "bytes" }, ETag = new EntityTagHeaderValue("\"abc123\"") },
            Content = new ByteArrayContent([])
            {
                Headers = { ContentLength = 100, LastModified = lm }
            }
        });

        var result = await probe.ProbeAsync(Url);

        result.ETag.Should().Be("\"abc123\"");
        result.LastModified.Should().Be(lm);
    }

    // === 无 Content-Length(StreamContent 不自动设置) ===

    [Fact]
    public async Task Probe_HeadNoContentLength_ReturnsNull()
    {
        var probe = CreateProbe(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Headers = { AcceptRanges = { "bytes" } },
                Content = new StreamContent(new MemoryStream())
            };
            resp.Content.Headers.ContentLength = null;
            return resp;
        });

        var result = await probe.ProbeAsync(Url);

        result.SupportsRange.Should().BeTrue();
        result.ContentLength.Should().BeNull();
    }

    // === HEAD 其他错误(500)→ 不支持,null 长度 ===

    [Fact]
    public async Task Probe_Head500_ReturnsNotSupported()
    {
        var probe = CreateProbe(req => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await probe.ProbeAsync(Url);

        result.SupportsRange.Should().BeFalse();
        result.ContentLength.Should().BeNull();
    }

    // === GET 206 无 Content-Range.Length,用 ContentLength ===

    [Fact]
    public async Task Probe_Head405_Get206NoContentRangeLength_UsesContentLength()
    {
        var probe = CreateProbe(req => req.Method == HttpMethod.Head
            ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
            : new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([0x00]) { Headers = { ContentLength = 1 } }
            });

        var result = await probe.ProbeAsync(Url);

        result.SupportsRange.Should().BeTrue();
        result.ContentLength.Should().Be(1);
    }

    // === 辅助 ===

    private static RangeSupportProbe CreateProbe(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new StubHandler(handler)));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        internal StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
