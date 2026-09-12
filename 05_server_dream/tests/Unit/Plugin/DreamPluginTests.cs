namespace Dream.Tests.Plugin;

/// <summary>
/// Dream 插件入口单元测试
/// </summary>
public sealed class DreamPluginTests
{
    [Fact]
    public void Name_Version_Description_AreCorrect()
    {
        var plugin = new DreamPlugin();

        Assert.Equal("Dream", plugin.Name);
        Assert.Equal("1.0.0", plugin.Version);
        Assert.Equal("JoinCode 记忆整合插件", plugin.Description);
    }

    [Fact]
    public async Task LoadAsync_RegistersServices()
    {
        var plugin = new DreamPlugin();
        var services = new ServiceCollection();
        var ctx = new PluginContext("Dream", services);

        var result = await plugin.LoadAsync(ctx).ConfigureAwait(true);

        Assert.True(result.Success);
        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<AutoDreamConfig>());
    }

    [Fact]
    public async Task InitializeAsync_WithPersistentRegistry_LoadsActiveTasks()
    {
        var plugin = new DreamPlugin();
        var persistence = new Mock<IDreamTaskPersistence>();
        persistence.Setup(p => p.LoadAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DreamTaskState>());
        var services = new ServiceCollection();
        services.AddSingleton<IDreamTaskPersistence>(persistence.Object);
        services.AddSingleton<IDreamTaskRegistry, PersistentDreamTaskRegistry>();
        var provider = services.BuildServiceProvider();

        var result = await plugin.InitializeAsync(provider).ConfigureAwait(true);

        Assert.True(result.Success);
        persistence.Verify(p => p.LoadAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WithNonPersistentRegistry_DoesNotThrow()
    {
        var plugin = new DreamPlugin();
        var services = new ServiceCollection();
        services.AddSingleton<IDreamTaskRegistry, InMemoryDreamTaskRegistry>();
        var provider = services.BuildServiceProvider();

        var result = await plugin.InitializeAsync(provider).ConfigureAwait(true);

        Assert.True(result.Success);
    }

    [Fact]
    public void RegisterCommands_RegistersDreamCommands()
    {
        var plugin = new DreamPlugin();
        var registry = new Mock<ICommandRegistry>();
        var services = new ServiceCollection();
        services.AddSingleton<IDreamFeature>(Mock.Of<IDreamFeature>());
        var provider = services.BuildServiceProvider();

        plugin.RegisterCommands(registry.Object, provider);

        registry.Verify(r => r.Register(It.Is<ICommand>(c => c.Name == "dream")), Times.Once);
        registry.Verify(r => r.Register(It.Is<ICommand>(c => c.Name == "dream-tasks")), Times.Once);
    }

    [Fact]
    public void UnregisterCommands_AfterRegister_UnregistersAll()
    {
        var plugin = new DreamPlugin();
        var registry = new Mock<ICommandRegistry>();
        var services = new ServiceCollection();
        services.AddSingleton<IDreamFeature>(Mock.Of<IDreamFeature>());
        var provider = services.BuildServiceProvider();

        plugin.RegisterCommands(registry.Object, provider);
        plugin.UnregisterCommands(registry.Object);

        registry.Verify(r => r.UnregisterCommand("dream"), Times.Once);
        registry.Verify(r => r.UnregisterCommand("dream-tasks"), Times.Once);
    }

    [Fact]
    public void UnregisterCommands_WithoutRegister_DoesNotThrow()
    {
        var plugin = new DreamPlugin();
        var registry = new Mock<ICommandRegistry>();

        var exception = Record.Exception(() => plugin.UnregisterCommands(registry.Object));

        Assert.Null(exception);
    }

    [Fact]
    public void Unload_ReturnsSuccess()
    {
        var plugin = new DreamPlugin();

        var result = plugin.Unload();

        Assert.True(result.IsSuccess);
        Assert.Equal("Dream", result.PluginName);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var plugin = new DreamPlugin();

        var exception = Record.Exception(() => plugin.Dispose());

        Assert.Null(exception);
    }
}