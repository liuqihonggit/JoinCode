namespace JoinCode.Transport.Bridge;

public interface IBridgeTransport
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendAsync(string message, CancellationToken cancellationToken = default);
    event EventHandler<TransportMessageReceivedEventArgs>? MessageReceived;
    event EventHandler<TransportErrorEventArgs>? ErrorOccurred;
}

public sealed class TransportMessageReceivedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
