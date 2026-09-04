namespace Host.Tests.Cli;

public sealed class McpCliCommandTests
{
    [Fact]
    public void Constructor_ShouldRegisterFiveSubcommands()
    {
        var command = new McpCliCommand();

        command.Name.Should().Be("mcp");
        command.Subcommands.Should().HaveCount(5);
        command.Subcommands.Select(c => c.Name).Should().BeEquivalentTo(["call", "list", "search", "schema", "serve"]);
    }

    [Fact]
    public void CallSubcommand_ShouldHaveToolNameArgumentAndArgsOptions()
    {
        var command = new McpCliCommand();
        var call = command.Subcommands.First(c => c.Name == "call");

        call.Arguments.Should().ContainSingle(a => a.Name == "tool-name");
        call.Options.Should().Contain(o => o.Name == "--args");
        call.Options.Should().Contain(o => o.Name == "--args-file");
        call.Options.Should().Contain(o => o.Name == "--args-stdin");
        call.Options.Should().Contain(o => o.Name == "--json");
    }

    [Fact]
    public void ListSubcommand_ShouldHaveCategoryOption()
    {
        var command = new McpCliCommand();
        var list = command.Subcommands.First(c => c.Name == "list");

        list.Options.Should().Contain(o => o.Name == "--category");
        list.Options.Should().Contain(o => o.Name == "--json");
    }

    [Fact]
    public void SearchSubcommand_ShouldHaveQueryArgument()
    {
        var command = new McpCliCommand();
        var search = command.Subcommands.First(c => c.Name == "search");

        search.Arguments.Should().ContainSingle(a => a.Name == "query");
    }

    [Fact]
    public void SchemaSubcommand_ShouldHaveToolNameArgument()
    {
        var command = new McpCliCommand();
        var schema = command.Subcommands.First(c => c.Name == "schema");

        schema.Arguments.Should().ContainSingle(a => a.Name == "tool-name");
    }

    [Fact]
    public void CliSubCommand_Mcp_ShouldMapToMcpString()
    {
        CliSubCommand.Mcp.ToValue().Should().Be("mcp");
        CliSubCommandExtensions.FromValue("mcp").Should().Be(CliSubCommand.Mcp);
    }

    [Fact]
    public void ServeSubcommand_ShouldHaveTransportPortHostOptions()
    {
        var command = new McpCliCommand();
        var serve = command.Subcommands.First(c => c.Name == "serve");

        serve.Options.Should().Contain(o => o.Name == "--transport");
        serve.Options.Should().Contain(o => o.Name == "--port");
        serve.Options.Should().Contain(o => o.Name == "--host");
    }
}
