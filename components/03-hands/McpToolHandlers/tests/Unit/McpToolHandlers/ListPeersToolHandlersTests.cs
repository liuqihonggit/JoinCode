namespace Sync.Tests.ToolHandlers;

public class ListPeersToolHandlersTests
{
    private readonly ListPeersToolHandlers _handler = new(NullLogger<ListPeersToolHandlers>.Instance);

    [Fact]
    public async Task ListPeersAsync_Default_ReturnsSuccess()
    {
        var result = await _handler.ListPeersAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("对等节点列表", result.GetTextContent());
    }
}
