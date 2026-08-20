namespace Guard.Tests.Configuration;

/// <summary>
/// ProviderDefinitionRegistry 协议分派测试 — 验证"配置大于代码"原则
///
/// 核心诉求:
/// - settings.json 配 protocol:"anthropic" 即走 AnthropicCompatibleProviderDefinition(新通用类)
/// - 供应商身份从 providerName 推导,DeepSeek 走 Anthropic 协议时 Vendor 仍为 DeepSeek
/// - anthropic-beta 头可配置:配了就发,DeepSeek 未配不发(安全),Anthropic 未配发默认(兼容)
/// - openai-compatible 协议路径不变(不破坏现有)
/// </summary>
public class ProviderDefinitionRegistryProtocolDispatchTests
{
    private static IFileSystem CreateFs(string json)
    {
        var mock = new Mock<IFileSystem>();
        mock.Setup(x => x.FileExists(It.IsAny<string>())).Returns(true);
        mock.Setup(x => x.ReadAllText(It.IsAny<string>())).Returns(json);
        return mock.Object;
    }

    private static ModelConfigLoader CreateLoader()
    {
        var loader = new ModelConfigLoader();
        loader.ApplyProviders(new Dictionary<string, ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["deepseek"] = new ModelProviderConfig
            {
                DefaultModelId = "deepseek-v4-pro",
                Models = [new ModelItemConfig { Id = "deepseek-v4-pro", DisplayName = "DeepSeek V4 Pro", ContextWindow = 128000 }]
            },
            ["anthropic"] = new ModelProviderConfig
            {
                DefaultModelId = "claude-sonnet-4-5",
                Models = [new ModelItemConfig { Id = "claude-sonnet-4-5", DisplayName = "Claude Sonnet 4.5", ContextWindow = 200000 }]
            }
        });
        return loader;
    }

    #region 分派到新通用类验证

    [Fact]
    public void DeepSeek_WithAnthropicProtocol_ShouldDispatchToAnthropicCompatible_VendorPreserved()
    {
        var json = """{"vendor":{"deepseek":{"protocol":"anthropic","endpoint":"https://api.deepseek.com/anthropic","apiKeyEnvVar":"DEEPSEEK_API_KEY"}}}""";
        var registry = new ProviderDefinitionRegistry(CreateLoader(), CreateFs(json));

        var def = registry.TryGet("deepseek");

        def.Should().NotBeNull();
        def!.Vendor.Should().Be(VendorKind.DeepSeek,
            "DeepSeek 走 Anthropic 协议时供应商身份保持,不因协议改变丢失(旧 AnthropicProviderDefinition 硬编码 Anthropic,此测试区分新旧)");
        def.Protocol.Should().Be(ProtocolKind.Anthropic);
        def.ProviderName.Should().Be("deepseek");
    }

    [Fact]
    public void DeepSeek_WithAnthropicProtocol_EndpointShouldBeFromConfig()
    {
        var json = """{"vendor":{"deepseek":{"protocol":"anthropic","endpoint":"https://api.deepseek.com/anthropic","apiKeyEnvVar":"DEEPSEEK_API_KEY"}}}""";
        var registry = new ProviderDefinitionRegistry(CreateLoader(), CreateFs(json));

        var def = registry.TryGet("deepseek")!;
        var baseUrl = def.GetBaseUrl(new ProviderConfig { Endpoint = "https://api.deepseek.com/anthropic" });

        baseUrl.Should().Be("https://api.deepseek.com/anthropic/");
    }

    #endregion

    #region anthropic-beta 头可配置验证(配置大于代码)

    [Fact]
    public void DeepSeek_WithAnthropicProtocol_AndAnthropicBetaConfigured_ShouldSendBetaHeader()
    {
        var json = """{"vendor":{"deepseek":{"protocol":"anthropic","endpoint":"https://api.deepseek.com/anthropic","apiKeyEnvVar":"DEEPSEEK_API_KEY","anthropicBeta":"prompt-caching-2024-07-31"}}}""";
        var registry = new ProviderDefinitionRegistry(CreateLoader(), CreateFs(json));

        var def = registry.TryGet("deepseek")!;
        using var client = new HttpClient();
        def.ConfigureHttpClient(client, new ProviderConfig { ApiKey = "sk-test" });

        client.DefaultRequestHeaders.TryGetValues("anthropic-beta", out var values).Should().BeTrue();
        values!.First().Should().Be("prompt-caching-2024-07-31",
            "用户配置的 anthropicBeta 原样发送,配置大于代码");
    }

    [Fact]
    public void DeepSeek_WithAnthropicProtocol_WithoutAnthropicBeta_ShouldNotSendBetaHeader()
    {
        var json = """{"vendor":{"deepseek":{"protocol":"anthropic","endpoint":"https://api.deepseek.com/anthropic","apiKeyEnvVar":"DEEPSEEK_API_KEY"}}}""";
        var registry = new ProviderDefinitionRegistry(CreateLoader(), CreateFs(json));

        var def = registry.TryGet("deepseek")!;
        using var client = new HttpClient();
        def.ConfigureHttpClient(client, new ProviderConfig { ApiKey = "sk-test" });

        client.DefaultRequestHeaders.Contains("anthropic-beta").Should().BeFalse(
            "DeepSeek 未配 beta 时不发,避免不支持的 Anthropic beta 特性导致 400");
    }

    [Fact]
    public void Anthropic_WithAnthropicProtocol_WithoutBetaConfig_ShouldSendDefaultBetaHeader()
    {
        var json = """{"vendor":{"anthropic":{"protocol":"anthropic","apiKeyEnvVar":"ANTHROPIC_API_KEY"}}}""";
        var registry = new ProviderDefinitionRegistry(CreateLoader(), CreateFs(json));

        var def = registry.TryGet("anthropic")!;
        using var client = new HttpClient();
        def.ConfigureHttpClient(client, new ProviderConfig { ApiKey = "sk-test" });

        client.DefaultRequestHeaders.TryGetValues("anthropic-beta", out var values).Should().BeTrue(
            "Anthropic 供应商未配 beta 时回退到默认 beta 串,保持 prompt caching 等特性兼容");
        values!.First().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Anthropic_WithAnthropicProtocol_AndBetaConfigured_ShouldUseConfiguredBeta()
    {
        var json = """{"vendor":{"anthropic":{"protocol":"anthropic","apiKeyEnvVar":"ANTHROPIC_API_KEY","anthropicBeta":"custom-beta-feature"}}}""";
        var registry = new ProviderDefinitionRegistry(CreateLoader(), CreateFs(json));

        var def = registry.TryGet("anthropic")!;
        using var client = new HttpClient();
        def.ConfigureHttpClient(client, new ProviderConfig { ApiKey = "sk-test" });

        client.DefaultRequestHeaders.TryGetValues("anthropic-beta", out var values).Should().BeTrue();
        values!.First().Should().Be("custom-beta-feature",
            "Anthropic 供应商配了 beta 时用配置值,覆盖默认");
    }

    #endregion

    #region OpenAI 兼容协议路径不破坏验证

    [Fact]
    public void DeepSeek_WithOpenAiCompatibleProtocol_ShouldStillDispatchToOpenAiCompatible()
    {
        var json = """{"vendor":{"deepseek":{"protocol":"openai-compatible","apiKeyEnvVar":"DEEPSEEK_API_KEY"}}}""";
        var registry = new ProviderDefinitionRegistry(CreateLoader(), CreateFs(json));

        var def = registry.TryGet("deepseek")!;

        def.Vendor.Should().Be(VendorKind.DeepSeek);
        def.Protocol.Should().Be(ProtocolKind.OpenAiCompatible,
            "OpenAI 兼容协议路径不变,不破坏现有行为");
    }

    [Fact]
    public void Anthropic_WithOpenAiCompatibleProtocol_ShouldDispatchToOpenAiCompatible()
    {
        var json = """{"vendor":{"anthropic":{"protocol":"openai-compatible","apiKeyEnvVar":"ANTHROPIC_API_KEY"}}}""";
        var registry = new ProviderDefinitionRegistry(CreateLoader(), CreateFs(json));

        var def = registry.TryGet("anthropic")!;

        def.Protocol.Should().Be(ProtocolKind.OpenAiCompatible,
            "Anthropic 供应商也可配 openai-compatible 协议(配置大于代码)");
    }

    #endregion
}
