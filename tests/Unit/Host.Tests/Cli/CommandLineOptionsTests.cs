
using JoinCode;

namespace Host.Tests.Cli;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveExpectedDefaults()
    {
        var options = new CommandLineOptions();

        options.ShowHelp.Should().BeFalse();
        options.ShowVersion.Should().BeFalse();
        options.PipeName.Should().BeNull();
        options.IsPipeMode.Should().BeFalse();
        options.NonInteractive.Should().BeFalse();
        options.TrustWorkspace.Should().BeFalse();
        options.Prompt.Should().BeNull();
        options.Model.Should().BeNull();
        options.Brief.Should().BeFalse();
        options.ForceInteractive.Should().BeFalse();
        options.AwaitTimeoutSeconds.Should().BeNull();
        options.Verbose.Should().BeFalse();
    }

    [Fact]
    public void IsNonInteractiveMode_WithPrompt_ShouldBeTrue()
    {
        var options = new CommandLineOptions { Prompt = "hello" };

        options.IsNonInteractiveMode.Should().BeTrue();
    }

    [Fact]
    public void IsNonInteractiveMode_WithNonInteractiveFlag_ShouldBeTrue()
    {
        var options = new CommandLineOptions { NonInteractive = true };

        options.IsNonInteractiveMode.Should().BeTrue();
    }

    [Fact]
    public void IsNonInteractiveMode_WithForceInteractive_ShouldOverrideNonInteractive()
    {
        var options = new CommandLineOptions { NonInteractive = true, ForceInteractive = true };

        options.IsNonInteractiveMode.Should().BeFalse();
    }

    [Fact]
    public void IsPipeMode_WithPipeName_ShouldBeTrue()
    {
        var options = new CommandLineOptions { PipeName = "test-pipe" };

        options.IsPipeMode.Should().BeTrue();
    }

    [Fact]
    public void IsPipeMode_WithEmptyPipeName_ShouldBeFalse()
    {
        var options = new CommandLineOptions { PipeName = "" };

        options.IsPipeMode.Should().BeFalse();
    }

    [Fact]
    public void AwaitTimeoutSeconds_WithValidValue_ShouldBeSet()
    {
        var options = new CommandLineOptions { AwaitTimeoutSeconds = 5 };

        options.AwaitTimeoutSeconds.Should().Be(5);
    }

    [Fact]
    public void CliArgParser_ParseHelp_ShouldSetHelp()
    {
        var result = CliArgParser.Parse(new[] { "--help" });

        result.Help.Should().BeTrue();
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseVersion_ShouldSetVersion()
    {
        var result = CliArgParser.Parse(new[] { "--version" });

        result.Version.Should().BeTrue();
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParsePrompt_ShouldSetPrompt()
    {
        var result = CliArgParser.Parse(new[] { "--prompt", "hello world" });

        result.Prompt.Should().Be("hello world");
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseShortPrompt_ShouldSetPrompt()
    {
        var result = CliArgParser.Parse(new[] { "-p", "test" });

        result.Prompt.Should().Be("test");
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseModel_ShouldSetModel()
    {
        var result = CliArgParser.Parse(new[] { "--model", "gpt-4o" });

        result.Model.Should().Be("gpt-4o");
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseTrust_ShouldSetTrust()
    {
        var result = CliArgParser.Parse(new[] { "--trust" });

        result.Trust.Should().BeTrue();
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseNonInteractive_ShouldSetNonInteractive()
    {
        var result = CliArgParser.Parse(new[] { "--non-interactive" });

        result.NonInteractive.Should().BeTrue();
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseBrief_ShouldSetBrief()
    {
        var result = CliArgParser.Parse(new[] { "--brief" });

        result.Brief.Should().BeTrue();
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseForceInteractive_ShouldSetForceInteractive()
    {
        var result = CliArgParser.Parse(new[] { "--force-interactive" });

        result.ForceInteractive.Should().BeTrue();
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseAwait_ShouldSetAwait()
    {
        var result = CliArgParser.Parse(new[] { "--await", "10" });

        result.Await.Should().Be("10");
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseVerbose_ShouldSetVerbose()
    {
        var result = CliArgParser.Parse(new[] { "--verbose" });

        result.Verbose.Should().BeTrue();
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParsePipe_ShouldSetPipe()
    {
        var result = CliArgParser.Parse(new[] { "--pipe", "my-pipe" });

        result.Pipe.Should().Be("my-pipe");
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseMultipleOptions_ShouldSetAll()
    {
        var result = CliArgParser.Parse(new[] { "--trust", "--model", "gpt-4o", "--brief" });

        result.Trust.Should().BeTrue();
        result.Model.Should().Be("gpt-4o");
        result.Brief.Should().BeTrue();
        result.HasError.Should().BeFalse();
    }

    [Fact]
    public void CliArgParser_ParseEmpty_ShouldHaveNoErrors()
    {
        var result = CliArgParser.Parse(Array.Empty<string>());

        result.HasError.Should().BeFalse();
        result.Help.Should().BeFalse();
        result.Version.Should().BeFalse();
    }
}
