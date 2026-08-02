namespace Guard.Tests.Security.Services;

using Core.Security.Sandbox.Providers;

public sealed class ShellCommandEscapeTests
{
    [Fact]
    public void EscapeForSingleQuotedShell_PlainCommand_ShouldWrapInSingleQuotes()
    {
        var command = "echo hello";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);
        escaped.Should().Be("'echo hello'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_DoubleQuotedCommand_ShouldPreserveDoubleQuotes()
    {
        var command = "echo \"hello world\"";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);
        escaped.Should().Be("'echo \"hello world\"'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_SingleQuoteInCommand_ShouldEscapeWithEndRestartSequence()
    {
        var command = "echo 'hello world'";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);
        escaped.Should().Be(@"'echo '\''hello world'\'''");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_CommandInjection_DollarParentheses_ShouldNotBeInterpreted()
    {
        var command = "echo $(whoami)";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("'echo $(whoami)'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_CommandInjection_Backticks_ShouldNotBeInterpreted()
    {
        var command = "echo `whoami`";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("'echo `whoami`'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_CommandInjection_Semicolon_ShouldNotBeInterpreted()
    {
        var command = "echo hello; rm -rf /";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("'echo hello; rm -rf /'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_CommandInjection_Pipe_ShouldNotBeInterpreted()
    {
        var command = "echo hello | cat /etc/passwd";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("'echo hello | cat /etc/passwd'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_CommandInjection_Ampersand_ShouldNotBeInterpreted()
    {
        var command = "echo hello && rm -rf /";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("'echo hello && rm -rf /'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_CommandInjection_Newline_ShouldNotBeInterpreted()
    {
        var command = "echo hello\nrm -rf /";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("'echo hello\nrm -rf /'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_SingleQuoteInCommand_ShouldBeEscaped()
    {
        var command = "it's a test";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Contain(@"'\''");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_EmptyCommand_ShouldReturnEmptySingleQuotes()
    {
        var command = "";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("''");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_DollarVariable_ShouldNotExpand()
    {
        var command = "echo $HOME";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("'echo $HOME'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_Backslash_ShouldBeLiteral()
    {
        var command = @"echo C:\Users\test";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be(@"'echo C:\Users\test'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_DoubleQuoteInjection_ShouldBeLiteral()
    {
        var command = "echo \"$(whoami)\"";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("'echo \"$(whoami)\"'");
    }

    [Fact]
    public void EscapeForSingleQuotedShell_RedirectionInjection_ShouldBeLiteral()
    {
        var command = "echo hello > /tmp/evil";
        var escaped = ShellCommandEscape.EscapeForSingleQuotedShell(command);

        escaped.Should().Be("'echo hello > /tmp/evil'");
    }
}
