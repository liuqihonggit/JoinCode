namespace Sync.Tests.ToolHandlers;

public class ReplToolHandlersTests
{
    private readonly ReplToolHandlers _handler = new(NullLogger<ReplToolHandlers>.Instance);

    [Fact]
    public async Task ReplAsync_WithoutService_ReturnsError()
    {
        var result = await _handler.ReplAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("REPL", result.GetTextContent());
    }

    [Fact]
    public async Task ReplAsync_WithCode_WithoutService_ReturnsError()
    {
        var result = await _handler.ReplAsync(code: "Console.WriteLine(42);", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("REPL", result.GetTextContent());
    }

    [Fact]
    public async Task ReplAsync_EnableAction_WithoutService_ReturnsError()
    {
        var result = await _handler.ReplAsync(action: "enable", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ReplAsync_StatusAction_WithoutService_ReturnsError()
    {
        var result = await _handler.ReplAsync(action: "status", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
    }
}
