namespace Core.Configuration.ModelFetch.Tests;

/// <summary>
/// ModelListFetcher 单元测试 — 验证并行拉取、跳过逻辑、认证分派、失败容错、auth.json 读取
/// </summary>
public class ModelListFetcherTests
{
    private const string TestApiKeyEnv = "TEST_MODEL_FETCH_KEY";

    [Fact]
    public async Task FetchAllAsync_ValidVendor_ReturnsModelIds()
    {
        using var env = new EnvScope(TestApiKeyEnv, "sk-test");
        var vendor = new Dictionary<string, ProfileSettings>
        {
            ["openai"] = new() { Provider = "openai", Protocol = "openai-compatible", Endpoint = "https://api.openai.com/v1", ModelsEndpoint = "models", ApiKeyEnvVar = TestApiKeyEnv }
        };

        var handler = new LambdaHandler(_ => Ok("""{"data":[{"id":"gpt-4o"},{"id":"gpt-5"}]}"""));
        var fetcher = new ModelListFetcher(CreateProvider(handler), CreateFileSystem());

        var result = await fetcher.FetchAllAsync(vendor);

        result.Should().ContainKey("openai");
        result["openai"].Should().Equal(["gpt-4o", "gpt-5"]);
    }

    [Fact]
    public async Task FetchAllAsync_EmptyEndpoint_SkipsProvider()
    {
        using var env = new EnvScope(TestApiKeyEnv, "sk-test");
        var vendor = new Dictionary<string, ProfileSettings>
        {
            ["no-endpoint"] = new() { Provider = "test", Protocol = "openai-compatible", Endpoint = null, ModelsEndpoint = "models", ApiKeyEnvVar = TestApiKeyEnv }
        };

        var fetcher = new ModelListFetcher(CreateProvider(new LambdaHandler(_ => Ok("{}"))), CreateFileSystem());

        var result = await fetcher.FetchAllAsync(vendor);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAllAsync_EmptyModelsEndpoint_SkipsProvider()
    {
        using var env = new EnvScope(TestApiKeyEnv, "sk-test");
        var vendor = new Dictionary<string, ProfileSettings>
        {
            ["no-models-endpoint"] = new() { Provider = "test", Protocol = "openai-compatible", Endpoint = "https://example.com", ModelsEndpoint = null, ApiKeyEnvVar = TestApiKeyEnv }
        };

        var fetcher = new ModelListFetcher(CreateProvider(new LambdaHandler(_ => Ok("{}"))), CreateFileSystem());

        var result = await fetcher.FetchAllAsync(vendor);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAllAsync_NoApiKey_SkipsProvider()
    {
        using var env = new EnvScope(TestApiKeyEnv, null);
        var vendor = new Dictionary<string, ProfileSettings>
        {
            ["no-key"] = new() { Provider = "test", Protocol = "openai-compatible", Endpoint = "https://example.com", ModelsEndpoint = "models", ApiKeyEnvVar = TestApiKeyEnv }
        };

        var fetcher = new ModelListFetcher(CreateProvider(new LambdaHandler(_ => Ok("{}"))), CreateFileSystem());

        var result = await fetcher.FetchAllAsync(vendor);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAllAsync_HttpError_SkipsProvider()
    {
        using var env = new EnvScope(TestApiKeyEnv, "sk-test");
        var vendor = new Dictionary<string, ProfileSettings>
        {
            ["error"] = new() { Provider = "test", Protocol = "openai-compatible", Endpoint = "https://example.com", ModelsEndpoint = "models", ApiKeyEnvVar = TestApiKeyEnv }
        };

        var handler = new LambdaHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var fetcher = new ModelListFetcher(CreateProvider(handler), CreateFileSystem());

        var result = await fetcher.FetchAllAsync(vendor);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAllAsync_AnthropicProtocol_UsesXApiKeyHeader()
    {
        using var env = new EnvScope(TestApiKeyEnv, "sk-anthropic");
        var vendor = new Dictionary<string, ProfileSettings>
        {
            ["anthropic"] = new() { Provider = "anthropic", Protocol = "anthropic", Endpoint = "https://api.anthropic.com", ModelsEndpoint = "v1/models", ApiKeyEnvVar = TestApiKeyEnv }
        };

        HttpRequestMessage? captured = null;
        var handler = new LambdaHandler(req => { captured = req; return Ok("""{"data":[{"id":"claude-sonnet-4-6"}]}"""); });
        var fetcher = new ModelListFetcher(CreateProvider(handler), CreateFileSystem());

        var result = await fetcher.FetchAllAsync(vendor);

        result.Should().ContainKey("anthropic");
        captured.Should().NotBeNull();
        captured!.Headers.Contains("x-api-key").Should().BeTrue();
        captured.Headers.Contains("Authorization").Should().BeFalse();
    }

    [Fact]
    public async Task FetchAllAsync_OpenAiProtocol_UsesBearerAuth()
    {
        using var env = new EnvScope(TestApiKeyEnv, "sk-openai");
        var vendor = new Dictionary<string, ProfileSettings>
        {
            ["openai"] = new() { Provider = "openai", Protocol = "openai-compatible", Endpoint = "https://api.openai.com/v1", ModelsEndpoint = "models", ApiKeyEnvVar = TestApiKeyEnv }
        };

        HttpRequestMessage? captured = null;
        var handler = new LambdaHandler(req => { captured = req; return Ok("""{"data":[{"id":"gpt-4o"}]}"""); });
        var fetcher = new ModelListFetcher(CreateProvider(handler), CreateFileSystem());

        var result = await fetcher.FetchAllAsync(vendor);

        result.Should().ContainKey("openai");
        captured.Should().NotBeNull();
        captured!.Headers.Contains("Authorization").Should().BeTrue();
        captured.Headers.Contains("x-api-key").Should().BeFalse();
    }

    [Fact]
    public async Task FetchAllAsync_MultipleProviders_ParallelFetch()
    {
        using var env = new EnvScope(TestApiKeyEnv, "sk-test");
        var vendor = new Dictionary<string, ProfileSettings>
        {
            ["openai"] = new() { Provider = "openai", Protocol = "openai-compatible", Endpoint = "https://api.openai.com/v1", ModelsEndpoint = "models", ApiKeyEnvVar = TestApiKeyEnv },
            ["agnes"] = new() { Provider = "agnes", Protocol = "openai-compatible", Endpoint = "https://apihub.agnes-ai.com/v1", ModelsEndpoint = "models", ApiKeyEnvVar = TestApiKeyEnv }
        };

        var handler = new LambdaHandler(req => req.RequestUri!.ToString().Contains("openai")
            ? Ok("""{"data":[{"id":"gpt-4o"}]}""")
            : Ok("""{"data":[{"id":"agnes-2.0-flash"}]}"""));
        var fetcher = new ModelListFetcher(CreateProvider(handler), CreateFileSystem());

        var result = await fetcher.FetchAllAsync(vendor);

        result.Should().HaveCount(2);
        result["openai"].Should().Equal(["gpt-4o"]);
        result["agnes"].Should().Equal(["agnes-2.0-flash"]);
    }

    [Fact]
    public async Task FetchAllAsync_AuthJsonApiKey_UsedWhenEnvVarMissing()
    {
        using var env = new EnvScope(TestApiKeyEnv, null);
        var vendor = new Dictionary<string, ProfileSettings>
        {
            ["sensenova"] = new() { Provider = "sensenova", Protocol = "openai-compatible", Endpoint = "https://token.sensenova.cn/v1", ModelsEndpoint = "models", ApiKeyEnvVar = TestApiKeyEnv }
        };

        var authJson = """{"sensenova":"sk-from-auth-json"}""";
        var handler = new LambdaHandler(_ => Ok("""{"data":[{"id":"sensenova-6.7"}]}"""));
        var fetcher = new ModelListFetcher(CreateProvider(handler), CreateFileSystem(authExists: true, authContent: authJson));

        var result = await fetcher.FetchAllAsync(vendor);

        result.Should().ContainKey("sensenova");
        result["sensenova"].Should().Equal(["sensenova-6.7"]);
    }

    private static IHttpClientProvider CreateProvider(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var mock = new Mock<IHttpClientProvider>();
        mock.Setup(x => x.GetClient()).Returns(client);
        return mock.Object;
    }

    private static IFileSystem CreateFileSystem(bool authExists = false, string? authContent = null)
    {
        var mock = new Mock<IFileSystem>();
        mock.Setup(x => x.FileExists(It.IsAny<string>())).Returns(authExists);
        if (authExists && authContent is not null)
            mock.Setup(x => x.ReadAllTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(authContent);
        return mock.Object;
    }

    private static HttpResponseMessage Ok(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private sealed class LambdaHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public LambdaHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly string _var;
        public EnvScope(string var, string? value)
        {
            _var = var;
            Environment.SetEnvironmentVariable(var, value);
        }
        public void Dispose() => Environment.SetEnvironmentVariable(_var, null);
    }
}
