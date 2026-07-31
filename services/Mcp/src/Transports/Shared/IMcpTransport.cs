
namespace McpClient.Transports;

public interface IMcpTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default);
    event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;
    event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;
    bool IsRunning { get; }
}

public class McpMessageReceivedEventArgs : EventArgs
{
    public required JsonRpcMessage Message { get; init; }
    public string? ConnectionId { get; init; }
}

public class McpTransportErrorEventArgs : EventArgs
{
    public required Exception Exception { get; init; }
    public string? ConnectionId { get; init; }
}
