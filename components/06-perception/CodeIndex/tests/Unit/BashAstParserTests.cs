using JoinCode.CodeIndex.Ast;

namespace JoinCode.CodeIndex.Tests;

public class BashAstParserTests
{
    [Fact]
    public void Parse_SimpleCommand_ReturnsCorrectArgv()
    {
        using var parser = new BashAstParser();
        var root = parser.Parse("echo hello world");
        Assert.NotNull(root);

        var commands = BashAstParser.ExtractSimpleCommands(root);
        Assert.Single(commands);
        Assert.Equal("echo", commands[0].Argv[0]);
        Assert.Equal("hello", commands[0].Argv[1]);
        Assert.Equal("world", commands[0].Argv[2]);
    }

    [Fact]
    public void Parse_Pipeline_ReturnsTwoCommands()
    {
        using var parser = new BashAstParser();
        var root = parser.Parse("cat file.txt | grep pattern");
        Assert.NotNull(root);

        var commands = BashAstParser.ExtractSimpleCommands(root);
        Assert.Equal(2, commands.Count);
        Assert.Equal("cat", commands[0].Argv[0]);
        Assert.Equal("grep", commands[1].Argv[0]);
    }

    [Fact]
    public void Parse_AndOrChain_ReturnsThreeCommands()
    {
        using var parser = new BashAstParser();
        var root = parser.Parse("cd /repo && make build || echo failed");
        Assert.NotNull(root);

        var commands = BashAstParser.ExtractSimpleCommands(root);
        Assert.Equal(3, commands.Count);
        Assert.Equal("cd", commands[0].Argv[0]);
        Assert.Equal("make", commands[1].Argv[0]);
        Assert.Equal("echo", commands[2].Argv[0]);
    }

    [Fact]
    public void Parse_Redirect_ExtractsRedirects()
    {
        using var parser = new BashAstParser();
        var root = parser.Parse("echo hello > output.txt");
        Assert.NotNull(root);

        var commands = BashAstParser.ExtractSimpleCommands(root);
        Assert.Single(commands);
        Assert.Equal("echo", commands[0].Argv[0]);
        Assert.NotEmpty(commands[0].Redirects);
    }

    [Fact]
    public void Parse_VariableAssignment_ExtractsEnvVars()
    {
        using var parser = new BashAstParser();
        var root = parser.Parse("VAR=value echo hello");
        Assert.NotNull(root);

        var commands = BashAstParser.ExtractSimpleCommands(root);
        Assert.Single(commands);
        Assert.Equal("echo", commands[0].Argv[0]);
        Assert.NotEmpty(commands[0].EnvVars);
        Assert.Equal("VAR", commands[0].EnvVars[0].Name);
    }

    [Fact]
    public void Parse_CommandSubstitution_Detected()
    {
        using var parser = new BashAstParser();
        var root = parser.Parse("echo $(date)");
        Assert.NotNull(root);

        var commands = BashAstParser.ExtractSimpleCommands(root);
        Assert.Single(commands);
        // $(date) should appear in argv
        Assert.True(commands[0].Argv.Length >= 2);
    }

    [Fact]
    public void Parse_QuotedString_StripsQuotes()
    {
        using var parser = new BashAstParser();
        var root = parser.Parse("echo 'hello world'");
        Assert.NotNull(root);

        var commands = BashAstParser.ExtractSimpleCommands(root);
        Assert.Single(commands);
        Assert.Equal("echo", commands[0].Argv[0]);
        Assert.Equal("hello world", commands[0].Argv[1]);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsNull()
    {
        using var parser = new BashAstParser();
        var result = parser.Parse("");
        Assert.Null(result);
    }

    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        using var parser = new BashAstParser();
        var result = parser.Parse(null!);
        Assert.Null(result);
    }

    [Fact]
    public void Parse_DangerousRm_ExtractsArgv()
    {
        using var parser = new BashAstParser();
        var root = parser.Parse("rm -rf /");
        Assert.NotNull(root);

        var commands = BashAstParser.ExtractSimpleCommands(root);
        Assert.Single(commands);
        Assert.Equal("rm", commands[0].Argv[0]);
        Assert.Equal("-rf", commands[0].Argv[1]);
        Assert.Equal("/", commands[0].Argv[2]);
    }

    [Fact]
    public void Parse_ComplexPipeline_ExtractsAllCommands()
    {
        using var parser = new BashAstParser();
        var root = parser.Parse("find . -name '*.cs' | xargs grep 'TODO' | sort | uniq -c");
        Assert.NotNull(root);

        var commands = BashAstParser.ExtractSimpleCommands(root);
        Assert.Equal(4, commands.Count);
        Assert.Equal("find", commands[0].Argv[0]);
        Assert.Equal("xargs", commands[1].Argv[0]);
        Assert.Equal("sort", commands[2].Argv[0]);
        Assert.Equal("uniq", commands[3].Argv[0]);
    }
}
