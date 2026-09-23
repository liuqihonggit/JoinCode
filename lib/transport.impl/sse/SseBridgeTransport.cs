namespace JoinCode.Transport.Bridge;

/// <summary>
/// SSE 传输实现
/// </summary>
public sealed class SseBridgeTransport : IBridgeTransport {
    private readonly string _endpoint;
    private readonly ILogger? _logger;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private string? _messageEndpoint;
    private volatile int _isStopped;

    /// <summary>接收到消息时触发</summary>
    public event EventHandler<TransportMessageReceivedEventArgs>? MessageReceived;
    /// <summary>发生错误时触发</summary>
    public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

    /// <summary>
    /// 构造 SSE 传输
    /// </summary>
    /// <param name="endpoint">SSE 端点 URL</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="httpClient">自定义 HTTP 客户端（可选，默认新建）</param>
    public SseBridgeTransport(string endpoint, ILogger? logger = null, HttpClient? httpClient = null) {
        _endpoint = endpoint;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// 启动 SSE 连接并开始接收循环
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public Task StartAsync(CancellationToken cancellationToken = default) {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _receiveTask = ReceiveSseLoopAsync(_cts.Token);

        _logger?.LogDebug("[SseBridgeTransport] 已启动 SSE 连接");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止 SSE 连接并释放资源
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        Interlocked.Exchange(ref _isStopped, 1);

        await (_cts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

        if (_receiveTask is not null) {
            try {
                await _receiveTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) {
            }
        }

        _cts?.Dispose();
        _cts = null;

        _logger?.LogDebug("[SseBridgeTransport] 已停止");
    }

    /// <summary>
    /// 通过 HTTP POST 发送消息到对端
    /// </summary>
    /// <param name="message">消息内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <exception cref="InvalidOperationException">传输未就绪时抛出</exception>
    public async Task SendAsync(string message, CancellationToken cancellationToken = default) {
        if (_isStopped != 0 || string.IsNullOrEmpty(_messageEndpoint)) {
            throw new InvalidOperationException(Core.Utils.ErrorMessages.SseTransportNotReady);
        }

        var content = new StringContent(message, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(_messageEndpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task ReceiveSseLoopAsync(CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested) {
            try {
                using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
                request.Headers.Add("Accept", "text/event-stream");
                request.Headers.Add("Cache-Control", "no-cache");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                await foreach (var sseEvent in SseStreamParser.ParseAsync(stream, cancellationToken).ConfigureAwait(false)) {
                    if (sseEvent.EventType == "endpoint") {
                        _messageEndpoint = sseEvent.Data;
                        _logger?.LogDebug("[SseBridgeTransport] 消息端点: {Endpoint}", _messageEndpoint);
                    } else {
                        MessageReceived?.Invoke(this, new TransportMessageReceivedEventArgs(sseEvent.Data));
                    }
                }
            } catch (OperationCanceledException) {
                break;
            } catch (Exception ex) {
                ErrorOccurred?.Invoke(this, new TransportErrorEventArgs(ex, "SSE 接收错误"));
                await Task.Delay(TransportConfiguration.DefaultRetryDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}