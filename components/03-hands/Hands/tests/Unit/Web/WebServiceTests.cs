namespace Core.Tests.Web;

using ChatApiMessage = JoinCode.Abstractions.LLM.Chat.ApiMessage;
using ChatMessageRole = JoinCode.Abstractions.LLM.Chat.MessageRole;

public sealed class WebServiceTests
{
    private static readonly string DefaultAnthropicModelId = ModelConfigLoader.GetDefaultModelId("anthropic");
    private static readonly string DefaultAnthropicFastModelId = ModelConfigLoader.GetDefaultFastModelId("anthropic");

    private readonly Mock<IApiClient> _apiClientMock = new();
    private readonly Mock<IQueryService> _queryServiceMock = new();

    private static ProviderConfig CreateProviderConfig(bool supportsWebSearch = true)
    {
        var definitionMock = new Mock<IProviderDefinition>();
        definitionMock.SetupGet(d => d.SupportsWebSearch).Returns(supportsWebSearch);
        definitionMock.SetupGet(d => d.DefaultFastModelId).Returns(DefaultAnthropicFastModelId);
        definitionMock.SetupGet(d => d.Kind).Returns(ProviderKind.Anthropic);
        definitionMock.SetupGet(d => d.ProviderName).Returns("anthropic");
        definitionMock.SetupGet(d => d.DisplayName).Returns("Anthropic");
        definitionMock.SetupGet(d => d.DefaultModelId).Returns(DefaultAnthropicModelId);
        definitionMock.Setup(d => d.IsValid(It.IsAny<ProviderConfig>())).Returns(true);
        definitionMock.Setup(d => d.GetBaseUrl(It.IsAny<ProviderConfig>())).Returns("https://api.anthropic.com");
        definitionMock.Setup(d => d.GetChatEndpoint(It.IsAny<ProviderConfig>())).Returns("v1/messages");
        definitionMock.Setup(d => d.ConfigureHttpClient(It.IsAny<HttpClient>(), It.IsAny<ProviderConfig>()));

        return new ProviderConfig
        {
            Provider = ProviderKind.Anthropic.ToValue(),
            Definition = definitionMock.Object
        };
    }

    private WebService CreateService(bool supportsWebSearch = true)
    {
        var cache = new WebFetchCache();
        var domainChecker = new DomainBlocklistChecker(_apiClientMock.Object, cache);
        var binaryStorage = new BinaryContentStorage(new IO.FileSystem.PhysicalFileSystem());

        var middlewares = new IMiddleware<WebContext>[]
        {
            new MetricsMiddleware<WebContext>(),
            new WebValidationMiddleware(),
            new WebCacheCheckMiddleware(cache),
            new WebDomainCheckMiddleware(domainChecker),
            new WebFetchMiddleware(_apiClientMock.Object),
            new WebContentProcessingMiddleware(new HtmlToMarkdownConverter(), binaryStorage),
            new WebCacheWriteMiddleware(cache)
        };
        var pipeline = new MiddlewarePipeline<WebContext>(middlewares.Cast<IMiddleware<WebContext>>());

        return new WebService(
            pipeline,
            cache,
            queryService: _queryServiceMock.Object,
            providerConfig: CreateProviderConfig(supportsWebSearch));
    }

    #region SearchAsync - 输入验证

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsError()
    {
        var service = CreateService();
        var result = await service.SearchAsync("").ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsError()
    {
        var service = CreateService();
        var result = await service.SearchAsync("   ").ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task SearchAsync_BothAllowedAndBlockedDomains_ReturnsError()
    {
        var service = CreateService();
        var result = await service.SearchAsync("test",
            allowedDomains: ["example.com"],
            blockedDomains: ["bad.com"]).ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cannot specify both");
    }

    #endregion

    #region SearchAsync - Provider 检查

    [Fact]
    public async Task SearchAsync_ProviderNotSupportWebSearch_ReturnsError()
    {
        var service = CreateService(supportsWebSearch: false);
        var result = await service.SearchAsync("test query").ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not available");
    }

    [Fact]
    public async Task SearchAsync_NoQueryService_ReturnsError()
    {
        var service = new WebService(
            new MiddlewarePipeline<WebContext>(Enumerable.Empty<IMiddleware<WebContext>>()),
            new WebFetchCache(),
            providerConfig: CreateProviderConfig());
        var result = await service.SearchAsync("test query").ConfigureAwait(true);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Query service");
    }

    #endregion

    #region SearchAsync - 结构化搜索结果提取

    [Fact]
    public async Task SearchAsync_WithWebSearchResultsMetadata_ExtractsStructuredResults()
    {
        var searchResultsJson = /*lang=json,strict*/ """[{"title":"Example","url":"https://example.com"}]""";
        var metadata = new Dictionary<string, JsonElement>
        {
            ["web_search_results"] = JsonDocument.Parse($"[{searchResultsJson}]").RootElement.Clone()
        };

        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatApiMessage>
            {
                new(ChatMessageRole.Assistant, "Search results:", metadata)
            });

        var service = CreateService();
        var result = await service.SearchAsync("test query").ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
        result.Results[0].Title.Should().Be("Example");
        result.Results[0].Url.Should().Be("https://example.com");
    }

    [Fact]
    public async Task SearchAsync_WithMultipleSearchResults_DeduplicatesByUrl()
    {
        var searchResultsJson = /*lang=json,strict*/ """[{"title":"Example","url":"https://example.com"},{"title":"Example Again","url":"https://example.com"}]""";
        var metadata = new Dictionary<string, JsonElement>
        {
            ["web_search_results"] = JsonDocument.Parse($"[{searchResultsJson}]").RootElement.Clone()
        };

        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatApiMessage>
            {
                new(ChatMessageRole.Assistant, "Search results:", metadata)
            });

        var service = CreateService();
        var result = await service.SearchAsync("test query").ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(1);
    }

    #endregion

    #region SearchAsync - 回退到文本解析

    [Fact]
    public async Task SearchAsync_NoMetadata_FallsBackToTextExtraction()
    {
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChatApiMessage>
            {
                new(ChatMessageRole.Assistant, "Here are some results:\n[Example](https://example.com)\n[Another](https://another.com)")
            });

        var service = CreateService();
        var result = await service.SearchAsync("test query").ConfigureAwait(true);

        result.Success.Should().BeTrue();
        result.Results.Should().HaveCount(2);
    }

    #endregion

    #region SearchAsync - 系统提示词注入

    [Fact]
    public async Task SearchAsync_IncludesSystemPromptInRequest()
    {
        MessageList? capturedHistory = null;
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions, IChatClient, CancellationToken>((history, _, _, _) => capturedHistory = history)
            .ReturnsAsync(new List<ChatApiMessage> { new(ChatMessageRole.Assistant, "No results") });

        var service = CreateService();
        await service.SearchAsync("test query").ConfigureAwait(true);

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

    [Fact]
    public async Task SearchAsync_PassesWebSearchToolInExtensionData()
    {
        ChatOptions? capturedOptions = null;
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions, IChatClient, CancellationToken>((_, options, _, _) => capturedOptions = options)
            .ReturnsAsync(new List<ChatApiMessage> { new(ChatMessageRole.Assistant, "No results") });

        var service = CreateService();
        await service.SearchAsync("test query").ConfigureAwait(true);

        capturedOptions.Should().NotBeNull();
        capturedOptions!.ExtensionData.Should().NotBeNull();
        capturedOptions.ExtensionData.Should().ContainKey("web_search_tool");

        var toolJson = capturedOptions.ExtensionData!["web_search_tool"].GetRawText();
        toolJson.Should().Contain("web_search_20250305");
        toolJson.Should().Contain("max_uses");
    }

    [Fact]
    public async Task SearchAsync_WithAllowedDomains_PassesInExtensionData()
    {
        ChatOptions? capturedOptions = null;
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Callback<MessageList, ChatOptions, IChatClient, CancellationToken>((_, options, _, _) => capturedOptions = options)
            .ReturnsAsync(new List<ChatApiMessage> { new(ChatMessageRole.Assistant, "No results") });

        var service = CreateService();
        await service.SearchAsync("test", allowedDomains: ["example.com"]).ConfigureAwait(true);

        var toolJson = capturedOptions!.ExtensionData!["web_search_tool"].GetRawText();
        toolJson.Should().Contain("allowed_domains");
        toolJson.Should().Contain("example.com");
    }

    #endregion

    #region SearchAsync - 错误处理

    [Fact]
    public async Task SearchAsync_QueryServiceThrows_ReturnsError()
    {
        _queryServiceMock
            .Setup(q => q.GetApiMessageContentsAsync(
                It.IsAny<MessageList>(), It.IsAny<ChatOptions>(),
                It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));

        var service = CreateService();
        var result = await service.SearchAsync("test query").ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("API error");
    }

    #endregion
}
