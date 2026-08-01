namespace Hands.Tests.Api.Vcr;

public sealed class VcrOptionsTests
{
    [Fact]
    public void DefaultValues_ShouldMatchExpected()
    {
        var options = new VcrOptions();

        options.Mode.Should().Be(VcrMode.None);
        options.CassettesDirectory.Should().Be("cassettes");
        options.RecordHeaders.Should().BeTrue();
        options.RecordContent.Should().BeTrue();
        options.RequestTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.StrictPlayback.Should().BeFalse();
        options.MaxCassetteSizeBytes.Should().Be(10 * 1024 * 1024);
    }
}
