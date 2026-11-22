// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Core.Tests.Services.E2E;

/// <summary>
/// 插件命令生命周期 E2E（TASK103 #39 收窄）。
/// <para>原文件混测 PluginHookInjector（死链，见 TASK103 #39）+ PluginCommandRegistry（活功能）；
/// hook 部分随注入器一并归档，本文件只保留命令注册/注销/别名/隔离的真实生命周期覆盖。</para>
/// </summary>
public sealed class PluginLifecycleE2ETests {
    private readonly PluginCommandRegistry _commandRegistry;

    public PluginLifecycleE2ETests() {
        _commandRegistry = new PluginCommandRegistry(NullLogger<PluginCommandRegistry>.Instance);
    }

    [Fact]
    public async Task PluginLifecycle_RegisterCommand_ShouldResolveByNameAndAlias() {
        var cmd = new PluginCommandDefinition {
            CommandName = "my-cmd",
            PluginName = "my-plugin",
            Description = "Test command",
            HandlerType = "TestHandler",
            Aliases = ["mc"]
        };

        await _commandRegistry.RegisterCommandAsync(cmd).ConfigureAwait(true);

        _commandRegistry.GetCommand("my-cmd").Should().NotBeNull();
        _commandRegistry.GetCommand("mc").Should().NotBeNull();
    }

    [Fact]
    public async Task PluginLifecycle_UnregisterCommand_ShouldCleanup() {
        var cmd = new PluginCommandDefinition {
            CommandName = "cleanup-cmd",
            PluginName = "cleanup-plugin",
            Description = "Cleanup test",
            HandlerType = "TestHandler"
        };

        await _commandRegistry.RegisterCommandAsync(cmd).ConfigureAwait(true);
        await _commandRegistry.UnregisterCommandAsync("cleanup-cmd").ConfigureAwait(true);

        _commandRegistry.GetCommand("cleanup-cmd").Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task PluginLifecycle_MultiplePlugins_ShouldIsolateCommands() {
        await _commandRegistry.RegisterCommandAsync(new PluginCommandDefinition {
            CommandName = "cmd-a",
            PluginName = "plugin-a",
            Description = "A command",
            HandlerType = "HandlerA"
        }).ConfigureAwait(true);

        await _commandRegistry.RegisterCommandAsync(new PluginCommandDefinition {
            CommandName = "cmd-b",
            PluginName = "plugin-b",
            Description = "B command",
            HandlerType = "HandlerB"
        }).ConfigureAwait(true);

        _commandRegistry.GetCommand("cmd-a")!.PluginName.Should().Be("plugin-a");
        _commandRegistry.GetCommand("cmd-b")!.PluginName.Should().Be("plugin-b");

        await _commandRegistry.UnregisterCommandAsync("cmd-a").ConfigureAwait(true);
        _commandRegistry.GetCommand("cmd-a").Should().BeNull();
        _commandRegistry.GetCommand("cmd-b").Should().NotBeNull();
    }

    [Fact]
    public async Task PluginLifecycle_CommandAliases_ShouldRegisterAndUnregister() {
        var cmd = new PluginCommandDefinition {
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
