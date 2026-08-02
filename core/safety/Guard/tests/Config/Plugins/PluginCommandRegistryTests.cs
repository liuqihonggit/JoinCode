namespace Core.Tests.Plugins;

public sealed class PluginCommandRegistryTests
{
    private readonly PluginCommandRegistry _registry;

    public PluginCommandRegistryTests()
    {
        _registry = new PluginCommandRegistry(NullLogger<PluginCommandRegistry>.Instance);
    }

    private static PluginCommandDefinition CreateCommand(string name, string plugin = "test-plugin", List<string>? aliases = null)
    {
        return new PluginCommandDefinition
        {
            CommandName = name,
            PluginName = plugin,
            Description = $"Test command {name}",
            HandlerType = "TestHandler",
            Aliases = aliases
        };
    }

    [Fact]
    public async Task RegisterCommandAsync_ValidCommand_ShouldRegister()
    {
        var cmd = CreateCommand("test-cmd");
        await _registry.RegisterCommandAsync(cmd).ConfigureAwait(true);

        var result = _registry.GetCommand("test-cmd");
        result.Should().NotBeNull();
        result!.CommandName.Should().Be("test-cmd");
    }

    [Fact]
    public async Task RegisterCommandAsync_NullCommand_ShouldThrow()
    {
        var act = async () => await _registry.RegisterCommandAsync(null!).ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task RegisterCommandAsync_DuplicateCommand_ShouldNotOverwrite()
    {
        var cmd1 = CreateCommand("test-cmd", "plugin1");
        var cmd2 = CreateCommand("test-cmd", "plugin2");

        await _registry.RegisterCommandAsync(cmd1).ConfigureAwait(true);
        await _registry.RegisterCommandAsync(cmd2).ConfigureAwait(true);

        var result = _registry.GetCommand("test-cmd");
        result!.PluginName.Should().Be("plugin1");
    }

    [Fact]
    public async Task RegisterCommandAsync_WithAliases_ShouldRegisterAliases()
    {
        var cmd = CreateCommand("test-cmd", aliases: ["tc", "t"]);
        await _registry.RegisterCommandAsync(cmd).ConfigureAwait(true);

        _registry.GetCommand("tc").Should().NotBeNull();
        _registry.GetCommand("t").Should().NotBeNull();
    }

    [Fact]
    public async Task UnregisterCommandAsync_ExistingCommand_ShouldRemove()
    {
        var cmd = CreateCommand("test-cmd");
        await _registry.RegisterCommandAsync(cmd).ConfigureAwait(true);
        await _registry.UnregisterCommandAsync("test-cmd").ConfigureAwait(true);

        _registry.GetCommand("test-cmd").Should().BeNull();
    }

    [Fact]
    public async Task UnregisterCommandAsync_WithAliases_ShouldRemoveAliases()
    {
        var cmd = CreateCommand("test-cmd", aliases: ["tc"]);
        await _registry.RegisterCommandAsync(cmd).ConfigureAwait(true);
        await _registry.UnregisterCommandAsync("test-cmd").ConfigureAwait(true);

        _registry.GetCommand("test-cmd").Should().BeNull();
        _registry.GetCommand("tc").Should().BeNull();
    }

    [Fact]
    public async Task UnregisterCommandAsync_NullOrWhiteSpace_ShouldThrow()
    {
        var act = async () => await _registry.UnregisterCommandAsync(null!).ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);

        var act2 = async () => await _registry.UnregisterCommandAsync("  ").ConfigureAwait(true);
        await act2.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task UnregisterCommandAsync_NonExistingCommand_ShouldNotThrow()
    {
        var act = async () => await _registry.UnregisterCommandAsync("nonexistent").ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public void GetCommand_NullOrWhiteSpace_ShouldThrow()
    {
        var act = () => _registry.GetCommand(null!);
        act.Should().Throw<ArgumentException>();

        var act2 = () => _registry.GetCommand("  ");
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task GetRegisteredCommands_ShouldReturnAllCommands()
    {
        await _registry.RegisterCommandAsync(CreateCommand("cmd1")).ConfigureAwait(true);
        await _registry.RegisterCommandAsync(CreateCommand("cmd2")).ConfigureAwait(true);

        var commands = _registry.GetRegisteredCommands();
        commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRegisteredCommands_Empty_ShouldReturnEmptyList()
    {
        var commands = _registry.GetRegisteredCommands();
        commands.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCommand_CaseInsensitive_ShouldFindCommand()
    {
        var cmd = CreateCommand("TestCmd");
        await _registry.RegisterCommandAsync(cmd).ConfigureAwait(true);

        _registry.GetCommand("testcmd").Should().NotBeNull();
        _registry.GetCommand("TESTCMD").Should().NotBeNull();
    }
}
