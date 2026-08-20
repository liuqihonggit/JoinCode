namespace Guard.Tests.Configuration;

/// <summary>
/// DeepSeek 供应商定义单元测试 — 新架构下 deepseek 是 OpenAiCompatibleProviderDefinition 实例
/// 
/// 新架构要点:
/// - 不再有 DeepSeekProviderDefinition 独立类，deepseek 使用 OpenAiCompatibleProviderDefinition
/// - 端点从 settings.json 配置读取，不硬编码默认端点
/// - API Key 环境变量从 settings.json 的 apiKeyEnvVar 字段读取
/// - 模型列表从 ModelConfigLoader 读取（数据从 settings.json vendor.models 流入）
/// </summary>
public class DeepSeekProviderDefinitionTests : IDisposable
{
    private readonly ModelConfigLoader _modelConfigLoader;
    private readonly IProviderDefinition _definition;

    public DeepSeekProviderDefinitionTests()
    {
        _modelConfigLoader = new ModelConfigLoader();
        _modelConfigLoader.ApplyProviders(new Dictionary<string, ModelProviderConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["deepseek"] = new ModelProviderConfig
            {
                DefaultModelId = "deepseek-chat",
                DefaultFastModelId = "deepseek-chat",
                Models =
                [
                    new ModelItemConfig
                    {
                        Id = "deepseek-chat",
                        DisplayName = "DeepSeek Chat",
                        ContextWindow = 64000,
                        Description = "DeepSeek Chat",
                        Aliases = ["chat", "default"],
                        Capabilities = new ModelCapabilitiesConfig { FastMode = true, Modalities = ModelModalityKind.Text | ModelModalityKind.ToolUse }
                    },
                    new ModelItemConfig
                    {
                        Id = "deepseek-reasoner",
                        DisplayName = "DeepSeek Reasoner",
                        ContextWindow = 64000,
                        Description = "DeepSeek Reasoner",
                        Aliases = ["reasoner", "thinking"],
                        Capabilities = new ModelCapabilitiesConfig { ThinkingMode = true, Modalities = ModelModalityKind.Text | ModelModalityKind.Thinking | ModelModalityKind.ToolUse }
                    }
                ]
            }
        });

        _definition = new OpenAiCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    #region ProviderDefinition 属性验证

    [Fact]
    public void DeepSeek_Kind_ShouldBeDeepSeek()
    {
        _definition.Vendor.Should().Be(VendorKind.DeepSeek);
    }

    [Fact]
    public void DeepSeek_ProviderName_ShouldBeDeepSeek()
    {
        _definition.ProviderName.Should().Be("deepseek");
    }

    [Fact]
    public void DeepSeek_DisplayName_ShouldBeDeepSeek()
    {
        _definition.DisplayName.Should().Be("deepseek");
    }

    [Fact]
    public void DeepSeek_DefaultModelId_ShouldBeDeepSeekChat()
    {
        _definition.DefaultModelId.Should().Be("deepseek-chat");
    }

    [Fact]
    public void DeepSeek_DefaultFastModelId_ShouldBeDeepSeekChat()
    {
        _definition.DefaultFastModelId.Should().Be("deepseek-chat");
    }

    [Fact]
    public void DeepSeek_DefaultEndpoint_ShouldBeNull()
    {
        _definition.DefaultEndpoint.Should().BeNull("新架构下端点从配置读取，不硬编码");
    }

    [Fact]
    public void DeepSeek_ApiKeyEnvironmentVariable_ShouldBeDeepSeekApiKey()
    {
        _definition.ApiKeyEnvironmentVariable.Should().Be("DEEPSEEK_API_KEY");
    }

    [Fact]
    public void DeepSeek_AvailableModels_ShouldNotBeEmpty()
    {
        _definition.AvailableModels.Should().NotBeEmpty();
    }

    #endregion

    #region URL 构建验证

    [Fact]
    public void DeepSeek_GetBaseUrl_WithEndpoint_ShouldUseConfiguredEndpoint()
    {
        var config = new ProviderConfig { Endpoint = "https://api.deepseek.com" };

        var baseUrl = _definition.GetBaseUrl(config);

        baseUrl.Should().Be("https://api.deepseek.com/",
            "端点从 settings.json 配置读取");
    }

    [Fact]
    public void DeepSeek_GetBaseUrl_WithCustomEndpoint_ShouldUseCustomEndpoint()
    {
        var config = new ProviderConfig { Endpoint = "https://custom.deepseek.example.com" };

        var baseUrl = _definition.GetBaseUrl(config);

        baseUrl.Should().Be("https://custom.deepseek.example.com/");
    }

    [Fact]
    public void DeepSeek_GetBaseUrl_WithCustomEndpointTrailingSlash_ShouldNotDoubleSlash()
    {
        var config = new ProviderConfig { Endpoint = "https://custom.deepseek.example.com/" };

        var baseUrl = _definition.GetBaseUrl(config);

        baseUrl.Should().Be("https://custom.deepseek.example.com/");
    }

    [Fact]
    public void DeepSeek_GetChatEndpoint_ShouldReturnChatCompletionsRelativePath()
    {
        var config = new ProviderConfig { Endpoint = "https://api.deepseek.com" };

        var chatEndpoint = _definition.GetChatEndpoint(config);

        chatEndpoint.Should().Be("chat/completions");
    }

    [Fact]
    public void DeepSeek_GetChatEndpoint_WithEndpointContainingChatCompletions_ShouldReturnEmpty()
    {
        var config = new ProviderConfig { Endpoint = "https://api.deepseek.com/chat/completions" };

        var chatEndpoint = _definition.GetChatEndpoint(config);

        chatEndpoint.Should().BeEmpty();
    }

    [Fact]
    public void DeepSeek_FullUrl_Composition_ShouldBeDeepSeekApiChatCompletions()
    {
        var config = new ProviderConfig { Endpoint = "https://api.deepseek.com" };

        var baseUrl = _definition.GetBaseUrl(config);
        var chatEndpoint = _definition.GetChatEndpoint(config);

        var fullUrl = new Uri(new Uri(baseUrl), chatEndpoint).AbsoluteUri;

        fullUrl.Should().Be("https://api.deepseek.com/chat/completions");
    }

    [Fact]
    public void DeepSeek_GetChatEndpoint_ResponsesProtocol_ReturnsResponsesPath()
    {
        var config = new ProviderConfig
        {
            Endpoint = "https://api.deepseek.com",
            Protocol = "responses"
        };

        var chatEndpoint = _definition.GetChatEndpoint(config);

        chatEndpoint.Should().Be("responses");
    }

    [Fact]
    public void DeepSeek_FullUrl_ResponsesProtocol_ComposesResponsesEndpoint()
    {
        var config = new ProviderConfig
        {
            Endpoint = "https://api.deepseek.com",
            Protocol = "responses"
        };

        var baseUrl = _definition.GetBaseUrl(config);
        var chatEndpoint = _definition.GetChatEndpoint(config);

        var fullUrl = new Uri(new Uri(baseUrl), chatEndpoint).AbsoluteUri;

        fullUrl.Should().Be("https://api.deepseek.com/responses");
    }

    #endregion

    #region HttpClient 配置验证

    [Fact]
    public void DeepSeek_ConfigureHttpClient_ShouldAddBearerTokenAuthorizationHeader()
    {
        var config = new ProviderConfig { ApiKey = "sk-deepseek-test-key" };
        using var client = new HttpClient();

        _definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.TryGetValues("Authorization", out var values).Should().BeTrue();
        values!.First().Should().Be("Bearer sk-deepseek-test-key");
    }

    [Fact]
    public void DeepSeek_ConfigureHttpClient_WithEmptyApiKey_ShouldNotAddAuthorizationHeader()
    {
        var config = new ProviderConfig { ApiKey = "" };
        using var client = new HttpClient();

        _definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Contains("Authorization").Should().BeFalse();
    }

    #endregion

    #region API Key 解析验证

    [Fact]
    public void DeepSeek_ResolveApiKeyFromEnv_WithDeepSeekApiKey_ShouldReturnIt()
    {
        var oldValue = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "sk-from-deepseek-env");

            var apiKey = _definition.ResolveApiKeyFromEnv();

            apiKey.Should().Be("sk-from-deepseek-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldValue);
        }
    }

    [Fact]
    public void DeepSeek_ResolveApiKeyFromEnv_WithoutEnvVar_ShouldReturnNull()
    {
        var oldValue = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        var oldOpenAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);

            var apiKey = _definition.ResolveApiKeyFromEnv();

            apiKey.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldValue);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", oldOpenAiKey);
        }
    }

    #endregion

    #region 配置有效性验证

    [Fact]
    public void DeepSeek_IsValid_WithApiKey_ShouldReturnTrue()
    {
        var config = new ProviderConfig { ApiKey = "sk-test" };

        var isValid = _definition.IsValid(config);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void DeepSeek_IsValid_WithEmptyApiKey_ShouldReturnFalse()
    {
        var config = new ProviderConfig { ApiKey = "" };

        var isValid = _definition.IsValid(config);

        isValid.Should().BeFalse();
    }

    #endregion

    #region 模型别名验证

    [Fact]
    public void DeepSeek_ResolveAlias_KnownAlias_ShouldNotReturnNull()
    {
        var resolved = _definition.ResolveAlias("chat");

        resolved.Should().NotBeNull();
    }

    [Fact]
    public void DeepSeek_ResolveAlias_UnknownInput_ShouldReturnNull()
    {
        var resolved = _definition.ResolveAlias("unknown-model");

        resolved.Should().BeNull();
    }

    #endregion

    #region Registry 注册验证

    [Fact]
    public void OpenAiCompatibleProviderDefinition_CanBeConstructedForDeepSeek()
    {
        var definition = new OpenAiCompatibleProviderDefinition(_modelConfigLoader, "deepseek", "DEEPSEEK_API_KEY");

        definition.Should().NotBeNull();
        definition.ProviderName.Should().Be("deepseek");
        definition.ApiKeyEnvironmentVariable.Should().Be("DEEPSEEK_API_KEY");
    }

    [Fact]
    public void AnthropicProviderDefinition_CanBeConstructedForAnthropic()
    {
        var definition = new AnthropicCompatibleProviderDefinition(_modelConfigLoader, "anthropic", "ANTHROPIC_API_KEY");

        definition.Should().NotBeNull();
        definition.ProviderName.Should().Be("anthropic");
        definition.ApiKeyEnvironmentVariable.Should().Be("ANTHROPIC_API_KEY");
    }

    #endregion
}
