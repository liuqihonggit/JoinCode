namespace Llm.Tests.Adapters.LLM.QueryServices.OpenAI;


/// <summary>
/// 流式响应读取单元测试 — 验证 SendStreamingRequestAsync 在收到 data: [DONE] 后正确退出，不卡住
/// TDD: 先写失败测试（模拟流式响应），修复后应通过
/// </summary>
public class OpenAIStreamingTests
{
    private static OpenAIQueryService CreateServiceWithMockResponse(string sseBody)
    {
        var kind = ProtocolKind.OpenAiCompatible;
        var config = new ProviderConfig
        {
            Vendor = "openai",
            ApiKey = "sk-test",
            ModelId = "gpt-4o",
            Definition = new FallbackProviderDefinition(kind)
        };

        var handler = new MockStreamingHandler(sseBody);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9901/") };
        return new OpenAIQueryService(config, httpClient);
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WithDoneSignal_CompletesWithoutHanging()
    {
        var sse = BuildSseResponse(["Hello", " world"], withDone: true);
        var service = CreateServiceWithMockResponse(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token))
        {
            events.Add(evt);
        }

        events.Should().NotBeEmpty("应收到至少一个流式事件");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WithDoneSignal_ExitsBeforeTimeout()
    {
        var sse = BuildSseResponse(["Hello"], withDone: true);
        var service = CreateServiceWithMockResponse(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var _ in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token))
        {
        }

        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "收到 [DONE] 后应立即退出，不应卡住");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WithoutDoneSignal_TimesOut()
    {
        var sse = BuildSseResponse(["Hello"], withDone: false);
        var service = CreateServiceWithMockResponse(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token))
            {
                events.Add(evt);
            }
        }
        catch (OperationCanceledException)
        {
        }

        events.Should().NotBeEmpty("超时前应收到事件");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WithUsageChunk_IncludesUsageMetadata()
    {
        var usageChunk = """data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15,"prompt_tokens_details":{"cached_tokens":3}},"model":"gpt-4o","created":1700000000}""" + "\n\n";
        var sse = BuildSseResponse(["Hi"], withDone: true, extraBeforeDone: usageChunk);
        var service = CreateServiceWithMockResponse(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token))
        {
            events.Add(evt);
        }

        events.Should().Contain(e => e.Metadata != null && e.Metadata.ContainsKey("Usage"), "应包含 usage 元数据");
    }

    private static string BuildSseResponse(string[] contentChunks, bool withDone, string? extraBeforeDone = null)
    {
        var sb = new StringBuilder();
        var id = "chatcmpl-test";

        foreach (var chunk in contentChunks)
        {
            var escaped = chunk.Replace("\\", "\\\\").Replace("\"", "\\\"");
            sb.AppendLine($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"content\":\"{escaped}\"}},\"finish_reason\":null}}],\"model\":\"gpt-4o\",\"created\":1700000000}}");
            sb.AppendLine();
        }

        sb.AppendLine($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{}},\"finish_reason\":\"stop\"}}],\"model\":\"gpt-4o\",\"created\":1700000000}}");
        sb.AppendLine();

        if (extraBeforeDone != null)
        {
            sb.Append(extraBeforeDone);
        }

        if (withDone)
        {
            sb.AppendLine("data: [DONE]");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private sealed class MockStreamingHandler : DelegatingHandler
    {
        private readonly string _sseBody;

        public MockStreamingHandler(string sseBody) : base(new HttpClientHandler())
        {
            _sseBody = sseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StringContent(_sseBody, Encoding.UTF8, "text/event-stream");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            };
            return Task.FromResult(response);
        }
    }
}
