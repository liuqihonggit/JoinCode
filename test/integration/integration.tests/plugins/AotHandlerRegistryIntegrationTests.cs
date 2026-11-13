namespace Integration.Tests.Plugins;

public sealed class AotHandlerRegistryIntegrationTests {
    private static IServiceProvider BuildServiceProvider(IFileSystem fileSystem) {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc-test-{Guid.NewGuid():N}");
        fileSystem.CreateDirectory(tempDir);
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder, tempDir);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IFileSystem>(fileSystem);

        var config = new WorkflowConfig();
        services.AddWorkflowServices(config);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task IAotHandlerRegistry_IsRegistered_InDiContainer() {
        await using var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        var provider = BuildServiceProvider(fileSystem);

        var registry = provider.GetService<IAotHandlerRegistry>();

        registry.Should().NotBeNull("IAotHandlerRegistry 应在 AddAotHandlerRegistry 中注册到 DI 容器");
    }

    [Fact]
    public async Task IAotHandlerRegistry_ReturnsHandlers_ForShellNode() {
        await using var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        var provider = BuildServiceProvider(fileSystem);
        var registry = provider.GetRequiredService<IAotHandlerRegistry>();

        var handlers = registry.GetHandlers("shell");

        handlers.Should().NotBeNull("shell 节点应有 AOT handler 注册");
        handlers.Should().NotBeEmpty("shell 节点应至少有一个工具 handler");
    }

    [Fact]
    public async Task IAotHandlerRegistry_ReturnsHandlers_ForFileNode() {
        await using var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        var provider = BuildServiceProvider(fileSystem);
        var registry = provider.GetRequiredService<IAotHandlerRegistry>();

        var handlers = registry.GetHandlers("file");

        handlers.Should().NotBeNull("file 节点应有 AOT handler 注册");
        handlers.Should().NotBeEmpty("file 节点应至少有一个工具 handler");
    }

    [Fact]
    public async Task IAotHandlerRegistry_ReturnsNull_ForUnknownNode() {
        await using var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        var provider = BuildServiceProvider(fileSystem);
        var registry = provider.GetRequiredService<IAotHandlerRegistry>();

        var handlers = registry.GetHandlers("non-existent-node-id");

        handlers.Should().BeNull("未知节点 ID 应返回 null");
    }

    [Fact]
    public async Task IAotHandlerRegistry_NodeIds_AlignWithToolCategoryEnumValue() {
        await using var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        var provider = BuildServiceProvider(fileSystem);
        var registry = provider.GetRequiredService<IAotHandlerRegistry>();

        var shellHandlers = registry.GetHandlers("shell");
        var fileHandlers = registry.GetHandlers("file");
        var webHandlers = registry.GetHandlers("web");

        shellHandlers.Should().NotBeNull("ToolCategory.Shell 的 EnumValue 是 'shell'");
        fileHandlers.Should().NotBeNull("ToolCategory.File 的 EnumValue 是 'file'");
        webHandlers.Should().NotBeNull("ToolCategory.Web 的 EnumValue 是 'web'");
    }
}
