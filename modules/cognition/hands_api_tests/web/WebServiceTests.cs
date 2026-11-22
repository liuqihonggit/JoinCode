namespace Core.Tests.Web;

/// <summary>
/// WebService 单元测试 — 测试输入验证 + 委托 ISearchProviderCoordinator（ADR 0138）。
/// Anthropic web_search 行为测试迁移至 AnthropicSearchProviderTests。
/// </summary>
public sealed class WebServiceTests {
    private readonly Mock<IApiClient> _apiClientMock = new();
    private readonly Mock<ISearchProviderCoordinator> _coordinatorMock = new();

    private async Task<WebService> CreateService() {
        var cache = new WebFetchCache();
        await using var domainChecker = new DomainBlocklistChecker(_apiClientMock.Object, cache);
        await using var binaryStorage = new BinaryContentStorage(new IO.FileSystem.PhysicalFileSystem());

        var middlewares = new IMiddleware<WebContext>[]
        {
            new MetricsMiddleware<WebContext>(),
            new WebValidationMiddleware(),
            new WebCacheCheckMiddleware(cache),
            new WebDomainCheckMiddleware(domainChecker),
            new WebFetchMiddleware(_apiClientMock.Object),
            new WebContentProcessingMiddleware(new HtmlToMarkdownConverter(), binaryStorage),
            new WebCacheWriteMiddleware(cache)
        };
        var pipeline = new MiddlewarePipeline<WebContext>(middlewares.Cast<IMiddleware<WebContext>>());

        return new WebService(pipeline, cache, _coordinatorMock.Object, Array.Empty<IWebFetchProvider>());
    }

    #region SearchAsync - 输入验证

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsError() {
        await using var service = await CreateService();
        var result = await service.SearchAsync("").ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsError() {
        await using var service = await CreateService();
        var result = await service.SearchAsync("   ").ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task SearchAsync_BothAllowedAndBlockedDomains_ReturnsError() {
        await using var service = await CreateService();
        var result = await service.SearchAsync("test",
            allowedDomains: ["example.com"],
            blockedDomains: ["bad.com"]).ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cannot specify both");
    }

    #endregion

    #region SearchAsync - 委托 Coordinator

    [Fact]
    public async Task SearchAsync_ValidQuery_DelegatesToCoordinator() {
        _coordinatorMock
            .Setup(c => c.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResult(true, "test query", [], 0));
        await using var service = await CreateService();
        var result = await service.SearchAsync("test query").ConfigureAwait(true);

        result.Success.Should().BeTrue();
        _coordinatorMock.Verify(
            c => c.SearchAsync(It.Is<SearchRequest>(r => r.Query == "test query"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_CoordinatorReturnsError_PassesErrorThrough() {
        _coordinatorMock
            .Setup(c => c.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new WebSearchResult(false, "test", [], 0, ErrorMessage: "All providers failed")));

        await using var service = await CreateService();
        var result = await service.SearchAsync("test query").ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("All providers failed");
    }

    [Fact]
    public async Task SearchAsync_CoordinatorReturnsResults_PassesResultsThrough() {
        var expectedResults = new List<SearchResultItem> {
            new("Example", "https://example.com", Snippet: "Test snippet")
        };
        _coordinatorMock
            .Setup(c => c.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(new WebSearchResult(true, "test", expectedResults, 1)));

        await using var service = await CreateService();
        var result = await service.SearchAsync("test query").ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
        result.Results[0].Title.Should().Be("Example");
        result.Results[0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task SearchAsync_PassesAllowedDomainsToCoordinator() {
        _coordinatorMock
            .Setup(c => c.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResult(true, "test", [], 0));
        await using var service = await CreateService();
        await service.SearchAsync("test", allowedDomains: ["example.com"]).ConfigureAwait(true);

        _coordinatorMock.Verify(
            c => c.SearchAsync(It.Is<SearchRequest>(r => r.AllowedDomains != null && r.AllowedDomains.Contains("example.com")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_PassesBlockedDomainsToCoordinator() {
        _coordinatorMock
            .Setup(c => c.SearchAsync(It.IsAny<SearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResult(true, "test", [], 0));
        await using var service = await CreateService();
        await service.SearchAsync("test", blockedDomains: ["bad.com"]).ConfigureAwait(true);

        _coordinatorMock.Verify(
            c => c.SearchAsync(It.Is<SearchRequest>(r => r.BlockedDomains != null && r.BlockedDomains.Contains("bad.com")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
