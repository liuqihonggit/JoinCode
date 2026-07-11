namespace Core.Tests.Services.E2E;

public sealed class PluginLifecycleE2ETests : IAsyncDisposable
{
    private readonly PluginCommandRegistry _commandRegistry;
    private readonly PluginHookInjector _hookInjector;
    private readonly Mock<IPluginManager> _pluginManager;

    public PluginLifecycleE2ETests()
    {
        _commandRegistry = new PluginCommandRegistry(NullLogger<PluginCommandRegistry>.Instance);
        _pluginManager = new Mock<IPluginManager>();
        _hookInjector = new PluginHookInjector(_pluginManager.Object, NullLogger<PluginHookInjector>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await ValueTask.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task PluginLifecycle_RegisterCommandsAndHooks_ShouldWorkTogether()
    {
        _pluginManager.Setup(m => m.IsPluginLoaded("my-plugin")).Returns(true);

        var cmd = new PluginCommandDefinition
        {
            CommandName = "my-cmd",
            PluginName = "my-plugin",
            Description = "Test command",
            HandlerType = "TestHandler",
            Aliases = ["mc"]
        };

        await _commandRegistry.RegisterCommandAsync(cmd).ConfigureAwait(true);

        var hooks = new List<PluginHookDefinition>
        {
            new() { HookName = "pre-compact", TargetEvent = "PreCompact", HookType = ShellToolNameConstants.ShellExecute, Command = "echo pre" },
            new() { HookName = "post-compact", TargetEvent = "PostCompact", HookType = "function" }
        };

        await _hookInjector.InjectHooksAsync("my-plugin", hooks).ConfigureAwait(true);

        _commandRegistry.GetCommand("my-cmd").Should().NotBeNull();
        _commandRegistry.GetCommand("mc").Should().NotBeNull();
        _hookInjector.GetInjectedHooks("my-plugin").Should().HaveCount(2);
    }

    [Fact]
    public async Task PluginLifecycle_UnregisterCommandsAndHooks_ShouldCleanup()
    {
        _pluginManager.Setup(m => m.IsPluginLoaded("cleanup-plugin")).Returns(true);

        var cmd = new PluginCommandDefinition
        {
            CommandName = "cleanup-cmd",
            PluginName = "cleanup-plugin",
            Description = "Cleanup test",
            HandlerType = "TestHandler"
        };

        await _commandRegistry.RegisterCommandAsync(cmd).ConfigureAwait(true);
        await _hookInjector.InjectHooksAsync("cleanup-plugin",
        [
            new() { HookName = "hook1", TargetEvent = "PreCompact", HookType = ShellToolNameConstants.ShellExecute }
        ]).ConfigureAwait(true);

        await _commandRegistry.UnregisterCommandAsync("cleanup-cmd").ConfigureAwait(true);
        await _hookInjector.RemoveHooksAsync("cleanup-plugin").ConfigureAwait(true);

        _commandRegistry.GetCommand("cleanup-cmd").Should().BeNull();
        _hookInjector.GetInjectedHooks("cleanup-plugin").Should().BeEmpty();
    }

    [Fact]
    public async Task PluginLifecycle_MultiplePlugins_ShouldIsolateCommands()
    {
        _pluginManager.Setup(m => m.IsPluginLoaded("plugin-a")).Returns(true);
        _pluginManager.Setup(m => m.IsPluginLoaded("plugin-b")).Returns(true);

        await _commandRegistry.RegisterCommandAsync(new PluginCommandDefinition
        {
            CommandName = "cmd-a",
            PluginName = "plugin-a",
            Description = "A command",
            HandlerType = "HandlerA"
        }).ConfigureAwait(true);

        await _commandRegistry.RegisterCommandAsync(new PluginCommandDefinition
        {
            CommandName = "cmd-b",
            PluginName = "plugin-b",
            Description = "B command",
            HandlerType = "HandlerB"
        }).ConfigureAwait(true);

        await _hookInjector.InjectHooksAsync("plugin-a",
        [
            new() { HookName = "hook-a", TargetEvent = "PreCompact", HookType = ShellToolNameConstants.ShellExecute }
        ]).ConfigureAwait(true);

        await _hookInjector.InjectHooksAsync("plugin-b",
        [
            new() { HookName = "hook-b", TargetEvent = "PostCompact", HookType = "function" }
        ]).ConfigureAwait(true);

        _commandRegistry.GetCommand("cmd-a")!.PluginName.Should().Be("plugin-a");
        _commandRegistry.GetCommand("cmd-b")!.PluginName.Should().Be("plugin-b");

        _hookInjector.GetInjectedHooks("plugin-a").Should().HaveCount(1);
        _hookInjector.GetInjectedHooks("plugin-b").Should().HaveCount(1);

        await _hookInjector.RemoveHooksAsync("plugin-a").ConfigureAwait(true);
        _hookInjector.GetInjectedHooks("plugin-a").Should().BeEmpty();
        _hookInjector.GetInjectedHooks("plugin-b").Should().HaveCount(1);
    }

    [Fact]
    public async Task PluginLifecycle_CommandAliases_ShouldRegisterAndUnregister()
    {
        var cmd = new PluginCommandDefinition
        {
            CommandName = "deploy",
            PluginName = "devops-plugin",
            Description = "Deploy application",
            HandlerType = "DeployHandler",
            Aliases = ["d", "dep"]
        };

        await _commandRegistry.RegisterCommandAsync(cmd).ConfigureAwait(true);

        _commandRegistry.GetCommand("deploy").Should().NotBeNull();
        _commandRegistry.GetCommand("d").Should().NotBeNull();
        _commandRegistry.GetCommand("dep").Should().NotBeNull();

        await _commandRegistry.UnregisterCommandAsync("deploy").ConfigureAwait(true);

        _commandRegistry.GetCommand("deploy").Should().BeNull();
        _commandRegistry.GetCommand("d").Should().BeNull();
        _commandRegistry.GetCommand("dep").Should().BeNull();
    }
}
