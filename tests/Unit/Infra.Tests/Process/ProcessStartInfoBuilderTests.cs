namespace Infra.Tests.Process;

[Trait("Category", "Unit")]
public sealed class ProcessStartInfoBuilderTests
{
    private static ProcessStartInfoBuilder CreateBuilder()
    {
        return new ProcessStartInfoBuilder(new ProcessEncodingProvider());
    }

    [Fact]
    public void Build_WithArgumentList_UsesArgumentListNotArguments()
    {
        var builder = CreateBuilder();

        var psi = builder.Build(new ProcessOptions
        {
            FileName = "dotnet",
            Arguments = "--should-be-ignored",
            ArgumentList = ["build", "-c", "Release"],
        });

        psi.ArgumentList.Should().HaveCount(3);
        psi.ArgumentList[0].Should().Be("build");
        psi.ArgumentList[1].Should().Be("-c");
        psi.ArgumentList[2].Should().Be("Release");
        psi.Arguments.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithoutArgumentList_UsesArguments()
    {
        var builder = CreateBuilder();

        var psi = builder.Build(new ProcessOptions
        {
            FileName = "dotnet",
            Arguments = "--version",
        });

        psi.Arguments.Should().Be("--version");
        psi.ArgumentList.Should().BeEmpty();
    }

    [Fact]
    public void Build_EnforcesUseShellExecuteFalse()
    {
        var builder = CreateBuilder();

        var psi = builder.Build(new ProcessOptions { FileName = "test" });

        psi.UseShellExecute.Should().BeFalse();
        psi.CreateNoWindow.Should().BeTrue();
    }

    [Fact]
    public void Build_AppliesEncodingFromProvider()
    {
        var provider = new ProcessEncodingProvider();
        var builder = new ProcessStartInfoBuilder(provider);

        var psi = builder.Build(new ProcessOptions { FileName = "test" });

        psi.StandardOutputEncoding.Should().BeSameAs(Encoding.UTF8);
        psi.StandardErrorEncoding.Should().BeSameAs(Encoding.UTF8);
    }

    [Fact]
    public void Build_OptionsEncodingOverridesProvider()
    {
        var provider = new ProcessEncodingProvider();
        provider.UseLocal();
        var builder = new ProcessStartInfoBuilder(provider);
        var custom = Encoding.ASCII;

        var psi = builder.Build(new ProcessOptions
        {
            FileName = "test",
            StandardOutputEncoding = custom,
        });

        psi.StandardOutputEncoding.Should().BeSameAs(Encoding.ASCII);
        psi.StandardErrorEncoding.Should().BeSameAs(Encoding.Default);
    }

    [Fact]
    public void Build_WithDangerousArgument_Throws()
    {
        var builder = CreateBuilder();

        var act = () => builder.Build(new ProcessOptions
        {
            FileName = "test",
            Arguments = "echo $HOME",
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*危险字符*");
    }

    [Fact]
    public void Build_WithDangerousArgumentList_Throws()
    {
        var builder = CreateBuilder();

        var act = () => builder.Build(new ProcessOptions
        {
            FileName = "test",
            ArgumentList = ["safe", "danger;ous"],
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*危险字符*");
    }

    [Fact]
    public void Build_WithSkipValidation_DoesNotThrow()
    {
        var builder = CreateBuilder();

        var psi = builder.Build(new ProcessOptions
        {
            FileName = "bash",
            Arguments = "-c \"echo hello && echo world\"",
            SkipArgumentValidation = true,
        });

        psi.Arguments.Should().Contain("&&");
    }

    [Fact]
    public void Build_WithEnvironmentVariables_SetsAll()
    {
        var builder = CreateBuilder();

        var psi = builder.Build(new ProcessOptions
        {
            FileName = "test",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["FOO"] = "bar",
                ["BAZ"] = "qux",
            },
        });

        psi.EnvironmentVariables["FOO"].Should().Be("bar");
        psi.EnvironmentVariables["BAZ"].Should().Be("qux");
    }

    [Fact]
    public void BuildInteractive_RedirectsAllStreams()
    {
        var builder = CreateBuilder();

        var psi = builder.BuildInteractive(new InteractiveProcessOptions
        {
            FileName = "test",
        });

        psi.RedirectStandardOutput.Should().BeTrue();
        psi.RedirectStandardInput.Should().BeTrue();
        psi.RedirectStandardError.Should().BeTrue();
        psi.StandardInputEncoding.Should().NotBeNull();
    }

    [Fact]
    public void BuildInteractive_WithArgumentList_UsesArgumentList()
    {
        var builder = CreateBuilder();

        var psi = builder.BuildInteractive(new InteractiveProcessOptions
        {
            FileName = "jcc",
            ArgumentList = ["--print", "--session-id", "abc123"],
        });

        psi.ArgumentList.Should().HaveCount(3);
        psi.ArgumentList[1].Should().Be("--session-id");
    }

    [Fact]
    public void BuildShellOpen_UsesShellExecuteTrue()
    {
        var builder = CreateBuilder();

        var psi = builder.BuildShellOpen("https://example.com");

        psi.UseShellExecute.Should().BeTrue();
        psi.FileName.Should().Be("https://example.com");
    }

    [Fact]
    public void BuildShellOpen_WithEmptyPath_Throws()
    {
        var builder = CreateBuilder();

        var act = () => builder.BuildShellOpen("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_LocalEncodingMode_AppliesLocalEncoding()
    {
        var provider = new ProcessEncodingProvider();
        var builder = new ProcessStartInfoBuilder(provider);
        provider.UseLocal();

        var psi = builder.Build(new ProcessOptions { FileName = "test" });

        psi.StandardOutputEncoding.Should().BeSameAs(Encoding.Default);
    }
}
