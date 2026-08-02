namespace Hands.Tests.Web;

/// <summary>
/// WebService 二进制内容持久化集成测试 — 对齐TS版 WebFetchTool/utils.ts 的二进制处理链路
/// </summary>
public class WebServiceBinaryContentTests
{
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private static Mock<IApiClient> CreateMockApiClient(byte[] content, string contentType)
    {
        var apiClient = new Mock<IApiClient>();

        apiClient.Setup(c => c.SendAsync(It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiRequest req, CancellationToken _) =>
            {
                // 域名黑名单检查URL以 api.anthropic.com 开头
                if (req.Path.StartsWith("https://api.anthropic.com", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"can_fetch\":true}")
                    };
                }

                // 内容获取请求 — 返回实际内容
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                return response;
            });

        return apiClient;
    }

    private WebService CreateService(Mock<IApiClient> apiClient)
    {
        var cache = new WebFetchCache();
        var domainChecker = new DomainBlocklistChecker(apiClient.Object, cache);
        var binaryStorage = new BinaryContentStorage(_fs);

        var middlewares = new IMiddleware<WebContext>[]
        {
            new MetricsMiddleware<WebContext>(),
            new WebValidationMiddleware(),
            new WebCacheCheckMiddleware(cache),
            new WebDomainCheckMiddleware(domainChecker),
            new WebFetchMiddleware(apiClient.Object),
            new WebContentProcessingMiddleware(new HtmlToMarkdownConverter(), binaryStorage),
            new WebCacheWriteMiddleware(cache)
        };
        var pipeline = new MiddlewarePipeline<WebContext>(middlewares.Cast<IMiddleware<WebContext>>());

        return new WebService(pipeline, cache);
    }

    [Fact]
    public async Task FetchAsync_BinaryContent_PersistsFile()
    {
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E }; // %PDF-1.
        var service = CreateService(CreateMockApiClient(pdfBytes, "application/pdf"));
        var result = await service.FetchAsync("https://example.com/doc.pdf", CancellationToken.None).ConfigureAwait(true);

        if (!result.Success) Assert.Fail($"FetchAsync failed: {result.ErrorMessage}");

        result.PersistedPath.Should().NotBeNull();
        result.PersistedPath.Should().EndWith(".pdf");
        result.PersistedSize.Should().Be(pdfBytes.Length);
        _fs.FileExists(result.PersistedPath!).Should().BeTrue();
    }

    [Fact]
    public async Task FetchAsync_TextContent_DoesNotPersist()
    {
        var htmlBytes = "<html><body>Hello</body></html>"u8.ToArray();
        var service = CreateService(CreateMockApiClient(htmlBytes, "text/html"));
        var result = await service.FetchAsync("https://example.com/page.html", CancellationToken.None).ConfigureAwait(true);

        if (!result.Success) Assert.Fail($"FetchAsync failed: {result.ErrorMessage}");

        result.PersistedPath.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_JsonContent_DoesNotPersist()
    {
        var jsonBytes = "{\"key\":\"value\"}"u8.ToArray();
        var service = CreateService(CreateMockApiClient(jsonBytes, "application/json"));
        var result = await service.FetchAsync("https://example.com/data.json", CancellationToken.None).ConfigureAwait(true);

        if (!result.Success) Assert.Fail($"FetchAsync failed: {result.ErrorMessage}");

        result.PersistedPath.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_ImageContent_PersistsWithCorrectExtension()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }; // PNG header
        var service = CreateService(CreateMockApiClient(pngBytes, "image/png"));
        var result = await service.FetchAsync("https://example.com/image.png", CancellationToken.None).ConfigureAwait(true);

        if (!result.Success) Assert.Fail($"FetchAsync failed: {result.ErrorMessage}");

        result.PersistedPath.Should().EndWith(".png");
    }

    [Fact]
    public async Task FetchAsync_BinaryContent_StillReturnsTextContent()
    {
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
        var service = CreateService(CreateMockApiClient(pdfBytes, "application/pdf"));
        var result = await service.FetchAsync("https://example.com/doc.pdf", CancellationToken.None).ConfigureAwait(true);

        if (!result.Success) Assert.Fail($"FetchAsync failed: {result.ErrorMessage}");

        result.Content.Should().NotBeNull();
        result.Content.Should().Contain("%PDF");
    }

    [Fact]
    public async Task FetchAsync_BinaryContent_CacheContainsPersistedPath()
    {
        var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var apiClient = CreateMockApiClient(pdfBytes, "application/pdf");
        var service = CreateService(apiClient);

        var result1 = await service.FetchAsync("https://example.com/doc.pdf", CancellationToken.None).ConfigureAwait(true);
        if (!result1.Success) Assert.Fail($"First fetch failed: {result1.ErrorMessage}");
        result1.PersistedPath.Should().NotBeNull();

        var result2 = await service.FetchAsync("https://example.com/doc.pdf", CancellationToken.None).ConfigureAwait(true);
        result2.PersistedPath.Should().NotBeNull();
        result2.PersistedPath.Should().Be(result1.PersistedPath);
    }
}
