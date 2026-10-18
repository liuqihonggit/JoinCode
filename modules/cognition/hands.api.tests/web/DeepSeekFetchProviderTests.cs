namespace Core.Tests.Web;

/// <summary>
/// DeepSeekFetchProvider 单元测试 — mock IApiClient HTTP 响应，验证 JSON 解析与 Markdown 内容提取。
/// </summary>
public sealed class DeepSeekFetchProviderTests {
    private static Mock<IApiClient> CreateJsonApiClient(string json, HttpStatusCode status = HttpStatusCode.OK) {
        var mock = new Mock<IApiClient>();
        mock.Setup(c => c.SendAsync(It.IsAny<ApiRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(status) {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        return mock;
    }

    [Fact]
    public async Task IsAvailable_WithApiKey_ReturnsTrue() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", "test-key");
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailable_WithoutApiKey_ReturnsFalse() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", null);
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Name_ReturnsDeepSeek() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", "test-key");
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        provider.Name.Should().Be("DeepSeek");
    }

    [Fact]
    public async Task FetchAsync_ParsesContent_WithTitle_PrependsHeading() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", "test-key");
        var json = /*lang=json,strict*/ """{"title":"Example Article","content":"Article body in Markdown."}""";
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient(json).Object, new FakeConfigRoot());

        var result = await provider.FetchAsync("https://example.com/article");

        result.Success.Should().BeTrue();
        result.Url.Should().Be("https://example.com/article");
        result.Content.Should().Be("# Example Article\n\nArticle body in Markdown.");
        result.ContentType.Should().Be("text/markdown");
        result.StatusCode.Should().Be(200);
        result.Bytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FetchAsync_ParsesContent_WithoutTitle_ReturnsContentOnly() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", "test-key");
        var json = /*lang=json,strict*/ """{"title":"","content":"Markdown content without title."}""";
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient(json).Object, new FakeConfigRoot());

        var result = await provider.FetchAsync("https://example.com");

        result.Success.Should().BeTrue();
        result.Content.Should().Be("Markdown content without title.");
    }

    [Fact]
    public async Task FetchAsync_EmptyContent_ReturnsFailure() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", "test-key");
        var json = /*lang=json,strict*/ """{"title":"Title","content":""}""";
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient(json).Object, new FakeConfigRoot());

        var result = await provider.FetchAsync("https://example.com");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("empty content");
    }

    [Fact]
    public async Task FetchAsync_ApiError_ReturnsFailure() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", "test-key");
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient("{}", HttpStatusCode.InternalServerError).Object, new FakeConfigRoot());

        var result = await provider.FetchAsync("https://example.com");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_WithoutApiKey_ReturnsFailure() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", null);
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());

        var result = await provider.FetchAsync("https://example.com");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("DEEPSEEK_API_KEY");
    }

    [Fact]
    public async Task FetchAsync_EmptyUrl_ReturnsFailure() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", "test-key");
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());

        var result = await provider.FetchAsync("");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("URL");
    }

    [Fact]
    public async Task FetchAsync_UrlExceeds8192Chars_ReturnsFailure() {
        using var env = EnvVarScope.Set("DEEPSEEK_API_KEY", "test-key");
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient("{}").Object, new FakeConfigRoot());
        var longUrl = "https://example.com/" + new string('a', 8192);

        var result = await provider.FetchAsync(longUrl);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("8192");
    }

    [Fact]
    public async Task ConfigDrivenEnvVar_ReadsCustomEnvVarName() {
        using var env = EnvVarScope.Set("MY_CUSTOM_DEEPSEEK_KEY", "test-key");
        var config = new FakeConfigRoot().Set("searchProviders.deepseek.apiKeyEnvVar", "MY_CUSTOM_DEEPSEEK_KEY");
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient("{}").Object, config);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task ConfigDrivenEnvVar_DefaultEnvVarNotRead_WhenCustomConfigured() {
        using var env1 = EnvVarScope.Set("DEEPSEEK_API_KEY", "should-not-use");
        using var env2 = EnvVarScope.Set("MY_CUSTOM_DEEPSEEK_KEY", null);
        var config = new FakeConfigRoot().Set("searchProviders.deepseek.apiKeyEnvVar", "MY_CUSTOM_DEEPSEEK_KEY");
        await using var provider = new DeepSeekFetchProvider(CreateJsonApiClient("{}").Object, config);
        provider.IsAvailable.Should().BeFalse();
    }
}
