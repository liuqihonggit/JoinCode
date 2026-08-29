namespace Llm.Tests.Adapters.LLM.QueryServices;

public sealed class QueryServiceFactoryTests
{
    private readonly QueryServiceFactory _factory = new();

    [Fact]
    public void Create_NullConfig_ThrowsArgumentNullException()
    {
        var act = () => _factory.Create(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Theory]
    [InlineData("openai", "openai-compatible", typeof(OpenAIQueryService))]
    [InlineData("azure", "azure", typeof(AzureQueryService))]
    [InlineData("anthropic", "anthropic", typeof(AnthropicQueryService))]
    [InlineData("agnes", "agnes", typeof(AgnesQueryService))]
    [InlineData("deepseek", "openai-compatible", typeof(OpenAIQueryService))]
    [InlineData("deepseek", "responses", typeof(ResponsesQueryService))]
    [InlineData("openai", "responses", typeof(ResponsesQueryService))]
    [InlineData("unknown", "openai-compatible", typeof(OpenAIQueryService))]
    public void Create_WithProviderKind_ReturnsExpectedType(string provider, string protocol, Type expectedType)
    {
        // Azure 需要 Endpoint + ModelId 才能构造合法 URL，其他 provider 忽略这两个字段
        var config = new ProviderConfig
        {
            Vendor = provider,
            Protocol = protocol,
            ApiKey = "sk-test",
            Endpoint = "https://test.openai.azure.com",
            ModelId = "gpt-4o"
        };

        var service = _factory.Create(config);

        service.Should().BeOfType(expectedType);
    }

    [Fact]
    public void Create_WithoutDefinition_InjectFallbackDefinition()
    {
        var config = new ProviderConfig { Vendor = "openai", ApiKey = "sk-test", Definition = null };

        _factory.Create(config);

        config.Definition.Should().NotBeNull();
        config.Definition.Should().BeOfType<FallbackProviderDefinition>();
    }

    [Fact]
    public void Create_WithExistingDefinition_PreservesDefinition()
    {
        var definition = new Mock<IProviderDefinition>();
        definition.Setup(d => d.GetBaseUrl(It.IsAny<ProviderConfig>())).Returns("https://api.example.com/");
        var config = new ProviderConfig { Vendor = "openai", ApiKey = "sk-test", Definition = definition.Object };

        _factory.Create(config);

        config.Definition.Should().BeSameAs(definition.Object);
    }

    [Fact]
    public void Create_PassesDependenciesToService()
    {
        var config = new ProviderConfig { Vendor = "openai", ApiKey = "sk-test" };
        using var httpClient = new HttpClient();
        var logger = new Mock<ILogger>().Object;

        var service = _factory.Create(config, httpClient, logger, null, null);

        service.Should().BeOfType<OpenAIQueryService>();
    }

    [Fact]
    public void Create_AsInterfaceFactory_ResolvesDependencies()
    {
        var config = new ProviderConfig { Vendor = "openai", ApiKey = "sk-test" };

        var service = ((IQueryServiceFactory)_factory).Create(config, null, null, null);

        service.Should().BeOfType<OpenAIQueryService>();
    }
}
