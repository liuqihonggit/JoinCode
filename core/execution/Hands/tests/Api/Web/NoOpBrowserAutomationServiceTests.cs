namespace Core.Tests.Web;

public sealed class NoOpBrowserAutomationServiceTests
{
    private readonly NoOpBrowserAutomationService _service = new();

    [Fact]
    public void IsAvailable_ShouldBeFalse()
    {
        _service.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task ScreenshotAsync_ShouldReturnNotSupportedError()
    {
        var result = await _service.ScreenshotAsync("https://example.com").ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_ShouldReturnNotSupportedError()
    {
        var result = await _service.EvaluateAsync("https://example.com", "return 1").ConfigureAwait(true);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
