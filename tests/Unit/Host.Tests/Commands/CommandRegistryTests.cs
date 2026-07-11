
namespace Core.Tests.Commands;

public class CommandRegistryTests
{
    private readonly ChatCommandRegistry _registry = new();

    [Fact]
    public void Register_ValidCommand_ShouldBeRegistered()
    {
        var command = new TestChatCommand("test", "Test command");

        _registry.Register(command);

        _registry.HasCommand("test").Should().BeTrue();
    }

    [Fact]
    public void GetCommand_ExistingCommand_ShouldReturnCommand()
    {
        var command = new TestChatCommand("test", "Test command");
        _registry.Register(command);

        var result = _registry.GetCommand("test");

        result.Should().NotBeNull();
        result!.Name.Should().Be("test");
    }

    [Fact]
    public void GetCommand_NonExistingCommand_ShouldReturnNull()
    {
        var result = _registry.GetCommand("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public void UnregisterCommand_ExistingCommand_ShouldRemoveCommand()
    {
        var command = new TestChatCommand("test", "Test command");
        _registry.Register(command);

        var result = ((JoinCode.Abstractions.Interfaces.ICommandRegistry)_registry).UnregisterCommand("test");

        result.Should().BeTrue();
        _registry.HasCommand("test").Should().BeFalse();
    }

    [Fact]
    public void UnregisterCommand_NonExistingCommand_ShouldReturnFalse()
    {
        var result = ((JoinCode.Abstractions.Interfaces.ICommandRegistry)_registry).UnregisterCommand("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public void GetAllCommands_WithMultipleCommands_ShouldReturnAll()
    {
        _registry.Register(new TestChatCommand("cmd1", "Command 1"));
        _registry.Register(new TestChatCommand("cmd2", "Command 2"));
        _registry.Register(new TestChatCommand("cmd3", "Command 3"));

        var commands = _registry.GetAllCommands();

        commands.Should().HaveCount(3);
        commands.Keys.Should().Contain("cmd1", "cmd2", "cmd3");
    }

    [Fact]
    public void Parse_ValidCommand_ShouldReturnSuccess()
    {
        _registry.Register(new TestChatCommand("test", "Test command"));

        var result = _registry.Parse("/test arg1 arg2");

        result.IsSuccess.Should().BeTrue();
        result.CommandName.Should().Be("test");
        result.Arguments.Should().Be("arg1 arg2");
    }

    [Fact]
    public void Parse_EmptyInput_ShouldReturnFailure()
    {
        var result = _registry.Parse("");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Parse_CommandOnly_ShouldHaveEmptyArguments()
    {
        _registry.Register(new TestChatCommand("test", "Test command"));

        var result = _registry.Parse("/test");

        result.IsSuccess.Should().BeTrue();
        result.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Register_DuplicateCommand_ShouldOverwrite()
    {
        var command1 = new TestChatCommand("test", "First command");
        var command2 = new TestChatCommand("test", "Second command");
        _registry.Register(command1);

        _registry.Register(command2);

        var result = _registry.GetCommand("test");
        result.Should().NotBeNull();
        result!.Description.Should().Be("Second command");
    }

    [Fact]
    public void RegisterRange_MultipleCommands_ShouldRegisterAll()
    {
        var commands = new IChatCommand[]
        {
            new TestChatCommand("cmd1", "Command 1"),
            new TestChatCommand("cmd2", "Command 2"),
            new TestChatCommand("cmd3", "Command 3")
        };

        _registry.RegisterRange(commands);

        _registry.HasCommand("cmd1").Should().BeTrue();
        _registry.HasCommand("cmd2").Should().BeTrue();
        _registry.HasCommand("cmd3").Should().BeTrue();
    }

    [Fact]
    public void GetCommand_CaseInsensitive_ShouldReturnCommand()
    {
        _registry.Register(new TestChatCommand("TestCommand", "Test command"));

        _registry.GetCommand("testcommand").Should().NotBeNull();
        _registry.GetCommand("TESTCOMMAND").Should().NotBeNull();
        _registry.GetCommand("TestCommand").Should().NotBeNull();
    }

    [Fact]
    public void HasCommand_CaseInsensitive_ShouldWork()
    {
        _registry.Register(new TestChatCommand("TestCommand", "Test command"));

        _registry.HasCommand("testcommand").Should().BeTrue();
        _registry.HasCommand("TESTCOMMAND").Should().BeTrue();
        _registry.HasCommand("unknown").Should().BeFalse();
    }

    [Fact]
    public void GetAllCommands_EmptyRegistry_ShouldReturnEmpty()
    {
        var commands = _registry.GetAllCommands();

        commands.Should().BeEmpty();
    }

    [Fact]
    public void GetCommandInfos_ShouldReturnAllCommandInfo()
    {
        _registry.Register(new TestChatCommand("cmd1", "Command 1"));
        _registry.Register(new TestChatCommand("cmd2", "Command 2"));

        var infos = _registry.GetCommandInfos().ToList();

        infos.Should().HaveCount(2);
        infos.Select(i => i.Name).Should().Contain("cmd1", "cmd2");
    }

    [Fact]
    public void ICommandRegistry_Register_LegacyCommand_ShouldAdapt()
    {
        var legacyCommand = new TestLegacyCommand("legacy", "Legacy command");
        var iRegistry = (JoinCode.Abstractions.Interfaces.ICommandRegistry)_registry;

        iRegistry.Register(legacyCommand);

        _registry.HasCommand("legacy").Should().BeTrue();
        _registry.GetCommand("legacy").Should().NotBeNull();
    }

    private sealed class TestChatCommand : IChatCommand
    {
        public string Name { get; }
        public string Description { get; }
        public string Usage => $"/{Name}";
        public string[] Aliases => Array.Empty<string>();
        public string ArgumentHint => string.Empty;
        public bool IsHidden => false;

        public TestChatCommand(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
        {
            return Task.FromResult(ChatCommandResult.Continue());
        }
    }

    private sealed class TestLegacyCommand : JoinCode.Abstractions.Interfaces.ICommand
    {
        public string Name { get; }
        public string Description { get; }
        public string Usage => $"/{Name}";

        public TestLegacyCommand(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public Task ExecuteAsync(JoinCode.Abstractions.Interfaces.ICommandContext context, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
