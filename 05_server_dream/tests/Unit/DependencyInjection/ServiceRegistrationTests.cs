namespace Dream.Tests.DependencyInjection;

/// <summary>
/// Dream DI 注册单元测试
/// </summary>
public sealed class ServiceRegistrationTests
{
    [Fact]
    public void AddDreamServices_WithConfigure_RegistersOptions()
    {
        var services = new ServiceCollection();

        services.AddDreamServices(cfg => cfg.MinHours = 12);

        var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<AutoDreamConfig>();
        Assert.Equal(12, config.MinHours);
    }

    [Fact]
    public void AddDreamServices_WithoutConfigure_RegistersSingleton()
    {
        var services = new ServiceCollection();

        services.AddDreamServices();

        var provider = services.BuildServiceProvider();
        var config1 = provider.GetRequiredService<AutoDreamConfig>();
        var config2 = provider.GetRequiredService<AutoDreamConfig>();
        Assert.Same(config1, config2);
        Assert.Equal(24, config1.MinHours);
    }

    [Fact]
    public void AddDreamServicesWithPersistence_WithConfigure_RegistersOptions()
    {
        var services = new ServiceCollection();

        services.AddDreamServicesWithPersistence(cfg => cfg.MinSessions = 3);

        var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<AutoDreamConfig>();
        Assert.Equal(3, config.MinSessions);
    }

    [Fact]
    public void AddDreamServicesWithPersistence_WithoutConfigure_RegistersSingleton()
    {
        var services = new ServiceCollection();

        services.AddDreamServicesWithPersistence();

        var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<AutoDreamConfig>());
    }

    [Fact]
    public void InitializeDreamSystem_ReturnsSameProvider()
    {
        var provider = new ServiceCollection().BuildServiceProvider();

        var result = provider.InitializeDreamSystem();

        Assert.Same(provider, result);
    }

    [Fact]
    public async Task InitializeDreamSystemWithPersistenceAsync_WithPersistentRegistry_LoadsActiveTasks()
    {
        var persistence = new Mock<IDreamTaskPersistence>();
        persistence.Setup(p => p.LoadAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DreamTaskState>());
        var services = new ServiceCollection();
        services.AddSingleton<IDreamTaskPersistence>(persistence.Object);
        services.AddSingleton<IDreamTaskRegistry, PersistentDreamTaskRegistry>();
        var provider = services.BuildServiceProvider();

        var result = await provider.InitializeDreamSystemWithPersistenceAsync().ConfigureAwait(true);

        Assert.Same(provider, result);
        persistence.Verify(p => p.LoadAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InitializeDreamSystemWithPersistenceAsync_WithoutPersistentRegistry_ReturnsProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDreamTaskRegistry, InMemoryDreamTaskRegistry>();
        var provider = services.BuildServiceProvider();

        var result = await provider.InitializeDreamSystemWithPersistenceAsync().ConfigureAwait(true);

        Assert.Same(provider, result);
    }

    [Fact]
    public void AddDreamPluginServices_RegistersAutoDreamConfig()
    {
        var services = new ServiceCollection();

        services.AddDreamPluginServices();

        var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<AutoDreamConfig>();
        Assert.Equal(2, config.MinSessions);
    }
}