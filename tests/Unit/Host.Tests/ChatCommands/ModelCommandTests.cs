namespace Host.Tests.ChatCommands;

public sealed class ModelCommandTests
{
    [Fact]
    public void Name_Should_Be_model()
    {
        var cmd = new ModelCommand();
        cmd.Name.Should().Be("model");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new ModelCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Be_exact()
    {
        var cmd = new ModelCommand();
        cmd.Usage.Should().Be("/model [model-id|default|info]");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new ModelCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new ModelCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void ArgumentHint_Should_Be_set()
    {
        var cmd = new ModelCommand();
        cmd.ArgumentHint.Should().Be("[model-id|default|info]");
    }

    [Fact]
    public void Should_Implement_IChatCommand()
    {
        var cmd = new ModelCommand();
        cmd.Should().BeAssignableTo<IChatCommand>();
    }
}