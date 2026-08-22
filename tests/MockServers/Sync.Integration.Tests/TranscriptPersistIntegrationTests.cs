using IO.FileSystem;

namespace Tests;

/// <summary>
/// Transcript 落盘下沉集成测试（T6）— 对话轮次完成后 transcript JSONL 自动生成于
/// {sessions根}/{sessionId}/transcript.json，三端（CLI/GUI/TUI）共用引擎管道链路。
/// 回归背景：落盘此前由三端各自手写（CLI=CliSession 手动、GUI=GuiSessionStore 全量覆盖、
/// TUI 无持久化）；TranscriptPersistMiddleware 下沉后由引擎统一增量写入。
/// </summary>
[Collection("ChatServiceTests")]
[Trait("Category", "Integration")]
public sealed class TranscriptPersistIntegrationTests : IAsyncLifetime
{
    private static IFileSystem RealFs => new PhysicalFileSystem();
    private PipeOpenAIMockServer? _mockServer;
    private string _pipeName = string.Empty;
    private ServiceProvider? _serviceProvider;

    public async Task InitializeAsync()
    {
        _pipeName = $"JoinCode_Test_{Guid.NewGuid():N}";

        var options = new MockServerOptions(_pipeName);
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _mockServer = new PipeOpenAIMockServer(options, loggerFactory.CreateLogger<PipeOpenAIMockServer>());
        await _mockServer.StartAsync().ConfigureAwait(true);
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync().ConfigureAwait(true);
            _serviceProvider = null;
        }

        if (_mockServer != null)
        {
            await _mockServer.StopAsync().ConfigureAwait(true);
            await _mockServer.DisposeAsync().ConfigureAwait(true);
        }
    }

    private ServiceProvider CreateServiceProvider()
    {
        if (_serviceProvider != null)
        {
            _serviceProvider.Dispose();
            _serviceProvider = null;
        }

        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Vendor = "openai",
                ApiKey = MockServerOptions.DefaultApiKey,
                ModelId = MockServerOptions.DefaultModel
            },
            PipeEndpoint = new PipeTransportConfig { PipeName = _pipeName }
        };

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddAiWorkflowServices(config);
        services.AddTestPipelines();

        _serviceProvider = services.BuildServiceProvider();
        // 引擎 sessionId 同源初始化 — 对齐 EngineSessionFactory（SwitchSession 注入真实 ID）
        _serviceProvider.GetRequiredService<IChatContextManager>().SwitchSession($"t6-e2e-{Guid.NewGuid():N}"[..24]);
        return _serviceProvider;
    }

    [Fact]
    public async Task SendMessageAsync_PersistsTranscriptDelta()
    {
        var sp = CreateServiceProvider();
        await using (sp.ConfigureAwait(true))
        {
            var chatService = sp.GetRequiredService<IChatService>();
            var ctxMgr = sp.GetRequiredService<IChatContextManager>();
            var transcriptService = sp.GetRequiredService<ITranscriptService>();
            var sessionId = ctxMgr.SessionId;

            await chatService.SendMessageAsync("T6 落盘冒烟").ConfigureAwait(true);

            // transcript 文件应生成且含本轮 user+assistant 差量
            var entries = await transcriptService.LoadTranscriptAsync(sessionId).ConfigureAwait(true);
            entries.Should().NotBeEmpty("对话后 transcript 必须有内容");
            entries.Should().Contain(e => e.Role == "user" && e.Content.Contains("T6 落盘冒烟"));
            entries.Should().Contain(e => e.Role == "assistant");
            entries.All(e => e.SessionId == sessionId).Should().BeTrue();
        }
    }

    [Fact]
    public async Task SecondTurn_AppendsWithoutDuplication()
    {
        var sp = CreateServiceProvider();
        await using (sp.ConfigureAwait(true))
        {
            var chatService = sp.GetRequiredService<IChatService>();

            await chatService.SendMessageAsync("第一轮").ConfigureAwait(true);
            await chatService.SendMessageAsync("第二轮").ConfigureAwait(true);

            var sessionId = sp.GetRequiredService<IChatContextManager>().SessionId;
            var transcriptService = sp.GetRequiredService<ITranscriptService>();
            var entries = await transcriptService.LoadTranscriptAsync(sessionId).ConfigureAwait(true);

            entries.Count(e => e.Role == "user" && e.Content == "第一轮").Should().Be(1, "增量语义：每轮只写一次");
            entries.Count(e => e.Role == "user" && e.Content == "第二轮").Should().Be(1);
        }
    }
}
