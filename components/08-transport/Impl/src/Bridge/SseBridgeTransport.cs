namespace JoinCode.Transport.Bridge;

/// <summary>
/// SSE 传输实现
/// </summary>
public sealed class SseBridgeTransport : IBridgeTransport
{
    private readonly string _endpoint;
    private readonly ILogger? _logger;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private string? _messageEndpoint;
    private volatile int _isStopped;

    public event EventHandler<TransportMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

    public SseBridgeTransport(string endpoint, ILogger? logger = null, HttpClient? httpClient = null)
    {
        _endpoint = endpoint;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _receiveTask = ReceiveSseLoopAsync(_cts.Token);

        _logger?.LogDebug("[SseBridgeTransport] 已启动 SSE 连接");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _isStopped, 1);

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

        _cts?.Dispose();
        _cts = null;

        _logger?.LogDebug("[SseBridgeTransport] 已停止");
    }

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_isStopped != 0 || string.IsNullOrEmpty(_messageEndpoint))
        {
            throw new InvalidOperationException("SSE 传输未就绪");
        }

        var content = new StringContent(message, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_messageEndpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task ReceiveSseLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
                request.Headers.Add("Accept", "text/event-stream");
                request.Headers.Add("Cache-Control", "no-cache");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                        break;

                    if (string.IsNullOrEmpty(line))
                        continue;

                    if (line.StartsWith("event: endpoint"))
                    {
                        var dataLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                        if (dataLine?.StartsWith("data: ") == true)
                        {
                            _messageEndpoint = dataLine[6..];
                            _logger?.LogDebug("[SseBridgeTransport] 消息端点: {Endpoint}", _messageEndpoint);
                        }
                    }
                    else if (line.StartsWith("data: "))
                    {
                        var data = line[6..];
                        MessageReceived?.Invoke(this, new TransportMessageReceivedEventArgs(data));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new TransportErrorEventArgs(ex, "SSE 接收错误"));
                await Task.Delay(TransportConfiguration.DefaultRetryDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
