namespace Core.Tests.Plugins;

/// <summary>
/// 断裂点2 测试: LoadExternalPluginAsync 加黑名单保护 + 外部插件卸载泄漏加黑名单
/// </summary>
public sealed class PluginExternalBlacklistTests
{
    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IPluginManager, PluginManager>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task LoadExternalPluginAsync_BlacklistedPlugin_ThrowsInfPluginBl()
    {
        var sp = CreateServiceProvider();
        var pm = (PluginManager)sp.GetRequiredService<IPluginManager>();

        pm.AddToBlacklistForTest("blacklisted-external");

        var act = async () => await pm.LoadExternalPluginAsync("dummy.exe", "blacklisted-external").ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("[INF-PLUGIN-BL]*").ConfigureAwait(true);
        sp.Dispose();
    }

    [Fact]
    public async Task LoadExternalPluginAsync_NonExistentFile_ThrowsInf036()
    {
        var sp = CreateServiceProvider();
        var pm = sp.GetRequiredService<IPluginManager>();

        var act = async () => await pm.LoadExternalPluginAsync("dummy.exe", "nonexistent-plugin").ConfigureAwait(true);

        await act.Should().ThrowAsync<FileNotFoundException>().ConfigureAwait(true);
        sp.Dispose();
    }
}
