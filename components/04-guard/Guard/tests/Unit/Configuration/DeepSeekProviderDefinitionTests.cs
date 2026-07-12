namespace Guard.Tests.Configuration;

/// <summary>
/// DeepSeek Provider 定义单元测试 — TDD 红绿循环驱动 P4-1 修复
///
/// 背景: P4-1 发现 jcc.exe 不支持 JCC_PROVIDER=deepseek，报错"未知的 Provider 'deepseek'"。
/// 修复目标: 新增 ProviderKind.DeepSeek + DeepSeekProviderDefinition + ProviderDefinitionRegistry 注册。
///
/// DeepSeek 协议特性:
/// - OpenAI 兼容协议（chat/completions 端点 + Bearer Token）
/// - 默认端点: https://api.deepseek.com（无 /v1 前缀，DeepSeek 官方端点）
/// - 默认模型: deepseek-chat（V3）；快速模型: deepseek-chat
/// - 缓存统计字段: prompt_cache_hit_tokens + prompt_cache_miss_tokens
///   （OpenAIQueryService 已支持解析，无需修改 QueryService 层）
/// - API Key 环境变量: DEEPSEEK_API_KEY，回退到 JCC_API_KEY
/// </summary>
public class DeepSeekProviderDefinitionTests
{
    private static readonly Core.Configuration.Providers.ProviderDefinitionRegistry Registry = new();

    #region ProviderKind 枚举验证

    [Fact]
    public void ProviderKind_DeepSeek_ToValue_ShouldReturnDeepSeekString()
    {
        // Act
        var value = ProviderKind.DeepSeek.ToValue();

        // Then
        value.Should().Be("deepseek");
    }

    [Fact]
    public void ProviderKindExtensions_FromDeepSeekString_ShouldReturnDeepSeekEnum()
    {
        // Act
        var kind = ProviderKindExtensions.FromValue("deepseek");

        // Then
        kind.Should().Be(ProviderKind.DeepSeek);
    }

    [Fact]
    public void ProviderKindExtensions_FromDeepSeekString_UpperCase_ShouldReturnDeepSeekEnum()
    {
        // Act — 大小写不敏感
        var kind = ProviderKindExtensions.FromValue("DeepSeek");

        // Then
        kind.Should().Be(ProviderKind.DeepSeek);
    }

    #endregion

    #region ProviderDefinitionRegistry 注册验证

    private IProviderDefinition? GetDeepSeekDefinition()
        => Registry.TryGet("deepseek");

    [Fact]
    public void ProviderDefinitionRegistry_TryGet_DeepSeek_ShouldReturnNonNullOrchestrator()
    {
        // Act
        var definition = Registry.TryGet("deepseek");

        // Then
        definition.Should().NotBeNull("JCC_PROVIDER=deepseek 时应能从注册表解析到 DeepSeekProviderDefinition");
    }

    [Fact]
    public void ProviderDefinitionRegistry_RegisteredProviders_ShouldIncludeDeepSeek()
    {
        // Act
        var providers = Registry.RegisteredProviders;

        // Then
        providers.Should().Contain("deepseek",
            "注册表必须包含 deepseek，否则 JCC_PROVIDER=deepseek 会抛 ConfigurationException");
    }

    #endregion

    #region DeepSeekProviderDefinition 属性验证

    [Fact]
    public void DeepSeekProviderDefinition_Kind_ShouldBeDeepSeek()
    {
        // Act
        var definition = GetDeepSeekDefinition();

        // Then
        definition!.Kind.Should().Be(ProviderKind.DeepSeek);
    }

    [Fact]
    public void DeepSeekProviderDefinition_ProviderName_ShouldBeDeepSeek()
    {
        var definition = GetDeepSeekDefinition();

        definition!.ProviderName.Should().Be("deepseek");
    }

    [Fact]
    public void DeepSeekProviderDefinition_DisplayName_ShouldBeDeepSeek()
    {
        var definition = GetDeepSeekDefinition();

        definition!.DisplayName.Should().Be("DeepSeek");
    }

    [Fact]
    public void DeepSeekProviderDefinition_DefaultModelId_ShouldBeDeepSeekChat()
    {
        var definition = GetDeepSeekDefinition();

        // DeepSeek V3 系列默认模型
        definition!.DefaultModelId.Should().Be("deepseek-chat");
    }

    [Fact]
    public void DeepSeekProviderDefinition_DefaultFastModelId_ShouldBeDeepSeekChat()
    {
        var definition = GetDeepSeekDefinition();

        // DeepSeek 没有独立的快速模型，复用 deepseek-chat
        definition!.DefaultFastModelId.Should().Be("deepseek-chat");
    }

    [Fact]
    public void DeepSeekProviderDefinition_DefaultEndpoint_ShouldBeDeepSeekApiUrl()
    {
        var definition = GetDeepSeekDefinition();

        // DeepSeek 官方端点（无 /v1 前缀）
        definition!.DefaultEndpoint.Should().Be("https://api.deepseek.com");
    }

    [Fact]
    public void DeepSeekProviderDefinition_ApiKeyEnvironmentVariable_ShouldBeDeepSeekApiKey()
    {
        var definition = GetDeepSeekDefinition();

        definition!.ApiKeyEnvironmentVariable.Should().Be("DEEPSEEK_API_KEY");
    }

    [Fact]
    public void DeepSeekProviderDefinition_AvailableModels_ShouldIncludeDeepSeekChatAndReasoner()
    {
        var definition = GetDeepSeekDefinition();

        // Then — 至少包含 deepseek-chat (V3) 和 deepseek-reasoner (R1)
        var modelIds = definition!.AvailableModels.Select(m => m.Id).ToList();
        modelIds.Should().Contain("deepseek-chat");
        modelIds.Should().Contain("deepseek-reasoner");
    }

    #endregion

    #region URL 构建验证（端点路径差异处理）

    [Fact]
    public void DeepSeekProviderDefinition_GetBaseUrl_WithNullEndpoint_ShouldReturnDeepSeekApiUrlWithTrailingSlash()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { Endpoint = null };

        // Act — DeepSeek 官方端点（无 /v1 前缀，与 OpenAI 不同）
        var baseUrl = definition!.GetBaseUrl(config);

        // Then
        baseUrl.Should().Be("https://api.deepseek.com/",
            "DeepSeek 官方端点无 /v1 前缀，与 OpenAI 的 https://api.openai.com/v1/ 不同");
    }

    [Fact]
    public void DeepSeekProviderDefinition_GetBaseUrl_WithCustomEndpoint_ShouldUseCustomEndpointWithTrailingSlash()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { Endpoint = "https://custom.deepseek.example.com" };

        var baseUrl = definition!.GetBaseUrl(config);

        baseUrl.Should().Be("https://custom.deepseek.example.com/");
    }

    [Fact]
    public void DeepSeekProviderDefinition_GetBaseUrl_WithCustomEndpointTrailingSlash_ShouldNotDoubleSlash()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { Endpoint = "https://custom.deepseek.example.com/" };

        var baseUrl = definition!.GetBaseUrl(config);

        baseUrl.Should().Be("https://custom.deepseek.example.com/");
    }

    [Fact]
    public void DeepSeekProviderDefinition_GetChatEndpoint_ShouldReturnChatCompletionsRelativePath()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { Endpoint = null };

        // Act — OpenAI 兼容协议的相对路径
        var chatEndpoint = definition!.GetChatEndpoint(config);

        // Then — 与 OpenAI 一致的相对路径
        chatEndpoint.Should().Be("chat/completions");
    }

    [Fact]
    public void DeepSeekProviderDefinition_GetChatEndpoint_WithEndpointContainingChatCompletions_ShouldReturnEmpty()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { Endpoint = "https://api.deepseek.com/chat/completions" };

        // Act — Endpoint 已包含完整路径时返回空字符串（与 OpenAI 逻辑一致）
        var chatEndpoint = definition!.GetChatEndpoint(config);

        chatEndpoint.Should().BeEmpty();
    }

    /// <summary>
    /// 端到端 URL 拼接验证：BaseAddress + 相对路径 = 完整 URL
    /// </summary>
    [Fact]
    public void DeepSeekProviderDefinition_FullUrl_Composition_ShouldBeDeepSeekApiChatCompletions()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { Endpoint = null };

        var baseUrl = definition!.GetBaseUrl(config);
        var chatEndpoint = definition.GetChatEndpoint(config);

        var fullUrl = new Uri(new Uri(baseUrl), chatEndpoint).AbsoluteUri;

        // DeepSeek 完整 URL: https://api.deepseek.com/chat/completions
        // 注意与 OpenAI https://api.openai.com/v1/chat/completions 的差异（无 /v1 前缀）
        fullUrl.Should().Be("https://api.deepseek.com/chat/completions");
    }

    #endregion

    #region HttpClient 配置验证

    [Fact]
    public void DeepSeekProviderDefinition_ConfigureHttpClient_ShouldAddBearerTokenAuthorizationHeader()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { ApiKey = "sk-deepseek-test-key" };
        using var client = new HttpClient();

        // Act — DeepSeek 用 Bearer Token 认证（OpenAI 兼容）
        definition!.ConfigureHttpClient(client, config);

        // Then
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        client.DefaultRequestHeaders.Authorization!.Parameter.Should().Be("sk-deepseek-test-key");
    }

    [Fact]
    public void DeepSeekProviderDefinition_ConfigureHttpClient_WithEmptyApiKey_ShouldNotAddAuthorizationHeader()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { ApiKey = "" };
        using var client = new HttpClient();

        definition!.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    #endregion

    #region API Key 解析验证

    [Fact]
    public void DeepSeekProviderDefinition_ResolveApiKeyFromEnv_WithDeepSeekApiKey_ShouldReturnIt()
    {
        var definition = GetDeepSeekDefinition();
        var oldValue = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", "sk-from-deepseek-env");

            var apiKey = definition!.ResolveApiKeyFromEnv();

            apiKey.Should().Be("sk-from-deepseek-env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldValue);
        }
    }

    [Fact]
    public void DeepSeekProviderDefinition_ResolveApiKeyFromEnv_WithoutEnvVar_ShouldReturnNull()
    {
        var definition = GetDeepSeekDefinition();
        var oldValue = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", null);

            var apiKey = definition!.ResolveApiKeyFromEnv();

            apiKey.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEEPSEEK_API_KEY", oldValue);
        }
    }

    #endregion

    #region 配置有效性验证

    [Fact]
    public void DeepSeekProviderDefinition_IsValid_WithApiKey_ShouldReturnTrue()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { ApiKey = "sk-test" };

        var isValid = definition!.IsValid(config);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void DeepSeekProviderDefinition_IsValid_WithEmptyApiKey_ShouldReturnFalse()
    {
        var definition = GetDeepSeekDefinition();
        var config = new ProviderConfig { ApiKey = "" };

        var isValid = definition!.IsValid(config);

        isValid.Should().BeFalse();
    }

    #endregion

    #region 模型别名验证

    [Fact]
    public void DeepSeekProviderDefinition_ResolveAlias_Chat_ShouldReturnDeepSeekChat()
    {
        var definition = GetDeepSeekDefinition();

        // Act — "chat" 应解析为 "deepseek-chat"
        var resolved = definition!.ResolveAlias("chat");

        resolved.Should().Be("deepseek-chat");
    }

    [Fact]
    public void DeepSeekProviderDefinition_ResolveAlias_Reasoner_ShouldReturnDeepSeekReasoner()
    {
        var definition = GetDeepSeekDefinition();

        // Act — "reasoner" 应解析为 "deepseek-reasoner" (R1 推理模型)
        var resolved = definition!.ResolveAlias("reasoner");

        resolved.Should().Be("deepseek-reasoner");
    }

    [Fact]
    public void DeepSeekProviderDefinition_ResolveAlias_UnknownInput_ShouldReturnNull()
    {
        var definition = GetDeepSeekDefinition();

        var resolved = definition!.ResolveAlias("unknown-model");

        resolved.Should().BeNull();
    }

    #endregion
}
