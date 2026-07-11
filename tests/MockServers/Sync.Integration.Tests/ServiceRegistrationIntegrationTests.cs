namespace Tests;

[Trait("Category", "Integration")]
public class ServiceRegistrationIntegrationTests
{
    [Fact]
    public void AddWorkflowServices_ShouldRegisterITranscriptService()
    {
        var services = BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var transcriptService = sp.GetService<ITranscriptService>();
        Assert.NotNull(transcriptService);
    }

    [Fact]
    public void AddWorkflowServices_ShouldRegisterIFastModeService()
    {
        var services = BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var fastModeService = sp.GetService<IFastModeService>();
        Assert.NotNull(fastModeService);
    }

    [Fact]
    public void AddWorkflowServices_ShouldRegisterISimpleModeService()
    {
        var services = BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var simpleModeService = sp.GetService<ISimpleModeService>();
        Assert.NotNull(simpleModeService);
    }

    [Fact]
    public void FastModeService_ShouldUsePrimaryModelIdFromConfig()
    {
        var services = BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var fastModeService = sp.GetRequiredService<IFastModeService>();
        Assert.Equal("gpt-4o", fastModeService.PrimaryModelId);
    }

    [Fact]
    public void AddWorkflowServices_ShouldRegisterIWebService()
    {
        var services = BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var webService = sp.GetService<IWebService>();
        Assert.NotNull(webService);
    }

    [Fact]
    public void AddWorkflowServices_ShouldRegisterITaskService()
    {
        var services = BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var taskService = sp.GetService<ITaskService>();
        Assert.NotNull(taskService);
    }

    [Fact]
    public void AddWorkflowServices_ShouldRegisterIAgentWorktreeService()
    {
        var services = BuildServiceCollection();
        var sp = services.BuildServiceProvider();

        var worktreeService = sp.GetService<IAgentWorktreeService>();
        Assert.NotNull(worktreeService);
    }

    /// <summary>
    /// IChatService 依赖 IChatClient（由 AddKernelWithPlugins 注册），
    /// 仅在 AddAiWorkflowServices 完整路径下可用，AddWorkflowServices 不含 AI 服务。
    /// </summary>
    [Fact]
    public void AddAiWorkflowServices_ShouldRegisterIChatService()
    {
        var services = BuildAiServiceCollection();
        var sp = services.BuildServiceProvider();

        var chatService = sp.GetService<IChatService>();
        Assert.NotNull(chatService);
    }

    private static ServiceCollection BuildServiceCollection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc-test-{Guid.NewGuid():N}");
        var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        fileSystem.CreateDirectory(tempDir);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, tempDir);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IFileSystem>(fileSystem);

        var config = new WorkflowConfig();
        services.AddWorkflowServices(config);
        services.AddTestPipelines();

        return services;
    }

    private static ServiceCollection BuildAiServiceCollection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc-test-{Guid.NewGuid():N}");
        var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        fileSystem.CreateDirectory(tempDir);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, tempDir);

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
