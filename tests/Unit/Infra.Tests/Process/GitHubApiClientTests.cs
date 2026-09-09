namespace Infra.Tests.Process;

/// <summary>
/// GitHubApiClient 单元测试 — 验证 Token 解析 / 请求构造 / 响应解析 / 错误处理 / 分页
/// </summary>
public sealed class GitHubApiClientTest : IDisposable
{
    private readonly FakeHandler _handler = new();
    private readonly GitHubApiClient _client;
    private readonly EnvVarScope _envScope;

    public GitHubApiClientTest()
    {
        _envScope = EnvVarScope.Set("JCC_GITHUB_TOKEN", "test-token").Add("JCC_GITHUB_API_URL", null);
        _client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, new InMemoryFileSystem());
    }

    public void Dispose()
    {
        _envScope.Dispose();
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
    }

    [Fact]
    public async Task SendAsync_Success_ReturnsBody()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":1}") };

        var result = await _client.SendAsync(HttpMethod.Get, "repos/foo/bar/pulls/1");

        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Body.Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task SendAsync_404_ReturnsErrorWithGitHubMessage()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"message\":\"Not Found\"}") };

        var result = await _client.SendAsync(HttpMethod.Get, "repos/foo/bar/pulls/999");

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Error.Should().Be("Not Found");
    }

    [Fact]
    public async Task SendAsync_SetsAuthorizationBearerHeader()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

        await _client.SendAsync(HttpMethod.Get, "repos/foo/bar/pulls");

        _handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        _handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("test-token");
    }

    [Fact]
    public async Task SendAsync_TokenMissing_ThrowsConfigurationException()
    {
        Environment.SetEnvironmentVariable("JCC_GITHUB_TOKEN", null);
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);

        var client = new GitHubApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.github.com/") }, new InMemoryFileSystem(), ghTokenResolver: () => null);
        var act = async () => await client.SendAsync(HttpMethod.Get, "repos/foo/bar");

        await act.Should().ThrowAsync<ConfigurationException>();
    }

    [Fact]
    public async Task SendAsync_FallsBackToGITHUB_TOKEN_WhenJCCMissing()
    {
        Environment.SetEnvironmentVariable("JCC_GITHUB_TOKEN", null);
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "fallback-token");
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

        await _client.SendAsync(HttpMethod.Get, "repos/foo/bar");

        _handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("fallback-token");
    }

    [Fact]
    public async Task SendAsync_QueryParameters_AppendedToUrl()
    {
        _handler.Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

        await _client.SendAsync(HttpMethod.Get, "repos/foo/bar/pulls", query: new Dictionary<string, string> { ["state"] = "open", ["limit"] = "10" });

        _handler.LastRequest!.RequestUri!.ToString().Should().Contain("state=open");
        _handler.LastRequest!.RequestUri!.ToString().Should().Contain("limit=10");
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public HttpResponseMessage Response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }
}
