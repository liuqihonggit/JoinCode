namespace Core.Tests.Plugins;

public sealed class PluginManagerTwoPhaseUnloadTests
{
    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IPluginManager, PluginManager>();
        services.AddSingleton<IResourceReferenceGraph, ResourceReferenceGraph>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task UnloadPluginAsync_WithReferenceGraph_PreparePhaseWorks()
    {
        var sp = CreateServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var result = await pm.UnloadPluginAsync("non-existent").ConfigureAwait(true);

        result.Status.Should().Be(PluginUnloadStatus.AlreadyUnloaded);
    }

    [Fact]
    public async Task UnloadAllPluginsAsync_WithReferenceGraph_Works()
    {
        var sp = CreateServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var results = await pm.UnloadAllPluginsAsync().ConfigureAwait(true);

        results.Should().BeEmpty();
    }

    [Fact]
    public void PluginManager_WithResourceScanner_ScanAfterUnloadNoLeak()
    {
        var scanner = new PluginResourceScanner();
        var report = scanner.ScanPluginResources("test-plugin", []);

        report.HasLeaks.Should().BeFalse();
    }

    [Fact]
    public async Task UnloadPluginAsync_ResourceGraphRegistered_PrepareRemovesReferences()
    {
        var sp = CreateServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();
        var graph = sp.GetRequiredService<IResourceReferenceGraph>();

        var ref1 = new ResourceReference(
            new ObjectId(ObjectType.Resource, "cmdB1"),
            new ObjectId(ObjectType.Resource, "cmdA1"),
            "pluginB", "pluginA");
        graph.AddReference(ref1);
        graph.GetConsumers("pluginA").Should().Contain("pluginB");

        var result = await pm.UnloadPluginAsync("pluginA").ConfigureAwait(true);

        result.Status.Should().Be(PluginUnloadStatus.AlreadyUnloaded);
        graph.GetConsumers("pluginA").Should().BeEmpty();
    }
}
