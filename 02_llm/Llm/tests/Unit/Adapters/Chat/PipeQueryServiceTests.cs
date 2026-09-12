namespace Llm.Tests.Adapters.Chat;


public class PipeQueryServiceTests
{
    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        var act = () => new PipeQueryService(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithConfig_DoesNotThrow()
    {
        var config = new PipeTransportConfig { PipeName = "test-pipe" };

        var act = () => new PipeQueryService(config);

        act.Should().NotThrow();
    }
}
