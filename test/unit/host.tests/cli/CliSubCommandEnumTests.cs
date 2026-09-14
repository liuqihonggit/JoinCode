namespace Host.Tests.Cli;

public sealed class CliSubCommandEnumTests
{
    [Theory]
    [InlineData(CliSubCommand.McpCall, "mcp_call")]
    [InlineData(CliSubCommand.McpList, "mcp_list")]
    [InlineData(CliSubCommand.McpSchema, "mcp_schema")]
    [InlineData(CliSubCommand.McpSearch, "mcp_search")]
    [InlineData(CliSubCommand.McpServe, "mcp_serve")]
    [InlineData(CliSubCommand.SlashCall, "slash_call")]
    [InlineData(CliSubCommand.SlashList, "slash_list")]
    [InlineData(CliSubCommand.SlashSchema, "slash_schema")]
    [InlineData(CliSubCommand.Rg, "rg")]
    public void FlatSubCommands_ShouldMapToExpectedStrings(CliSubCommand sub, string expected)
    {
        sub.ToValue().Should().Be(expected);
        CliSubCommandExtensions.FromValue(expected).Should().Be(sub);
    }
}
