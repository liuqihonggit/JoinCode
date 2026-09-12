namespace Tests;

[Trait("Category", "Integration")]
public class McpToolRegistrationTests
{
    [Fact]
    public async Task InitializeAsync_CompletesWithinTimeout()
    {
        var serviceProvider = BuildServiceProvider();
        var mcpService = serviceProvider.GetRequiredService<IMcpService>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await mcpService.InitializeAsync(serviceProvider, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("InitializeAsync timed out after 15 seconds");
        }
    }

    [Fact]
    public async Task RegisterAllToolHandlers_ShouldRegisterCoreTools()
    {
        var serviceProvider = BuildServiceProvider();
        var mcpService = serviceProvider.GetRequiredService<IMcpService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await mcpService.InitializeAsync(serviceProvider, cts.Token).ConfigureAwait(true);

        var toolRegistry = serviceProvider.GetRequiredService<IMcpToolRegistry>();
        var toolCount = await toolRegistry.GetLocalToolCountAsync().ConfigureAwait(true);

        Assert.True(toolCount > 0, $"Expected at least 1 tool registered, but got {toolCount}");

        var allTools = await toolRegistry.GetAllToolsAsync().ConfigureAwait(true);
        Assert.NotEmpty(allTools);
    }

    [Fact]
    public async Task RegisterAllToolHandlers_FileAndShellToolsAvailable()
    {
        var serviceProvider = BuildServiceProvider();
        var mcpService = serviceProvider.GetRequiredService<IMcpService>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await mcpService.InitializeAsync(serviceProvider, cts.Token).ConfigureAwait(true);

        var toolRegistry = serviceProvider.GetRequiredService<IMcpToolRegistry>();
        var allTools = await toolRegistry.GetAllToolsAsync().ConfigureAwait(true);

        var requiredTools = new[] { FileToolNameConstants.FileRead, ShellToolNameConstants.Bash, SearchToolNameConstants.Glob, "config_get" };
        var missingTools = requiredTools.Where(t => !allTools.ContainsKey(t)).ToList();

        Assert.Empty(missingTools);
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc-test-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, tempDir);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Trace));

        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var config = new WorkflowConfig();
        services.AddWorkflowServices(config);
        services.AddTestPipelines();

        return services.BuildServiceProvider();
    }
}
