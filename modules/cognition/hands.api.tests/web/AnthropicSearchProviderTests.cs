namespace Core.Tests.Web;

/// <summary>
/// AnthropicSearchProvider 单元测试 — 从原 WebServiceTests 迁移 Anthropic web_search 行为测试（ADR 0138）。
/// 覆盖：结构化结果提取、去重、文本回退、系统提示词注入、ExtensionData 传递、错误处理、provider 可用性。
/// </summary>
public sealed class AnthropicSearchProviderTests {
    private static readonly IModelConfigLoader Loader = new ModelConfigLoader();
    private static readonly string DefaultAnthropicModelId = Loader.GetDefaultModelId("anthropic");
    private static readonly string DefaultAnthropicFastModelId = Loader.GetDefaultFastModelId("anthropic");

    private readonly Mock<IQueryService> _queryServiceMock = new();

    private static ProviderConfig CreateProviderConfig(bool supportsWebSearch = true) {
        var definitionMock = new Mock<IProviderDefinition>();
        definitionMock.SetupGet(d => d.SupportsWebSearch).Returns(supportsWebSearch);
        definitionMock.SetupGet(d => d.DefaultFastModelId).Returns(DefaultAnthropicFastModelId);
        definitionMock.SetupGet(d => d.Vendor).Returns(VendorKind.Anthropic);
        definitionMock.SetupGet(d => d.ProviderName).Returns("anthropic");
        definitionMock.SetupGet(d => d.DisplayName).Returns("Anthropic");
        definitionMock.SetupGet(d => d.DefaultModelId).Returns(DefaultAnthropicModelId);
        definitionMock.Setup(d => d.IsValid(It.IsAny<ProviderConfig>())).Returns(true);
        definitionMock.Setup(d => d.GetBaseUrl(It.IsAny<ProviderConfig>())).Returns("https://api.anthropic.com");
        definitionMock.Setup(d => d.GetChatEndpoint(It.IsAny<ProviderConfig>())).Returns("v1/messages");
        definitionMock.Setup(d => d.ConfigureHttpClient(It.IsAny<HttpClient>(), It.IsAny<ProviderConfig>()));

        return new ProviderConfig {
            Vendor = VendorKind.Anthropic.ToValue(),
            Definition = definitionMock.Object
        };
    }

    private AnthropicSearchProvider CreateProvider(bool supportsWebSearch = true)
        => new(
            clock: SystemClockService.Instance,
            queryService: _queryServiceMock.Object,
            providerConfig: CreateProviderConfig(supportsWebSearch));

    #region IsAvailable

    [Fact]
    public async Task IsAvailable_WhenSupportsWebSearch_ReturnsTrue() {
        await using var provider = CreateProvider(supportsWebSearch: true);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailable_WhenNotSupportWebSearch_ReturnsFalse() {
        await using var provider = CreateProvider(supportsWebSearch: false);
        provider.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task IsAvailable_WhenQueryServiceNull_ReturnsFalse() {
        await using var provider = new AnthropicSearchProvider(
            clock: SystemClockService.Instance,
            queryService: null,
            providerConfig: CreateProviderConfig());
        provider.IsAvailable.Should().BeFalse();
    }

    #endregion

    #region SearchAsync - Provider 检查

    [Fact]
    public async Task SearchAsync_NotSupportWebSearch_ReturnsError() {
        await using var provider = CreateProvider(supportsWebSearch: false);
        var result = await provider.SearchAsync(new SearchRequest("test")).ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("does not support web search");
    }

    #endregion

    #region SearchAsync - 结构化搜索结果提取

    [Fact]
    public async Task SearchAsync_WithWebSearchResultsMetadata_ExtractsStructuredResults() {
        var searchResultsJson = /*lang=json,strict*/ """[{"title":"Example","url":"https://example.com"}]""";
        var metadata = new Dictionary<string, JsonElement> {
            ["web_search_results"] = JsonDocument.Parse($"[{searchResultsJson}]").RootElement.Clone()
        };

        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatApiMessage> { new(ChatMessageRole.Assistant, "Search results:", metadata) });

        await using var provider = CreateProvider();
        var result = await provider.SearchAsync(new SearchRequest("test query")).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
        result.Results[0].Title.Should().Be("Example");
        result.Results[0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task SearchAsync_WithMultipleSearchResults_DeduplicatesByUrl() {
        var searchResultsJson = /*lang=json,strict*/ """[{"title":"Example","url":"https://example.com"},{"title":"Example Again","url":"https://example.com"}]""";
        var metadata = new Dictionary<string, JsonElement> {
            ["web_search_results"] = JsonDocument.Parse($"[{searchResultsJson}]").RootElement.Clone()
        };

        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatApiMessage> { new(ChatMessageRole.Assistant, "Search results:", metadata) });

        await using var provider = CreateProvider();
        var result = await provider.SearchAsync(new SearchRequest("test query")).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
    }

    #endregion

    #region SearchAsync - 回退到文本解析

    [Fact]
    public async Task SearchAsync_NoMetadata_FallsBackToTextExtraction() {
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatApiMessage> {
                new(ChatMessageRole.Assistant, "Here are some results:\n[Example](https://example.com)\n[Another](https://another.com)")
            });

        await using var provider = CreateProvider();
        var result = await provider.SearchAsync(new SearchRequest("test query")).ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(2);
    }

    #endregion

    #region SearchAsync - 系统提示词注入

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 测试断言已保证非空")]
    [Fact]
    public async Task SearchAsync_IncludesSystemPromptInRequest() {
        MessageList? capturedHistory = null;
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions, IChatClient, CancellationToken>((history, _, _, _) => capturedHistory = history)
            .ReturnsAsync(new List<ChatApiMessage> { new(ChatMessageRole.Assistant, "No results") });

        await using var provider = CreateProvider();
        await provider.SearchAsync(new SearchRequest("test query")).ConfigureAwait(true);

        capturedHistory.Should().NotBeNull();
        capturedHistory!.Count.Should().Be(2);
        capturedHistory[0].Role.Should().Be(ChatMessageRole.System);
        capturedHistory[0].Content.Should().Contain("CRITICAL REQUIREMENT");
        capturedHistory[0].Content.Should().Contain("Sources:");
        capturedHistory[1].Role.Should().Be(ChatMessageRole.User);
        capturedHistory[1].Content.Should().Contain("web search");
    }

    #endregion

    #region SearchAsync - ExtensionData 传递

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 测试断言已保证非空")]
    [Fact]
    public async Task SearchAsync_PassesWebSearchToolInExtensionData() {
        ChatOptions? capturedOptions = null;
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions, IChatClient, CancellationToken>((_, options, _, _) => capturedOptions = options)
            .ReturnsAsync(new List<ChatApiMessage> { new(ChatMessageRole.Assistant, "No results") });

        await using var provider = CreateProvider();
        await provider.SearchAsync(new SearchRequest("test query")).ConfigureAwait(true);

        capturedOptions.Should().NotBeNull();
        capturedOptions!.ExtensionData.Should().NotBeNull();
        capturedOptions.ExtensionData.Should().ContainKey("web_search_tool");

        var toolJson = capturedOptions.ExtensionData!["web_search_tool"].GetRawText();
        toolJson.Should().Contain("web_search_20250305");
        toolJson.Should().Contain("max_uses");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 测试断言已保证非空")]
    [Fact]
    public async Task SearchAsync_WithAllowedDomains_PassesInExtensionData() {
        ChatOptions? capturedOptions = null;
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions, IChatClient, CancellationToken>((_, options, _, _) => capturedOptions = options)
            .ReturnsAsync(new List<ChatApiMessage> { new(ChatMessageRole.Assistant, "No results") });

        await using var provider = CreateProvider();
        await provider.SearchAsync(new SearchRequest("test", AllowedDomains: ["example.com"])).ConfigureAwait(true);

        var toolJson = capturedOptions!.ExtensionData!["web_search_tool"].GetRawText();
        toolJson.Should().Contain("allowed_domains");
        toolJson.Should().Contain("example.com");
    }

    #endregion

    #region SearchAsync - 错误处理

    [Fact]
    public async Task SearchAsync_QueryServiceThrows_ReturnsError() {
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        await using var provider = CreateProvider();
        var result = await provider.SearchAsync(new SearchRequest("test query")).ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("API error");
    }

    #endregion
}
