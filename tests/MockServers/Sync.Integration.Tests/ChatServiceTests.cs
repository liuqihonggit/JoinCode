
using IO.FileSystem;

namespace Tests;

[CollectionDefinition("ChatServiceTests", DisableParallelization = true)]
public class ChatServiceTestsCollection { }

[Collection("ChatServiceTests")]
[Trait("Category", "Integration")]
public class ChatServiceTests : IAsyncLifetime
{
    private static IFileSystem RealFs => new PhysicalFileSystem();
    private readonly string _testStateFilePath;
    private readonly string _testDbFilePath;
    private PipeOpenAIMockServer? _mockServer;
    private string _pipeName = string.Empty;
    private ServiceProvider? _serviceProvider;

    public ChatServiceTests()
    {
        _testStateFilePath = Path.Combine(Path.GetTempPath(), $"test_chat_state_{Guid.NewGuid():N}.json");
        _testDbFilePath = Path.ChangeExtension(_testStateFilePath, ".db");
    }

    public async Task InitializeAsync()
    {
        _pipeName = $"JoinCode_Test_{Guid.NewGuid():N}";

        var options = new MockServerOptions(_pipeName);
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        _mockServer = new PipeOpenAIMockServer(options, loggerFactory.CreateLogger<PipeOpenAIMockServer>());
        await _mockServer.StartAsync().ConfigureAwait(true);
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync().ConfigureAwait(true);
            _serviceProvider = null;
            await Task.Delay(200).ConfigureAwait(true);
        }

        if (_mockServer != null)
        {
            await _mockServer.StopAsync().ConfigureAwait(true);
            await _mockServer.DisposeAsync().ConfigureAwait(true);
        }

        try { if (RealFs.FileExists(_testStateFilePath)) RealFs.DeleteFile(_testStateFilePath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine($"Cleanup skipped: {ex.Message}"); }
        try { if (RealFs.FileExists(_testDbFilePath)) RealFs.DeleteFile(_testDbFilePath); } catch (IOException ex) { System.Diagnostics.Trace.WriteLine($"Cleanup skipped: {ex.Message}"); }
    }

    private async Task<ServiceProvider> CreateServiceProviderAsync()
    {
        if (_serviceProvider != null)
        {
            _serviceProvider.Dispose();
            _serviceProvider = null;
            await Task.Delay(100).ConfigureAwait(true);
        }

        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = MockServerOptions.DefaultApiKey,
                ModelId = MockServerOptions.DefaultModel
            },
            StateFilePath = _testStateFilePath,
            PipeEndpoint = new PipeTransportConfig
            {
                PipeName = _pipeName
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddLogging(builder => builder.AddConsole());
        services.AddAiWorkflowServices(config);
        // 注册测试用管道 — ChatService 构造函数依赖 StreamMiddlewarePipeline/MiddlewarePipeline
        // 生产代码由 JoinCode.App.PipelineComposition.AddAllPipelines 注册，测试项目无法引用 Exe
        services.AddTestPipelines();

        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }

    private async Task<IChatService> CreateChatServiceAsync()
    {
        var serviceProvider = await CreateServiceProviderAsync().ConfigureAwait(true);
        return serviceProvider.GetRequiredService<IChatService>();
    }

    private async Task<IChatService> CreateChatServiceWithoutCleanupAsync()
    {
        if (_serviceProvider != null)
        {
            _serviceProvider.Dispose();
            _serviceProvider = null;
            await Task.Delay(100).ConfigureAwait(true);
        }

        var config = new WorkflowConfig
        {
            Provider = new ProviderConfig
            {
                Provider = "openai",
                ApiKey = MockServerOptions.DefaultApiKey,
                ModelId = MockServerOptions.DefaultModel
            },
            StateFilePath = _testStateFilePath,
            PipeEndpoint = new PipeTransportConfig
            {
                PipeName = _pipeName
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddLogging(builder => builder.AddConsole());
        services.AddAiWorkflowServices(config);
        // 注册测试用管道 — 同 CreateServiceProviderAsync
        services.AddTestPipelines();

        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider.GetRequiredService<IChatService>();
    }

    [Fact]
    public async Task SendApiMessageAsync_ShouldAddMessageToHistory()
    {
        var service = await CreateChatServiceAsync().ConfigureAwait(true);
        var testMessage = "Hello, test message";

        var response = await service.SendMessageAsync(testMessage).ConfigureAwait(true);

        Assert.NotNull(response);
        var chatHistory = await service.GetMessageListAsync().ConfigureAwait(true);
        Assert.NotNull(chatHistory);
        Assert.True(chatHistory.Count >= 2);
    }

    [Fact]
    public async Task ClearMessageList_ShouldEmptyHistoryAndAddSystemPrompt()
    {
        var service = await CreateChatServiceAsync().ConfigureAwait(true);

        await service.ClearHistoryAsync().ConfigureAwait(true);

        var chatHistory = await service.GetMessageListAsync().ConfigureAwait(true);
        Assert.NotNull(chatHistory);
        Assert.Single(chatHistory);
        Assert.Equal("system", chatHistory[0].Role.ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task SetSystemPrompt_ShouldUpdateSystemPrompt()
    {
        var service = await CreateChatServiceAsync().ConfigureAwait(true);
        var newSystemPrompt = "You are a test assistant.";

        await service.SetSystemPromptAsync(newSystemPrompt).ConfigureAwait(true);

        var chatHistory = await service.GetMessageListAsync().ConfigureAwait(true);
        Assert.NotNull(chatHistory);
        Assert.Single(chatHistory);
        Assert.Equal("system", chatHistory[0].Role.ToString(), ignoreCase: true);
    }

    [Fact]
    public async Task LoadState_ShouldRestorePreviousState()
    {
        var service1 = await CreateChatServiceAsync().ConfigureAwait(true);
        var testMessage = "Test message for persistence";
        var newSystemPrompt = "Test system prompt";

        await service1.SetSystemPromptAsync(newSystemPrompt).ConfigureAwait(true);
        await service1.SendMessageAsync(testMessage).ConfigureAwait(true);

        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync().ConfigureAwait(true);
            _serviceProvider = null;
            await Task.Delay(100).ConfigureAwait(true);
        }

        var service2 = await CreateChatServiceWithoutCleanupAsync().ConfigureAwait(true);
        var chatHistory = await service2.GetMessageListAsync().ConfigureAwait(true);

        Assert.NotNull(chatHistory);
        Assert.True(chatHistory.Count >= 2);
    }

    [Fact]
    public async Task SaveState_ShouldCreateStateFile()
    {
        var service = await CreateChatServiceAsync().ConfigureAwait(true);
        var testMessage = "Test message to trigger save";

        await service.SendMessageAsync(testMessage).ConfigureAwait(true);

        Assert.True(RealFs.FileExists(_testStateFilePath) || RealFs.FileExists(_testDbFilePath));
    }
}
