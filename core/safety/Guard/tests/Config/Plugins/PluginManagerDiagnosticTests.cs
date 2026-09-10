namespace Core.Tests.Plugins;

public sealed class PluginManagerDiagnosticTests
{
    [Fact]
    public void PluginManager_HasOnDiagnosticEvent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IPluginManager, PluginManager>();
        var sp = services.BuildServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        Assert.NotNull(pm.GetType().GetEvent("OnDiagnostic"));
    }

    [Fact]
    public async Task Unload_NonExistentPlugin_ReturnsAlreadyUnloaded()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IPluginManager, PluginManager>();
        var sp = services.BuildServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var result = await pm.UnloadPluginAsync("non-existent");
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void PluginManager_GetDiagnostics_EmptyInitially()
    {
        var pm = new PluginManager(
            fs: new PhysicalFileSystem());
        Assert.Empty(pm.GetDiagnostics());
    }
}
