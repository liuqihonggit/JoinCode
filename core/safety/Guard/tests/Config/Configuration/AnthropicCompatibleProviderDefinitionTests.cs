namespace Guard.Tests.Configuration;

/// <summary>
/// AnthropicCompatibleProviderDefinition 单元测试 — 验证"Anthropic 协议通用化"架构决策
///
/// 核心诉求(配置大于代码):
/// - 任何供应商(含 DeepSeek)配 protocol:"anthropic" 即走 Anthropic 协议
/// - 供应商身份(Vendor)从 providerName 推导,不因协议改变而丢失
/// - 端点从 settings.json 配置读取,不硬编码默认端点(Anthropic 供应商除外,回退官方)
/// - API Key 环境变量从构造参数读取,不硬编码回退到 ANTHROPIC_API_KEY
/// - 认证头用 Anthropic 协议(x-api-key + anthropic-version),这是协议固有,与供应商无关
/// </summary>
public class AnthropicCompatibleProviderDefinitionTests : IDisposable
{
    private readonly ModelConfigLoader _modelConfigLoader;

    public AnthropicCompatibleProviderDefinitionTests()
    {
        _modelConfigLoader = new ModelConfigLoader();
        _modelConfigLoader.ApplyProviders(new Dictionary<string, ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["deepseek"] = new ModelProviderConfig
            {
                DefaultModelId = "deepseek-v4-pro",
                DefaultFastModelId = "deepseek-v4-flash",
                Models =
                [
                    new ModelItemConfig
                    {
                        Id = "deepseek-v4-pro",
                        DisplayName = "DeepSeek V4 Pro",
                        ContextWindow = 128000,
                        Description = "DeepSeek V4 Pro",
                        Aliases = ["pro", "default"],
                        Capabilities = new ModelCapabilitiesConfig { ThinkingMode = true, Modalities = ModelModalityKind.Text | ModelModalityKind.Thinking | ModelModalityKind.ToolUse }
                    }
                ]
            },
            ["anthropic"] = new ModelProviderConfig
            {
                DefaultModelId = "claude-sonnet-4-5",
                DefaultFastModelId = "claude-haiku-4-5",
                Models =
                [
                    new ModelItemConfig
                    {
                        Id = "claude-sonnet-4-5",
                        DisplayName = "Claude Sonnet 4.5",
                        ContextWindow = 200000,
                        Description = "Claude Sonnet 4.5",
                        Aliases = ["sonnet", "default"],
                        Capabilities = new ModelCapabilitiesConfig { ThinkingMode = true, Modalities = ModelModalityKind.Text | ModelModalityKind.Thinking | ModelModalityKind.ToolUse }
                    }
                ]
            }
        });
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    #region 供应商身份保持验证(核心:不硬编码 Anthropic)

    [Fact]
    public void ConstructedWithDeepSeek_Vendor_ShouldBeDeepSeek_NotAnthropic()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");

        definition.Vendor.Should().Be(VendorKind.DeepSeek,
            "DeepSeek 用 Anthropic 协议时,供应商身份必须保持 DeepSeek,不因协议改变而丢失");
    }

    [Fact]
    public void ConstructedWithDeepSeek_DisplayName_ShouldBeDeepSeek()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");

        definition.DisplayName.Should().Be("deepseek",
            "显示名从 providerName 推导,不硬编码 Anthropic");
    }

    [Fact]
    public void ConstructedWithDeepSeek_ProviderName_ShouldBeDeepSeek()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");

        definition.ProviderName.Should().Be("deepseek");
    }

    [Fact]
    public void ConstructedWithAnthropic_Vendor_ShouldBeAnthropic()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "anthropic", "ANTHROPIC_API_KEY");

        definition.Vendor.Should().Be(VendorKind.Anthropic,
            "Anthropic 供应商本身仍正确识别");
    }

    [Fact]
    public void ConstructedWithDeepSeek_Protocol_ShouldBeAnthropic()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");

        definition.Protocol.Should().Be(ProtocolKind.Anthropic,
            "协议始终是 Anthropic,这是此类存在的意义");
    }

    #endregion

    #region 端点配置验证(配置大于代码)

    [Fact]
    public void GetBaseUrl_WithEndpoint_ShouldUseConfiguredEndpoint()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { Endpoint = "https://api.deepseek.com/anthropic" };

        var baseUrl = definition.GetBaseUrl(config);

        baseUrl.Should().Be("https://api.deepseek.com/anthropic/",
            "端点从 settings.json 配置读取,配置大于代码");
    }

    [Fact]
    public void GetBaseUrl_WithCustomEndpoint_ShouldUseCustomEndpoint()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { Endpoint = "https://custom.example.com/anthropic" };

        var baseUrl = definition.GetBaseUrl(config);

        baseUrl.Should().Be("https://custom.example.com/anthropic/");
    }

    [Fact]
    public void GetBaseUrl_WithoutEndpoint_ForAnthropic_ShouldReturnAnthropicOfficial()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "anthropic", "ANTHROPIC_API_KEY");
        var config = new ProviderConfig { Endpoint = null };

        var baseUrl = definition.GetBaseUrl(config);

        baseUrl.Should().Be("https://api.anthropic.com/",
            "Anthropic 供应商本身未配端点时回退到官方地址");
    }

    [Fact]
    public void GetBaseUrl_WithoutEndpoint_ForDeepSeek_ShouldThrow()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { Endpoint = null };

        var act = () => definition.GetBaseUrl(config);

        act.Should().Throw<InvalidOperationException>(
            "非 Anthropic 供应商用 Anthropic 协议时必须显式配置 endpoint,避免静默错发到 api.anthropic.com");
    }

    [Fact]
    public void GetChatEndpoint_ShouldReturnV1Messages()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { Endpoint = "https://api.deepseek.com/anthropic" };

        var chatEndpoint = definition.GetChatEndpoint(config);

        chatEndpoint.Should().Be("v1/messages",
            "Anthropic 协议端点路径固定为 v1/messages");
    }

    [Fact]
    public void FullUrl_Composition_ForDeepSeekAnthropicProtocol_ShouldBeDeepSeekAnthropicMessages()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { Endpoint = "https://api.deepseek.com/anthropic" };

        var baseUrl = definition.GetBaseUrl(config);
        var chatEndpoint = definition.GetChatEndpoint(config);
        var fullUrl = new Uri(new Uri(baseUrl), chatEndpoint).AbsoluteUri;

        fullUrl.Should().Be("https://api.deepseek.com/anthropic/v1/messages",
            "DeepSeek 走 Anthropic 协议的完整 URL");
    }

    #endregion

    #region HttpClient 配置验证(Anthropic 协议认证头)

    [Fact]
    public void ConfigureHttpClient_ShouldAddXApiKeyHeader()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { ApiKey = "sk-deepseek-test" };
        using var client = new HttpClient();

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.TryGetValues("x-api-key", out var values).Should().BeTrue();
        values!.First().Should().Be("sk-deepseek-test",
            "Anthropic 协议用 x-api-key 认证,与供应商无关");
    }

    [Fact]
    public void ConfigureHttpClient_ShouldAddAnthropicVersionHeader()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { ApiKey = "sk-test" };
        using var client = new HttpClient();

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.TryGetValues("anthropic-version", out var values).Should().BeTrue();
        values!.First().Should().NotBeNullOrEmpty(
            "anthropic-version 是 Anthropic 协议固有头");
    }

    [Fact]
    public void ConfigureHttpClient_WithEmptyApiKey_ShouldNotAddHeaders()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { ApiKey = "" };
        using var client = new HttpClient();

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Contains("x-api-key").Should().BeFalse();
    }

    #endregion

    #region API Key 解析验证(不硬编码回退 ANTHROPIC_API_KEY)

    [Fact]
    public void ApiKeyEnvironmentVariable_ShouldBeFromConstructor()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");

        definition.ApiKeyEnvironmentVariable.Should().Be("DEEPSEEK_API_KEY",
            "环境变量名从构造参数读取,不硬编码回退到 ANTHROPIC_API_KEY");
    }

    [Fact]
    public void ResolveApiKeyFromEnv_WithCustomEnvVar_ShouldReturnIt()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var oldValue = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "sk-from-deepseek-env");

            var apiKey = definition.ResolveApiKeyFromEnv();

            apiKey.Should().Be("sk-from-deepseek-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldValue);
        }
    }

    [Fact]
    public void ResolveApiKeyFromEnv_WithoutEnvVar_ShouldReturnNull()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var oldValue = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        var oldAnthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);

            var apiKey = definition.ResolveApiKeyFromEnv();

            apiKey.Should().BeNull(
                "DeepSeek 的 key 未设时不应回退到 ANTHROPIC_API_KEY");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldValue);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", oldAnthropicKey);
        }
    }

    #endregion

    #region 模型能力验证(从 providerName 查询,不硬编码)

    [Fact]
    public void ConstructedWithDeepSeek_DefaultModelId_ShouldBeDeepSeekV4Pro()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");

        definition.DefaultModelId.Should().Be("deepseek-v4-pro",
            "模型从 providerName=deepseek 查询,不因协议改变而读 anthropic 的模型");
    }

    [Fact]
    public void ConstructedWithDeepSeek_AvailableModels_ShouldNotBeEmpty()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");

        definition.AvailableModels.Should().NotBeEmpty();
    }

    [Fact]
    public void ConstructedWithDeepSeek_SupportsThinkingMode_ShouldReturnTrue()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");

        definition.SupportsThinkingMode("deepseek-v4-pro").Should().BeTrue();
    }

    #endregion

    #region 配置有效性验证

    [Fact]
    public void IsValid_WithApiKey_ShouldReturnTrue()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { ApiKey = "sk-test" };

        definition.IsValid(config).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithEmptyApiKey_ShouldReturnFalse()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
        var config = new ProviderConfig { ApiKey = "" };

        definition.IsValid(config).Should().BeFalse();
    }

    #endregion
}
