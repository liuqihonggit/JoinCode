namespace Core.Tests.Web;

/// <summary>
/// 各搜索引擎供应商单元测试 — mock IApiClient HTTP 响应，验证 JSON 解析与结果提取（ADR 0138）。
/// 覆盖 Tavily/Serper/Brave/SearXNG/SubAgent 五个供应商。
/// </summary>
public sealed class SearchProvidersTests {
    private static Mock<IApiClient> CreateJsonApiClient(string json, HttpStatusCode status = HttpStatusCode.OK) {
        var mock = new Mock<IApiClient>();
        mock.Setup(c => c.SendAsync(It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(status) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        return mock;
    }

    private static Mock<IApiClient> CreateHtmlApiClient(string html) {
        var mock = new Mock<IApiClient>();
        mock.Setup(c => c.SendAsync(It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });
        return mock;
    }

    #region TavilySearchProvider

    [Fact]
    public async Task Tavily_IsAvailable_WithApiKey_ReturnsTrue() {
        using var env = EnvVarScope.Set("TAVILY_API_KEY", "test-key");
        await using var provider = new TavilySearchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Tavily_IsAvailable_WithoutApiKey_ReturnsFalse() {
        using var env = EnvVarScope.Set("TAVILY_API_KEY", null);
        await using var provider = new TavilySearchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Tavily_SearchAsync_ParsesResults() {
        using var env = EnvVarScope.Set("TAVILY_API_KEY", "test-key");
        var json = /*lang=json,strict*/ """{"answer":"test","results":[{"title":"Example","url":"https://example.com","content":"snippet","score":0.9}]}""";
        await using var provider = new TavilySearchProvider(CreateJsonApiClient(json).Object, new FakeConfigRoot());

        var result = await provider.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
        result.Results[0].Title.Should().Be("Example");
        result.Results[0].Url.Should().Be("https://example.com");
        result.Results[0].Snippet.Should().Be("snippet");
    }

    [Fact]
    public async Task Tavily_SearchAsync_EmptyResults_ReturnsSuccess() {
        using var env = EnvVarScope.Set("TAVILY_API_KEY", "test-key");
        var json = /*lang=json,strict*/ """{"answer":"","results":[]}""";
        await using var provider = new TavilySearchProvider(CreateJsonApiClient(json).Object, new FakeConfigRoot());

        var result = await provider.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        result.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Tavily_SearchAsync_ApiError_ReturnsFailure() {
        using var env = EnvVarScope.Set("TAVILY_API_KEY", "test-key");
        await using var provider = new TavilySearchProvider(CreateJsonApiClient("{}", HttpStatusCode.InternalServerError).Object, new FakeConfigRoot());

        var result = await provider.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeFalse();
    }

    #endregion

    #region SerperSearchProvider

    [Fact]
    public async Task Serper_IsAvailable_WithApiKey_ReturnsTrue() {
        using var env = EnvVarScope.Set("SERPER_API_KEY", "test-key");
        await using var provider = new SerperSearchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Serper_SearchAsync_ParsesResults() {
        using var env = EnvVarScope.Set("SERPER_API_KEY", "test-key");
        var json = /*lang=json,strict*/ """{"organic":[{"title":"Google Result","link":"https://google.com","snippet":"search snippet","position":1}]}""";
        await using var provider = new SerperSearchProvider(CreateJsonApiClient(json).Object, new FakeConfigRoot());

        var result = await provider.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
        result.Results[0].Title.Should().Be("Google Result");
        result.Results[0].Url.Should().Be("https://google.com");
    }

    [Fact]
    public async Task Serper_IsAvailable_WithoutApiKey_ReturnsFalse() {
        using var env = EnvVarScope.Set("SERPER_API_KEY", null);
        await using var provider = new SerperSearchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeFalse();
    }

    #endregion

    #region BraveSearchProvider

    [Fact]
    public async Task Brave_IsAvailable_WithApiKey_ReturnsTrue() {
        using var env = EnvVarScope.Set("BRAVE_API_KEY", "test-key");
        await using var provider = new BraveSearchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Brave_SearchAsync_ParsesResults() {
        using var env = EnvVarScope.Set("BRAVE_API_KEY", "test-key");
        var json = /*lang=json,strict*/ """{"web":{"results":[{"title":"Brave Result","url":"https://brave.com","description":"brave desc"}]}}""";
        await using var provider = new BraveSearchProvider(CreateJsonApiClient(json).Object, new FakeConfigRoot());

        var result = await provider.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
        result.Results[0].Title.Should().Be("Brave Result");
        result.Results[0].Url.Should().Be("https://brave.com");
    }

    [Fact]
    public async Task Brave_IsAvailable_WithoutApiKey_ReturnsFalse() {
        using var env = EnvVarScope.Set("BRAVE_API_KEY", null);
        await using var provider = new BraveSearchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeFalse();
    }

    #endregion

    #region SearXngSearchProvider

    [Fact]
    public async Task SearXNG_IsAvailable_WithUrl_ReturnsTrue() {
        using var env = EnvVarScope.Set("SEARXNG_URL", "http://localhost:8080");
        await using var provider = new SearXngSearchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task SearXNG_SearchAsync_ParsesResults() {
        using var env = EnvVarScope.Set("SEARXNG_URL", "http://localhost:8080");
        var json = /*lang=json,strict*/ """{"results":[{"title":"SearXNG Result","url":"https://searxng.org","content":"content","engine":"google"}]}""";
        await using var provider = new SearXngSearchProvider(CreateJsonApiClient(json).Object, new FakeConfigRoot());

        var result = await provider.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
        result.Results[0].Title.Should().Be("SearXNG Result");
        result.Results[0].Url.Should().Be("https://searxng.org");
    }

    [Fact]
    public async Task SearXNG_IsAvailable_WithoutUrl_ReturnsFalse() {
        using var env = EnvVarScope.Set("SEARXNG_URL", null);
        await using var provider = new SearXngSearchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeFalse();
    }

    #endregion

    #region SubAgentSearchProvider

    [Fact]
    public async Task SubAgent_IsAlwaysAvailable() {
        await using var provider = new SubAgentSearchProvider(CreateHtmlApiClient("").Object);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task SubAgent_SearchAsync_ExtractsResultsWithRegex_WhenNoLlm() {
        var html = """
            <html><body>
            <a href="https://example.com">Example Site</a>
            <a href="https://test.org">Test Org</a>
            <a href="https://duckduckgo.com/internal">Internal</a>
            </body></html>
            """;
        await using var provider = new SubAgentSearchProvider(CreateHtmlApiClient(html).Object);

        var result = await provider.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(2);
        result.Results[0].Url.Should().Be("https://example.com");
        result.Results[1].Url.Should().Be("https://test.org");
    }

    [Fact]
    public async Task SubAgent_SearchAsync_EmptyHtml_ReturnsFailure() {
        await using var provider = new SubAgentSearchProvider(CreateHtmlApiClient("").Object);

        var result = await provider.SearchAsync(new SearchRequest("test"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty");
    }

    #endregion

    #region ConfigDrivenEnvVar — IConfigRoot 配置驱动环境变量名

    [Fact]
    public async Task Tavily_ConfigDrivenEnvVar_ReadsCustomEnvVarName() {
        using var env = EnvVarScope.Set("MY_CUSTOM_TAVILY_KEY", "test-key");
        var config = new FakeConfigRoot().Set("searchProviders.tavily.apiKeyEnvVar", "MY_CUSTOM_TAVILY_KEY");
        await using var provider = new TavilySearchProvider(CreateJsonApiClient("{}").Object, config);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Tavily_ConfigDrivenEnvVar_DefaultEnvVarNotRead_WhenCustomConfigured() {
        using var env1 = EnvVarScope.Set("TAVILY_API_KEY", "should-not-use");
        using var env2 = EnvVarScope.Set("MY_CUSTOM_TAVILY_KEY", null);
        var config = new FakeConfigRoot().Set("searchProviders.tavily.apiKeyEnvVar", "MY_CUSTOM_TAVILY_KEY");
        await using var provider = new TavilySearchProvider(CreateJsonApiClient("{}").Object, config);
        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Serper_ConfigDrivenEnvVar_ReadsCustomEnvVarName() {
        using var env = EnvVarScope.Set("MY_CUSTOM_SERPER_KEY", "test-key");
        var config = new FakeConfigRoot().Set("searchProviders.serper.apiKeyEnvVar", "MY_CUSTOM_SERPER_KEY");
        await using var provider = new SerperSearchProvider(CreateJsonApiClient("{}").Object, config);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Brave_ConfigDrivenEnvVar_ReadsCustomEnvVarName() {
        using var env = EnvVarScope.Set("MY_CUSTOM_BRAVE_KEY", "test-key");
        var config = new FakeConfigRoot().Set("searchProviders.brave.apiKeyEnvVar", "MY_CUSTOM_BRAVE_KEY");
        await using var provider = new BraveSearchProvider(CreateJsonApiClient("{}").Object, config);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task SearXNG_ConfigDrivenEnvVar_ReadsCustomEndpointEnvVarName() {
        using var env = EnvVarScope.Set("MY_CUSTOM_SEARXNG_URL", "http://localhost:9090");
        var config = new FakeConfigRoot().Set("searchProviders.searxng.endpointEnvVar", "MY_CUSTOM_SEARXNG_URL");
        await using var provider = new SearXngSearchProvider(CreateJsonApiClient("{}").Object, config);
        provider.IsAvailable.Should().BeTrue();
    }

    #endregion
}
