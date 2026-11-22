namespace Llm.Tests.Adapters.LLM.QueryServices;

/// <summary>
/// Jev QueryServiceFactory 分派测试 — 验证 ProtocolKind.Jev 正确路由到 JevQueryService
/// </summary>
public sealed class JevQueryServiceFactoryTests {
    private readonly QueryServiceFactory _factory = new();

    public JevQueryServiceFactoryTests() {
        _factory.RegisterProvider(ProtocolKind.Jev,
            (config, http, logger, fs, executor) => new JevQueryService(config, http, logger, fs, executor));
        _factory.RegisterDefault(
            (config, http, logger, fs, executor) => new OpenAIQueryService(config, http, logger, fs, executor));
    }

    [Fact]
    public void Create_WithJevProtocol_ReturnsJevQueryService() {
        var config = new ProviderConfig {
            Vendor = "jev",
            Protocol = "jev",
            ApiKey = "jev-test-key",
            ModelId = "jev-latest"
        };

        var service = _factory.Create(config);

        service.Should().BeOfType<JevQueryService>();
    }

    [Fact]
    public void Create_WithJevProtocol_ShouldImplementITypedDecisionService() {
        var config = new ProviderConfig {
            Vendor = "jev",
            Protocol = "jev",
            ApiKey = "jev-test-key",
            ModelId = "jev-latest"
        };

        var service = _factory.Create(config);

        service.Should().BeAssignableTo<ITypedDecisionService>();
    }

    [Fact]
    public void Create_WithJevProtocolAndCustomEndpoint_ReturnsJevQueryService() {
        var config = new ProviderConfig {
            Vendor = "jev",
            Protocol = "jev",
            ApiKey = "jev-test-key",
            ModelId = "jev-latest",
            Endpoint = "https://custom.typesafe.ai/v1/systemone"
        };

        var service = _factory.Create(config);

        service.Should().BeOfType<JevQueryService>();
    }
}
