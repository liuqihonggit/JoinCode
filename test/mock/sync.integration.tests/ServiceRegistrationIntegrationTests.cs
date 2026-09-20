namespace Tests;

[Trait("Category", "Integration")]
public class ServiceRegistrationIntegrationTests {
    private static readonly IModelConfigLoader Loader = new ModelConfigLoader();
    private static readonly string DefaultModelId = Loader.GetDefaultModelId("deepseek");

    [Fact]
    public async Task AddWorkflowServices_ShouldRegisterITranscriptService() {
        var services = await BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var transcriptService = sp.GetService<ITranscriptService>();
        Assert.NotNull(transcriptService);
    }

    [Fact]
    public async Task AddWorkflowServices_ShouldRegisterIFastModeService() {
        var services = await BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var fastModeService = sp.GetService<IFastModeService>();
        Assert.NotNull(fastModeService);
    }

    [Fact]
    public async Task AddWorkflowServices_ShouldRegisterISimpleModeService() {
        var services = await BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var simpleModeService = sp.GetService<ISimpleModeService>();
        Assert.NotNull(simpleModeService);
    }

    [Fact]
    public async Task FastModeService_ShouldUsePrimaryModelIdFromConfig() {
        var services = await BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var fastModeService = sp.GetRequiredService<IFastModeService>();
        Assert.Equal(DefaultModelId, fastModeService.PrimaryModelId);
    }

    [Fact]
    public async Task AddWorkflowServices_ShouldRegisterIWebService() {
        var services = await BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var webService = sp.GetService<IWebService>();
        Assert.NotNull(webService);
    }

    [Fact]
    public async Task AddWorkflowServices_ShouldRegisterITaskService() {
        var services = await BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var taskService = sp.GetService<ITaskService>();
        Assert.NotNull(taskService);
    }

    [Fact]
    public async Task AddWorkflowServices_ShouldRegisterIAgentWorktreeService() {
        var services = await BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var worktreeService = sp.GetService<IAgentWorktreeService>();
        Assert.NotNull(worktreeService);
    }

    /// <summary>
    /// IChatService 依赖 IChatClient（由 AddKernelWithPlugins 注册），
    /// 仅在 AddAiWorkflowServices 完整路径下可用，AddWorkflowServices 不含 AI 服务。
    /// </summary>
    [Fact]
    public async Task AddAiWorkflowServices_ShouldRegisterIChatService() {
        var services = await BuildAiServiceCollection();
        var sp = services.BuildServiceProvider();

        var chatService = sp.GetService<IChatService>();
        Assert.NotNull(chatService);
    }

    private static async Task<ServiceCollection> BuildServiceCollection() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc-test-{Guid.NewGuid():N}");
        await using var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        fileSystem.CreateDirectory(tempDir);
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder, tempDir);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IFileSystem>(fileSystem);

        var config = new WorkflowConfig();
        services.AddWorkflowServices(config);
        services.AddTestPipelines();

        return services;
    }

    private static async Task<ServiceCollection> BuildAiServiceCollection() {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc-test-{Guid.NewGuid():N}");
        await using var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        fileSystem.CreateDirectory(tempDir);
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder, tempDir);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IFileSystem>(fileSystem);

        var config = new WorkflowConfig();
        services.AddAiWorkflowServices(config);
        services.AddTestPipelines();

        return services;
    }
}