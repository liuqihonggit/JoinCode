namespace JoinCode.Transport.Bridge;

/// <summary>
/// WebSocket 传输实现
/// </summary>
public sealed class WebSocketTransport : IBridgeTransport
{
    private readonly string _endpoint;
    private readonly ILogger? _logger;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public event EventHandler<TransportMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

    public WebSocketTransport(string endpoint, ILogger? logger = null)
    {
        _endpoint = endpoint;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _webSocket = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var uri = new Uri(_endpoint);
        await _webSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("[WebSocketTransport] 已连接到 {Endpoint}", _endpoint);

        _receiveTask = ReceiveLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await (_cts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_webSocket?.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken).ConfigureAwait(false);
        }

        _webSocket?.Dispose();
        _webSocket = null;
        _cts?.Dispose();
        _cts = null;

        _logger?.LogDebug("[WebSocketTransport] 已断开连接");
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket 未连接");
        }

        var bytes = Encoding.UTF8.GetBytes(message);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[TransportConfiguration.DefaultBufferSizeBytes];

        while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    MessageReceived?.Invoke(this, new TransportMessageReceivedEventArgs(message));
                }
            }
            catch (WebSocketException ex)
            {
                ErrorOccurred?.Invoke(this, new TransportErrorEventArgs(ex, "WebSocket 接收错误"));
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new TransportErrorEventArgs(ex, "接收消息时发生错误"));
            }
        }
    }
}
