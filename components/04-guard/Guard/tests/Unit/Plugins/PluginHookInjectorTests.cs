namespace Core.Tests.Plugins;

public sealed class PluginHookInjectorTests
{
    private readonly Mock<IPluginManager> _pluginManager;
    private readonly PluginHookInjector _injector;

    public PluginHookInjectorTests()
    {
        _pluginManager = new Mock<IPluginManager>();
        _injector = new PluginHookInjector(_pluginManager.Object, NullLogger<PluginHookInjector>.Instance);
    }

    [Fact]
    public void Constructor_NullPluginManager_ShouldThrow()
    {
        var act = () => new PluginHookInjector(null!, NullLogger<PluginHookInjector>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task InjectHooksAsync_PluginNotLoaded_ShouldThrow()
    {
        _pluginManager.Setup(m => m.IsPluginLoaded("test")).Returns(false);

        var hooks = new List<PluginHookDefinition>
        {
            new() { HookName = "hook1", TargetEvent = "PreCompact", HookType = ShellToolNameConstants.Bash }
        };

        var act = async () => await _injector.InjectHooksAsync("test", hooks).ConfigureAwait(true);
        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task InjectHooksAsync_PluginLoaded_ShouldStoreHooks()
    {
        _pluginManager.Setup(m => m.IsPluginLoaded("test")).Returns(true);

        var hooks = new List<PluginHookDefinition>
        {
            new() { HookName = "hook1", TargetEvent = "PreCompact", HookType = ShellToolNameConstants.Bash },
            new() { HookName = "hook2", TargetEvent = "PostCompact", HookType = "function" }
        };

        await _injector.InjectHooksAsync("test", hooks).ConfigureAwait(true);

        var result = _injector.GetInjectedHooks("test");
        result.Should().HaveCount(2);
        result[0].HookName.Should().Be("hook1");
        result[1].HookName.Should().Be("hook2");
    }

    [Fact]
    public async Task RemoveHooksAsync_ExistingPlugin_ShouldRemoveHooks()
    {
        _pluginManager.Setup(m => m.IsPluginLoaded("test")).Returns(true);

        var hooks = new List<PluginHookDefinition>
        {
            new() { HookName = "hook1", TargetEvent = "PreCompact", HookType = ShellToolNameConstants.Bash }
        };

        await _injector.InjectHooksAsync("test", hooks).ConfigureAwait(true);
        await _injector.RemoveHooksAsync("test").ConfigureAwait(true);

        _injector.GetInjectedHooks("test").Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveHooksAsync_NonExistingPlugin_ShouldNotThrow()
    {
        var act = async () => await _injector.RemoveHooksAsync("nonexistent").ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public void GetInjectedHooks_NonExistingPlugin_ShouldReturnEmpty()
    {
        _injector.GetInjectedHooks("nonexistent").Should().BeEmpty();
    }

    [Fact]
    public async Task InjectHooksAsync_NullPluginName_ShouldThrow()
    {
        var act = async () => await _injector.InjectHooksAsync(null!, []).ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task InjectHooksAsync_NullHooks_ShouldThrow()
    {
        _pluginManager.Setup(m => m.IsPluginLoaded("test")).Returns(true);
        var act = async () => await _injector.InjectHooksAsync("test", null!).ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task InjectHooksAsync_MultiplePlugins_ShouldTrackSeparately()
    {
        _pluginManager.Setup(m => m.IsPluginLoaded("plugin1")).Returns(true);
        _pluginManager.Setup(m => m.IsPluginLoaded("plugin2")).Returns(true);

        var hooks1 = new List<PluginHookDefinition>
        {
            new() { HookName = "hook1", TargetEvent = "PreCompact", HookType = ShellToolNameConstants.Bash }
        };
        var hooks2 = new List<PluginHookDefinition>
        {
            new() { HookName = "hook2", TargetEvent = "PostCompact", HookType = "function" }
        };

        await _injector.InjectHooksAsync("plugin1", hooks1).ConfigureAwait(true);
        await _injector.InjectHooksAsync("plugin2", hooks2).ConfigureAwait(true);

        _injector.GetInjectedHooks("plugin1").Should().HaveCount(1);
        _injector.GetInjectedHooks("plugin2").Should().HaveCount(1);
    }
}
