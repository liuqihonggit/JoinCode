// JCC1017 抑制: 存量手写 JSON, 后续改为 DTO+JsonContext
#pragma warning disable JCC1017
namespace Llm.Tests.Adapters.LLM.QueryServices.OpenAI;


/// <summary>
/// 流式响应读取单元测试 — 验证 SendStreamingRequestAsync 在收到 data: [DONE] 后正确退出，不卡住
/// TDD: 先写失败测试（模拟流式响应），修复后应通过
/// </summary>
public class OpenAIStreamingTests {
    private static OpenAIQueryService CreateServiceWithMockResponse(string sseBody) {
        var kind = ProtocolKind.OpenAiCompatible;
        var config = new ProviderConfig {
            Vendor = "openai",
            ApiKey = "sk-test",
            ModelId = "test-model",
            Definition = new FallbackProviderDefinition(kind)
        };

        var handler = new MockStreamingHandler(sseBody);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9901/") };
        return new OpenAIQueryService(config, httpClient);
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WithDoneSignal_CompletesWithoutHanging() {
        var sse = BuildSseResponse(["Hello", " world"], withDone: true);
        var service = CreateServiceWithMockResponse(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token)) {
            events.Add(evt);
        }

        events.Should().NotBeEmpty("应收到至少一个流式事件");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WithDoneSignal_ExitsBeforeTimeout() {
        var sse = BuildSseResponse(["Hello"], withDone: true);
        var service = CreateServiceWithMockResponse(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var _ in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token)) {
        }

        sw.Stop();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "收到 [DONE] 后应立即退出，不应卡住");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WithoutDoneSignal_TimesOut() {
        var sse = BuildSseResponse(["Hello"], withDone: false);
        var service = CreateServiceWithMockResponse(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try {
            await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token)) {
                events.Add(evt);
            }
        } catch (OperationCanceledException) {
        }

        events.Should().NotBeEmpty("超时前应收到事件");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_WithUsageChunk_IncludesUsageMetadata() {
        var usageChunk = """data: {"id":"chatcmpl-test","object":"chat.completion.chunk","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15,"prompt_tokens_details":{"cached_tokens":3}},"model":"test-model","created":1700000000}""" + "\n\n";
        var sse = BuildSseResponse(["Hi"], withDone: true, extraBeforeDone: usageChunk);
        var service = CreateServiceWithMockResponse(sse);

        var history = new MessageList { new(MessageRole.User, "hi") };
        var events = new List<StreamEvent>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in service.GetStreamEventContentsAsync(history, cancellationToken: cts.Token)) {
            events.Add(evt);
        }

        events.Should().Contain(e => e.Metadata != null && e.Metadata.ContainsKey("Usage"), "应包含 usage 元数据");
    }

    /// <summary>
    /// vv088 回归：HTTP 200 + text/event-stream 但流不发送数据 = SendStreamingRequestAsync 无 idle timeout 保护，
    /// ReadLineAsync 无限挂死（对齐 PipeQueryService run #858 回归）。
    /// 修复前：测试挂死 30s 哨兵后失败（winner != consuming）。
    /// 修复后：idle timeout 触发 TimeoutException，测试在超时后快速通过。
    /// </summary>
    [Fact]
    public async Task GetStreamEventContentsAsync_StreamStalls_TriggersIdleTimeoutInsteadOfHanging() {
        var service = CreateServiceWithStalledStream(idleTimeoutMs: 2_000);
        var history = new MessageList { new(MessageRole.User, "hi") };

        var consuming = Task.Run(async () => {
            await foreach (var _ in service.GetStreamEventContentsAsync(history)) { }
        });

#pragma warning disable JCC3010, JCC3011, JCC3012 // 防挂死哨兵：修复缺失时流会无限卡死，30s 哨兵让红测试快速失败
        var winner = await Task.WhenAny(consuming, Task.Delay(TimeSpan.FromSeconds(30)));
#pragma warning restore JCC3010, JCC3011, JCC3012
        winner.Should().BeSameAs(consuming, "流卡死时空闲超时必须触发，禁止无限挂死（vv088 回归）");

        var act = async () => await consuming;
        await act.Should().ThrowAsync<TimeoutException>(
            "空闲超时语义必须是显式 TimeoutException（含诱导信息），而非挂死或笼统取消");
    }

    private static OpenAIQueryService CreateServiceWithStalledStream(int idleTimeoutMs) {
        var kind = ProtocolKind.OpenAiCompatible;
        var config = new ProviderConfig {
            Vendor = "openai",
            ApiKey = "sk-test",
            ModelId = "test-model",
            Definition = new FallbackProviderDefinition(kind)
        };
        var handler = new StalledStreamHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:9901/") };
        return new TestOpenAIQueryService(config, httpClient, idleTimeoutMs);
    }

    private static string BuildSseResponse(string[] contentChunks, bool withDone, string? extraBeforeDone = null) {
        var sb = new StringBuilder();
        var id = "chatcmpl-test";

        foreach (var chunk in contentChunks) {
            var escaped = chunk.Replace("\\", "\\\\").Replace("\"", "\\\"");
            sb.AppendLine($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{\"content\":\"{escaped}\"}},\"finish_reason\":null}}],\"model\":\"test-model\",\"created\":1700000000}}");
            sb.AppendLine();
        }

        sb.AppendLine($"data: {{\"id\":\"{id}\",\"object\":\"chat.completion.chunk\",\"choices\":[{{\"index\":0,\"delta\":{{}},\"finish_reason\":\"stop\"}}],\"model\":\"test-model\",\"created\":1700000000}}");
        sb.AppendLine();

        if (extraBeforeDone != null) {
            sb.Append(extraBeforeDone);
        }

        if (withDone) {
            sb.AppendLine("data: [DONE]");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private sealed class MockStreamingHandler : DelegatingHandler {
        private readonly string _sseBody;

        public MockStreamingHandler(string sseBody) : base(new HttpClientHandler()) {
            _sseBody = sseBody;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var content = new StringContent(_sseBody, Encoding.UTF8, "text/event-stream");
            var response = new HttpResponseMessage(HttpStatusCode.OK) {
                Content = content
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>测试专用子类 — 覆写 StreamIdleTimeoutMs 加速回归（2s 而非 90s）</summary>
    private sealed class TestOpenAIQueryService : OpenAIQueryService {
        private readonly int _idleTimeoutMs;
        public TestOpenAIQueryService(ProviderConfig config, HttpClient httpClient, int idleTimeoutMs)
            : base(config, httpClient) {
            _idleTimeoutMs = idleTimeoutMs;
        }
        protected internal override int StreamIdleTimeoutMs => _idleTimeoutMs;
    }

    /// <summary>模拟流卡死 — HTTP 200 + text/event-stream 但底层流永不完成（不发送数据不关闭）</summary>
    private sealed class StalledStreamHandler : DelegatingHandler {
        public StalledStreamHandler() : base(new HttpClientHandler()) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var content = new StreamContent(new NeverCompletingStream());
            var response = new HttpResponseMessage(HttpStatusCode.OK) {
                Content = content
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }
    }

    /// <summary>永不完成的流 — ReadAsync 永不返回（模拟服务器 200 但不发数据不关闭连接）</summary>
    private sealed class NeverCompletingStream : Stream {
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("同步读取不支持");
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(static s => {
                if (s is TaskCompletionSource<int> source) source.TrySetCanceled();
            }, tcs);
            return tcs.Task;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}