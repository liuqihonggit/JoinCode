namespace Llm.Tests.Adapters.Chat;

/// <summary>
/// PipeQueryService SSE 流式读取超时防御测试 — 回归背景 run #858：
/// Linux CI 上 Sync.Integration 挂死 7 分钟（HttpClient.Timeout 只覆盖响应头，
/// ReadAsStreamAsync 之后的 SSE 逐行读取无超时兜底，流卡死 = 无限挂死）。
/// 契约：每行读取空闲超时（复用 RequestTimeoutMs）必须快速失败抛 TimeoutException，禁止无限挂死。
/// </summary>
public class PipeQueryServiceStreamingTests : IAsyncDisposable {
    private readonly string _pipeName = $"JoinCode_Test_{Guid.NewGuid():N}";
    private readonly ILoggerFactory _loggerFactory;
    private int _disposed;

    public PipeQueryServiceStreamingTests() {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    }

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _loggerFactory.Dispose();
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_NormalStream_CompletesWithEvents() {
        await using var server = CreateServer(SseChunkDelayMs: 0);
        await server.StartAsync();
        var service = CreateService(RequestTimeoutMs: 30_000);

        var events = await ConsumeAllAsync(service);

        events.Should().NotBeEmpty("正常流式响应必须完整收到事件");
        events.Should().Contain(e => e.Metadata.ContainsKey("FinishReason"), "流末尾必须带 finish_reason 元数据");
    }

    [Fact]
    public async Task GetStreamEventContentsAsync_StreamStalls_TriggersIdleTimeoutInsteadOfHanging() {
        // MockServer 每个 SSE chunk 间隔 60s = 模拟流卡死（run #858 挂死形态）
        await using var stalledServer = CreateServer(SseChunkDelayMs: 60_000);
        await stalledServer.StartAsync();

        var service = CreateService(RequestTimeoutMs: 2_000);
        var consuming = ConsumeAllAsync(service);

#pragma warning disable JCC3010, JCC3011, JCC3012 // 防挂死哨兵：修复缺失时流会无限卡死，30s 哨兵让红测试快速失败而非挂满 10 分钟
        var winner = await Task.WhenAny(consuming, Task.Delay(TimeSpan.FromSeconds(30)));
#pragma warning restore JCC3010, JCC3011, JCC3012
        winner.Should().BeSameAs(consuming, "流卡死时空闲超时必须触发，禁止无限挂死（run #858 回归）");

        var act = async () => {
            await consuming;
        };
        await act.Should().ThrowAsync<TimeoutException>(
            "空闲超时语义必须是显式 TimeoutException（含诱导信息），而非挂死或笼统取消");
    }

    private PipeOpenAIMockServer CreateServer(int SseChunkDelayMs)
        => new(
            new MockServerOptions(_pipeName) { SseChunkDelayMs = SseChunkDelayMs },
            _loggerFactory.CreateLogger<PipeOpenAIMockServer>());

    private PipeQueryService CreateService(int RequestTimeoutMs)
        => new(new PipeTransportConfig {
            PipeName = _pipeName,
            ConnectionTimeoutMs = 5_000,
            RequestTimeoutMs = RequestTimeoutMs
        }, MockServerOptions.DefaultApiKey);

    private static async Task<List<StreamEvent>> ConsumeAllAsync(PipeQueryService service) {
        var history = new MessageList([
            new ApiMessage(ChatMessageRole.User, "hello")
        ]);
        var events = new List<StreamEvent>();
        await foreach (var evt in service.GetStreamEventContentsAsync(history).ConfigureAwait(false)) {
            events.Add(evt);
        }
        return events;
    }
}
