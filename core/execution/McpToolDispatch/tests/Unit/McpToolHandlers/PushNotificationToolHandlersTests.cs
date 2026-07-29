namespace Sync.Tests.ToolHandlers;

public class PushNotificationToolHandlersTests
{
    private readonly PushNotificationToolHandlers _handler = new();

    [Fact]
    public async Task PushNotificationAsync_EmptyTitle_ReturnsError()
    {
        var result = await _handler.PushNotificationAsync("", "msg", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("title", result.GetTextContent());
    }

    [Fact]
    public async Task PushNotificationAsync_EmptyMessage_ReturnsError()
    {
        var result = await _handler.PushNotificationAsync("title", "", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("message", result.GetTextContent());
    }

    [Fact]
    public async Task PushNotificationAsync_Valid_ReturnsSuccess()
    {
        var result = await _handler.PushNotificationAsync("Alert", "Something happened", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("通知已发送", result.GetTextContent());
        Assert.Contains("Alert", result.GetTextContent());
    }
}
