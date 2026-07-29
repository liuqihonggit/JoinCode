namespace Sync.Tests.ToolHandlers;

public class TerminalCaptureToolHandlersTests
{
    private readonly TerminalCaptureToolHandlers _handler = new(NullLogger<TerminalCaptureToolHandlers>.Instance);

    [Fact]
    public async Task CaptureTerminalAsync_Default_ReturnsResult()
    {
        var result = await _handler.CaptureTerminalAsync("screen", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.NotNull(result.GetTextContent());
    }

    [Fact]
    public async Task CaptureTerminalAsync_Buffer_ReturnsResult()
    {
        var result = await _handler.CaptureTerminalAsync("buffer", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.NotNull(result.GetTextContent());
    }
}
