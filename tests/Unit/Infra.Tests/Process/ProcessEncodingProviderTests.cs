namespace Infra.Tests.Process;

[Trait("Category", "Unit")]
public sealed class ProcessEncodingProviderTests
{
    [Fact]
    public void Default_IsUtf8Mode()
    {
        var provider = new ProcessEncodingProvider();

        provider.IsUtf8Mode.Should().BeTrue();
        provider.Output.WebName.Should().Be("utf-8");
        provider.Error.WebName.Should().Be("utf-8");
        provider.Input.WebName.Should().Be("utf-8");
    }

    [Fact]
    public void UseLocal_SwitchesToDefaultEncoding()
    {
        var provider = new ProcessEncodingProvider();

        provider.UseLocal();

        provider.IsUtf8Mode.Should().BeFalse();
        provider.Output.Should().BeSameAs(Encoding.Default);
        provider.Error.Should().BeSameAs(Encoding.Default);
        provider.Input.Should().BeSameAs(Encoding.Default);
    }

    [Fact]
    public void UseUtf8_RestoresUtf8Mode()
    {
        var provider = new ProcessEncodingProvider();
        provider.UseLocal();

        provider.UseUtf8();

        provider.IsUtf8Mode.Should().BeTrue();
        provider.Output.Should().BeSameAs(Encoding.UTF8);
        provider.Error.Should().BeSameAs(Encoding.UTF8);
        provider.Input.Should().BeSameAs(Encoding.UTF8);
    }

    [Fact]
    public void SetEncoding_AppliesToAllChannels()
    {
        var provider = new ProcessEncodingProvider();
        var custom = Encoding.ASCII;

        provider.SetEncoding(custom);

        provider.Output.Should().BeSameAs(Encoding.ASCII);
        provider.Error.Should().BeSameAs(Encoding.ASCII);
        provider.Input.Should().BeSameAs(Encoding.ASCII);
        provider.IsUtf8Mode.Should().BeFalse();
    }

    [Fact]
    public void SetEncoding_WithUtf8_SetsIsUtf8ModeTrue()
    {
        var provider = new ProcessEncodingProvider();
        provider.UseLocal();

        provider.SetEncoding(Encoding.UTF8);

        provider.IsUtf8Mode.Should().BeTrue();
    }

    [Fact]
    public void SetEncoding_Null_Throws()
    {
        var provider = new ProcessEncodingProvider();

        var act = () => provider.SetEncoding(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SwitchingEncoding_DoesNotAffectAlreadyCreatedProcessStartInfo()
    {
        var provider = new ProcessEncodingProvider();
        var builder = new ProcessStartInfoBuilder(provider);

        var psi1 = builder.Build(new ProcessOptions { FileName = "test" });
        provider.UseLocal();
        var psi2 = builder.Build(new ProcessOptions { FileName = "test" });

        psi1.StandardOutputEncoding.Should().BeSameAs(Encoding.UTF8);
        psi2.StandardOutputEncoding.Should().BeSameAs(Encoding.Default);
    }
}
