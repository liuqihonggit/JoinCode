namespace Llm.Tests.Adapters.LLM;


public class QueryServiceBaseHelpersTests
{
    #region ConvertRole

    [Theory]
    [InlineData("system", MessageRole.System)]
    [InlineData("user", MessageRole.User)]
    [InlineData("assistant", MessageRole.Assistant)]
    [InlineData("tool", MessageRole.Tool)]
    public void ConvertRole_ValidString_ReturnsExpected(string? role, MessageRole expected)
    {
        QueryServiceBase.ConvertRole(role).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void ConvertRole_InvalidString_ReturnsAssistant(string? role)
    {
        QueryServiceBase.ConvertRole(role).Should().Be(MessageRole.Assistant);
    }

    #endregion

    #region ConvertRoleToString

    [Theory]
    [InlineData(MessageRole.System, "system")]
    [InlineData(MessageRole.User, "user")]
    [InlineData(MessageRole.Assistant, "assistant")]
    [InlineData(MessageRole.Tool, "tool")]
    public void ConvertRoleToString_ValidRole_ReturnsExpected(MessageRole role, string expected)
    {
        QueryServiceBase.ConvertRoleToString(role).Should().Be(expected);
    }

    [Fact]
    public void ConvertRoleToString_UnknownRole_ReturnsAssistant()
    {
        QueryServiceBase.ConvertRoleToString((MessageRole)999).Should().Be("assistant");
    }

    #endregion

    #region MapClrTypeToJsonSchemaType

    [Theory]
    [InlineData(null, "string")]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(long), "integer")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(decimal), "number")]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(object), "string")]
    public void MapClrTypeToJsonSchemaType_MapsExpectedType(Type? type, string expected)
    {
        TestableQueryService.MapClrTypeToJsonSchemaType(type).Should().Be(expected);
    }

    #endregion

    #region ConvertToOpenAIToolCalls

    [Fact]
    public void ConvertToOpenAIToolCalls_Null_ReturnsNull()
    {
        QueryServiceBase.ConvertToOpenAIToolCalls(null).Should().BeNull();
    }

    [Fact]
    public void ConvertToOpenAIToolCalls_DirectList_ReturnsSameInstance()
    {
        var list = new List<OpenAIToolCall>
        {
            new() { Id = "1", Function = new OpenAIToolCallFunction { Name = "fn" } }
        };

        var result = QueryServiceBase.ConvertToOpenAIToolCalls(list);

        result.Should().BeSameAs(list);
    }

    [Fact]
    public void ConvertToOpenAIToolCalls_JsonElementArray_ParsesEntries()
    {
        var json = JsonSerializer.SerializeToElement(new[]
        {
            new { Id = "call-1", Name = "toolA", Arguments = "{}" },
            new { Id = "call-2", Name = "toolB", Arguments = "{\"x\":1}" }
        });

        var result = QueryServiceBase.ConvertToOpenAIToolCalls(json);

        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result![0].Id.Should().Be("call-1");
        result[0].Function!.Name.Should().Be("toolA");
        result[1].Function!.Arguments.Should().Be("{\"x\":1}");
    }

    [Fact]
    public void ConvertToOpenAIToolCalls_UnknownObject_ReturnsNull()
    {
        QueryServiceBase.ConvertToOpenAIToolCalls("not-a-list").Should().BeNull();
    }

    #endregion

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

        public new static string MapClrTypeToJsonSchemaType(Type? type)
            => QueryServiceBase.MapClrTypeToJsonSchemaType(type);
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage());
    }
}
