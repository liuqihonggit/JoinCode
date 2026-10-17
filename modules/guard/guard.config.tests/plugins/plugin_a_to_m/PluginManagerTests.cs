
namespace Core.Tests.Services;

public class PluginManagerTests {
    private ServiceProvider CreateServiceProvider() {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<IPluginManager, PluginManager>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Constructor_ShouldInitializeEmptyPluginManager() {
        await using var serviceProvider = CreateServiceProvider();
        await using var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

        Assert.Empty(pluginManager.LoadedPluginNames);
        Assert.False(pluginManager.IsPluginLoaded("any-plugin"));
    }

    [Fact]
    public async Task UnloadPluginAsync_WhenPluginNotLoaded_ShouldReturnAlreadyUnloaded() {
        await using var serviceProvider = CreateServiceProvider();
        await using var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

        var result = await pluginManager.UnloadPluginAsync("non-existent").ConfigureAwait(true);

        Assert.Equal(PluginUnloadStatus.AlreadyUnloaded, result.Status);
        Assert.Equal("non-existent", result.PluginName);
    }

    [Fact]
    public async Task UnloadAllPluginsAsync_WhenNoPluginsLoaded_ShouldReturnEmptyList() {
        await using var serviceProvider = CreateServiceProvider();
        await using var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

        var results = await pluginManager.UnloadAllPluginsAsync().ConfigureAwait(true);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Dispose_ShouldNotThrow_WhenNoPluginsLoaded() {
        await using var serviceProvider = CreateServiceProvider();
        var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

        var exception = await Record.ExceptionAsync(async () => await pluginManager.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public void PluginUnloadOptions_Default_ShouldHaveCorrectValues() {
        var options = PluginUnloadOptions.Default;

        Assert.Equal(TimeSpan.FromSeconds(5), options.CooperativeTimeout);
        Assert.True(options.ForceAlcUnloadOnTimeout);
    }

    [Fact]
    public void PluginUnloadOptions_CanBeCustomized() {
        var options = new PluginUnloadOptions {
            CooperativeTimeout = TimeSpan.FromSeconds(10),
            ForceAlcUnloadOnTimeout = false
        };

        Assert.Equal(TimeSpan.FromSeconds(10), options.CooperativeTimeout);
        Assert.False(options.ForceAlcUnloadOnTimeout);
    }

    [Fact]
    public void PluginUnloadResult_Success_ShouldHaveCorrectProperties() {
        var elapsed = TimeSpan.FromMilliseconds(100);
        var result = PluginUnloadResult.Success("test-plugin", elapsed);

        Assert.Equal(PluginUnloadStatus.Success, result.Status);
        Assert.Equal("test-plugin", result.PluginName);
        Assert.Equal(elapsed, result.ElapsedTime);
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void PluginUnloadResult_CooperativeTimeout_ShouldHaveCorrectProperties() {
        var elapsed = TimeSpan.FromSeconds(5);
        var result = PluginUnloadResult.CooperativeTimeout("test-plugin", elapsed);

        Assert.Equal(PluginUnloadStatus.CooperativeTimeout, result.Status);
        Assert.Equal("test-plugin", result.PluginName);
        Assert.Equal(elapsed, result.ElapsedTime);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void PluginUnloadResult_AlcUnloadFailed_ShouldHaveCorrectProperties() {
        var elapsed = TimeSpan.FromMilliseconds(500);
        var errorMessage = "ALC unload failed";
        var result = PluginUnloadResult.AlcUnloadFailed("test-plugin", elapsed, errorMessage);

        Assert.Equal(PluginUnloadStatus.AlcUnloadFailed, result.Status);
        Assert.Equal("test-plugin", result.PluginName);
        Assert.Equal(elapsed, result.ElapsedTime);
        Assert.False(result.IsSuccess);
        Assert.Equal(errorMessage, result.ErrorMessage);
    }

    [Fact]
    public void PluginUnloadResult_AlreadyUnloaded_ShouldHaveCorrectProperties() {
        var result = PluginUnloadResult.AlreadyUnloaded("test-plugin");

        Assert.Equal(PluginUnloadStatus.AlreadyUnloaded, result.Status);
        Assert.Equal("test-plugin", result.PluginName);
        Assert.Equal(TimeSpan.Zero, result.ElapsedTime);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task IsWorkflowPluginLoaded_WhenNotLoaded_ShouldReturnFalse() {
        await using var serviceProvider = CreateServiceProvider();
        await using var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

        Assert.False(pluginManager.IsWorkflowPluginLoaded("non-existent"));
    }

    [Fact]
    public async Task IsExternalPluginLoaded_WhenNotLoaded_ShouldReturnFalse() {
        await using var serviceProvider = CreateServiceProvider();
        await using var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

        Assert.False(pluginManager.IsExternalPluginLoaded("non-existent"));
    }

    [Fact]
    public async Task GetWorkflowPlugin_WhenNotLoaded_ShouldReturnNull() {
        await using var serviceProvider = CreateServiceProvider();
        await using var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

        Assert.Null(pluginManager.GetWorkflowPlugin("non-existent"));
    }

    [Fact]
    public async Task GetExternalPlugin_WhenNotLoaded_ShouldReturnNull() {
        await using var serviceProvider = CreateServiceProvider();
        await using var pluginManager = serviceProvider.GetRequiredService<IPluginManager>();

        Assert.Null(pluginManager.GetExternalPlugin("non-existent"));
    }

    [Fact]
    public async Task LoadExternalPlugin_PendingApproval_Throws() {
        var approval = new PluginApprovalRegistry();
        approval.ArmRequest("unapproved-plugin", "等待审批");
        await using var fs = new InMemoryFileSystem();
        await using var pluginManager = new PluginManager(fs, approvalRegistry: approval);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pluginManager.LoadExternalPluginAsync("/fake/path.exe", "unapproved-plugin"));

        Assert.Contains("PENDING-APPROVAL", ex.Message);
        Assert.Contains("unapproved-plugin", ex.Message);
    }

    [Fact]
    public async Task LoadExternalPlugin_AfterApproval_Succeeds_Or_Fails_With_Other_Error() {
        var approval = new PluginApprovalRegistry();
        var req = approval.ArmRequest("approved-plugin", "已审批");
        approval.Approve(req.RequestId);
        await using var fs = new InMemoryFileSystem();
        await using var pluginManager = new PluginManager(fs, approvalRegistry: approval);

        // 审批通过后不应因 PENDING-APPROVAL 失败(可能因文件不存在失败,但不是审批错误)
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => pluginManager.LoadExternalPluginAsync("/fake/path.exe", "approved-plugin"));
        Assert.DoesNotContain("PENDING-APPROVAL", ex.Message);
    }
}