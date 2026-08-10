namespace Llm.Tests.Adapters;

public sealed class QueryServiceBaseHelperTests
{
    #region ConvertRole

    [Theory]
    [InlineData("system", MessageRole.System)]
    [InlineData("user", MessageRole.User)]
    [InlineData("assistant", MessageRole.Assistant)]
    [InlineData("tool", MessageRole.Tool)]
    [InlineData("SYSTEM", MessageRole.System)]
    [InlineData(null, MessageRole.Assistant)]
    [InlineData("", MessageRole.Assistant)]
    [InlineData("unknown", MessageRole.Assistant)]
    public void ConvertRole_MapsKnownRolesAndDefaultsToAssistant(string? role, MessageRole expected)
    {
        QueryServiceBase.ConvertRole(role).Should().Be(expected);
    }

    #endregion

    #region ConvertRoleToString

    [Theory]
    [InlineData(MessageRole.System, "system")]
    [InlineData(MessageRole.User, "user")]
    [InlineData(MessageRole.Assistant, "assistant")]
    [InlineData(MessageRole.Tool, "tool")]
    public void ConvertRoleToString_MapsAllRoles(MessageRole role, string expected)
    {
        QueryServiceBase.ConvertRoleToString(role).Should().Be(expected);
    }

    [Fact]
    public void ConvertRoleToString_UnknownRole_DefaultsToAssistant()
    {
        QueryServiceBase.ConvertRoleToString((MessageRole)999).Should().Be("assistant");
    }

    #endregion

    #region MapClrTypeToJsonSchemaType

    [Theory]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(long), "integer")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(decimal), "number")]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(string), "string")]
    [InlineData(null, "string")]
    public void MapClrTypeToJsonSchemaType_MapsTypeToSchemaType(Type? type, string expected)
    {
        TestableQueryService.MapClrTypeToJsonSchemaType(type).Should().Be(expected);
    }

    #endregion

    #region ConvertToOpenAIToolCalls

    [Fact]
    public void ConvertToOpenAIToolCalls_WithDirectList_ReturnsSameList()
    {
        var expected = new List<OpenAIToolCall>
        {
            new()
            {
                Id = "id1",
                Type = "function",
                Function = new OpenAIToolCallFunction { Name = "read", Arguments = "{}" }
            }
        };

        var result = QueryServiceBase.ConvertToOpenAIToolCalls(expected);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public void ConvertToOpenAIToolCalls_WithJsonElementArray_ParsesProperties()
    {
        var json = """
            [
              {
                "Id": "call_1",
                "Name": "read_file",
                "Arguments": "{\"path\":\"a.txt\"}"
              }
            ]
            """;
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        var result = QueryServiceBase.ConvertToOpenAIToolCalls(element);

        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result![0].Id.Should().Be("call_1");
        result[0].Type.Should().Be("function");
        result[0].Function!.Name.Should().Be("read_file");
        result[0].Function!.Arguments.Should().Be("{\"path\":\"a.txt\"}");
    }

    [Fact]
    public void ConvertToOpenAIToolCalls_WithNonArrayJsonElement_ReturnsNull()
    {
        var element = JsonSerializer.SerializeToElement("not-an-array");

        var result = QueryServiceBase.ConvertToOpenAIToolCalls(element);

        result.Should().BeNull();
    }

    [Fact]
    public void ConvertToOpenAIToolCalls_WithUnsupportedType_ReturnsNull()
    {
        QueryServiceBase.ConvertToOpenAIToolCalls("string").Should().BeNull();
    }

    #endregion

    #region BaseUrl / ChatEndpoint / Definition validation

    [Fact]
    public void GetBaseUrl_WithDefinition_DelegatesToDefinition()
    {
        var definition = new Mock<IProviderDefinition>();
        definition.Setup(d => d.GetBaseUrl(It.IsAny<ProviderConfig>())).Returns("https://custom.example.com/");
        var config = new ProviderConfig { Vendor = "openai", Definition = definition.Object };

        var url = TestableQueryService.GetBaseUrl(config);

        url.Should().Be("https://custom.example.com/");
    }

    [Fact]
    public void GetBaseUrl_WithoutDefinition_ThrowsInvalidOperationException()
    {
        var config = new ProviderConfig { Vendor = "openai", Definition = null };

        var act = () => TestableQueryService.GetBaseUrl(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*缺少 IProviderDefinition*");
    }

    [Fact]
    public void GetChatEndpoint_WithDefinition_DelegatesToDefinition()
    {
        var definition = new Mock<IProviderDefinition>();
        definition.Setup(d => d.GetChatEndpoint(It.IsAny<ProviderConfig>())).Returns("chat/completions");
        var config = new ProviderConfig { Vendor = "openai", Definition = definition.Object };

        var endpoint = TestableQueryService.GetChatEndpoint(config);

        endpoint.Should().Be("chat/completions");
    }

    [Fact]
    public void GetChatEndpoint_WithoutDefinition_ThrowsInvalidOperationException()
    {
        var config = new ProviderConfig { Vendor = "openai", Definition = null };

        var act = () => TestableQueryService.GetChatEndpoint(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*缺少 IProviderDefinition*");
    }

    #endregion

    #region Rate limit headers

    [Fact]
    public void ExtractRateLimitHeaders_PopulatesLastHeaders()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-requests", "9");
        response.Headers.TryAddWithoutValidation("retry-after", "120");

        var service = CreateTestableService();
        service.ExtractRateLimitHeaders(response);

        var headers = service.GetLastRateLimitHeaders();
        headers.Should().NotBeNull();
        var actualHeaders = headers!;
        actualHeaders["x-ratelimit-remaining-requests"].Should().Be("9");
        actualHeaders["retry-after"].Should().Be("120");
        // 实现仅添加响应中存在的头，不存在的头不在字典中
        actualHeaders.Should().NotContainKey("x-ratelimit-limit-requests");
    }

    [Fact]
    public void ExtractRateLimitHeaders_NoMatchingHeaders_DoesNotPopulate()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("content-type", "application/json");

        var service = CreateTestableService();
        service.ExtractRateLimitHeaders(response);

        service.GetLastRateLimitHeaders().Should().BeNull();
    }

    [Fact]
    public void EnrichWithRateLimitMetadata_WhenHeadersExist_AddsRatelimitPrefix()
    {
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-requests", "8");

        var service = CreateTestableService();
        service.ExtractRateLimitHeaders(response);

        var original = new StreamEvent(MessageRole.Assistant, "hi", "model",
            new Dictionary<string, JsonElement> { ["Id"] = JsonElementHelper.FromString("id") });

        var enriched = service.EnrichWithRateLimitMetadata(original);

        enriched.Should().NotBeNull();
        enriched!.Metadata.Should().ContainKey("ratelimit_x-ratelimit-remaining-requests");
        enriched.Metadata!["ratelimit_x-ratelimit-remaining-requests"].GetString().Should().Be("8");
        enriched.Metadata.Should().ContainKey("Id");
    }

    [Fact]
    public void EnrichWithRateLimitMetadata_WhenNoHeaders_ReturnsNull()
    {
        var service = CreateTestableService();
        var original = new StreamEvent(MessageRole.Assistant, "hi", "model");

        var enriched = service.EnrichWithRateLimitMetadata(original);

        enriched.Should().BeNull();
    }

    #endregion

    #region Constructor validation

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        // 直接构造，绕过 CreateTestableService 的 ??= 默认值替换
        var act = () => new TestableQueryService(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_ConfigWithoutDefinition_ThrowsInvalidOperationException()
    {
        var config = new ProviderConfig { Vendor = "openai", Definition = null };

        var act = () => CreateTestableService(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*IProviderDefinition 未注入*");
    }

    #endregion

    private static TestableQueryService CreateTestableService(ProviderConfig? config = null)
    {
        var definition = new Mock<IProviderDefinition>();
        definition.Setup(d => d.GetBaseUrl(It.IsAny<ProviderConfig>())).Returns("https://api.example.com/");
        definition.Setup(d => d.GetChatEndpoint(It.IsAny<ProviderConfig>())).Returns("chat/completions");

        config ??= new ProviderConfig
        {
            Vendor = "openai",
            ApiKey = "sk-test",
            Definition = definition.Object
        };

        return new TestableQueryService(config);
    }

    private sealed class TestableQueryService : QueryServiceBase
    {
        public TestableQueryService(ProviderConfig config)
            : base(config, new HttpClient(new FakeHttpMessageHandler()), logger: null, fs: null, resilientExecutor: null)
        {
        }

        public override Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
            MessageList chatHistory,
            ChatOptions? executionSettings = null,
            IChatClient? kernel = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ApiMessage>>([]);

        public override IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
            MessageList chatHistory,
            ChatOptions? executionSettings = null,
            IChatClient? kernel = null,
            CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<StreamEvent>();

        public new void ExtractRateLimitHeaders(HttpResponseMessage response)
            => base.ExtractRateLimitHeaders(response);

        public new IReadOnlyDictionary<string, string?>? GetLastRateLimitHeaders()
            => base.GetLastRateLimitHeaders();

        public new StreamEvent? EnrichWithRateLimitMetadata(StreamEvent msg)
            => base.EnrichWithRateLimitMetadata(msg);

        public new static string MapClrTypeToJsonSchemaType(Type? type)
            => QueryServiceBase.MapClrTypeToJsonSchemaType(type);

        public new static string GetBaseUrl(ProviderConfig config)
            => QueryServiceBase.GetBaseUrl(config);

        public new static string GetChatEndpoint(ProviderConfig config)
            => QueryServiceBase.GetChatEndpoint(config);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage());
    }
}
