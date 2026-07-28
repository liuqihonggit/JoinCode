
namespace McpClient.Transports;

/// <summary>
/// 服务端 SSE 传输 — 继承 TransportBase 共享连接管理内核，实现 IMcpTransport 桥接 JSON-RPC 协议
/// 使用 HttpListener 监听连接，管理多个 SseClient，广播消息到所有客户端
/// </summary>
public sealed class SseTransport : TransportBase, IMcpTransport
{
    private readonly int _port;
    private readonly string _host;
    private HttpListener? _listener;
    private readonly Dictionary<string, SseClient> _clients = new();
    private readonly SemaphoreSlim _clientsLock = new(1, 1);

    /// <summary>IMcpTransport: 收到 JSON-RPC 消息</summary>
    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;

    /// <summary>IMcpTransport: 传输错误（隐藏基类同名事件，使用 MCP 专用参数类型）</summary>
    public new event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;

    public SseTransport(int port, string host = "localhost")
    {
        _port = port;
        _host = host;

        // 订阅基类事件，桥接到 MCP 事件
        base.ErrorOccurred += (_, e) =>
            ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs { Exception = e.Exception });
    }

    /// <inheritdoc/>
    protected override Task ConnectCoreAsync(CancellationToken ct)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{_host}:{_port}/");
        _listener.Start();

        var token = CreateCtsAndToken();
        BackgroundTask = RunAcceptLoopAsync(token);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override async Task DisconnectCoreAsync(CancellationToken ct)
    {
        await _clientsLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var clients = _clients.Values.ToList();
            _clients.Clear();
            await Task.WhenAll(clients.Select(c => c.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
        finally
        {
            _clientsLock.Release();
        }

        _listener?.Stop();
        _listener?.Close();
        _listener = null;
    }

    /// <inheritdoc/>
    protected override async Task SendCoreAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException(McpErrorMessages.TransportNotRunning);
        }

        // SSE 格式包装：data: {json}\n\n
        var sseData = $"data: {Encoding.UTF8.GetString(payload.Span)}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseData);

        List<SseClient> clients;

        await _clientsLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            clients = _clients.Values.ToList();
        }
        finally
        {
            _clientsLock.Release();
        }

        var clientsToRemove = new ConcurrentBag<SseClient>();

        var sendTasks = clients.Select(async client =>
        {
            try
            {
                await client.SendAsync(bytes, ct).ConfigureAwait(false);
            }
            catch
            {
                clientsToRemove.Add(client);
            }
        });

        await Task.WhenAll(sendTasks).ConfigureAwait(false);

        if (clientsToRemove.Count > 0)
        {
            await _clientsLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var client in clientsToRemove)
                {
                    _clients.Remove(client.ConnectionId);
                }
            }
            finally
            {
                _clientsLock.Release();
            }
            await Task.WhenAll(clientsToRemove.Select(c => c.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
    }

    /// <summary>IMcpTransport: 发送 JSON-RPC 消息（序列化为字节后委托给基类）</summary>
    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var json = message.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);
        await SendAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>SseTransport 是服务端传输，StartAsync 即启动 HttpListener 监听</remarks>
    public override Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;
        return base.StartAsync(ct);
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return;
        IsRunning = false;

        await DisconnectCoreAsync(ct).ConfigureAwait(false);
        await GracefulStopAsync(ct).ConfigureAwait(false);
    }

    private async Task RunAcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && IsRunning && _listener is not null)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(context, cancellationToken), cancellationToken);
            }
            catch (HttpListenerException) when (!IsRunning)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(ex);
                ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs { Exception = ex });
            }
        }
    }

    private async Task HandleClientAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        var response = context.Response;
        var connectionId = Guid.NewGuid().ToString("N");

        try
        {
            if (request.Url?.AbsolutePath == "/sse")
            {
                await HandleSseConnectionAsync(response, connectionId, cancellationToken).ConfigureAwait(false);
            }
            else if (request.Url?.AbsolutePath == "/message" && request.HttpMethod == "POST")
            {
                await HandleMessagePostAsync(request, response, connectionId, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                response.StatusCode = 404;
                response.Close();
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(ex);
            ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs
            {
                Exception = ex,
                ConnectionId = connectionId
            });

            try
            {
                response.StatusCode = 500;
                response.Close();
            }
            catch (Exception closeEx)
            {
                System.Diagnostics.Trace.WriteLine($"关闭 HTTP 响应失败: {closeEx.Message}");
            }
        }
    }

    private async Task HandleSseConnectionAsync(
        HttpListenerResponse response,
        string connectionId,
        CancellationToken cancellationToken)
    {
        response.ContentType = HttpContentType.TextEventStream.ToValue();
        response.Headers.Add("Cache-Control", "no-cache");
        response.Headers.Add("Connection", "keep-alive");
        response.StatusCode = 200;

        var outputStream = response.OutputStream;
        var client = new SseClient(connectionId, outputStream);

        await _clientsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _clients[connectionId] = client;
        }
        finally
        {
            _clientsLock.Release();
        }

        var endpointMessage = $"event: endpoint\ndata: /message?connectionId={connectionId}\n\n";
        await client.SendAsync(Encoding.UTF8.GetBytes(endpointMessage), cancellationToken).ConfigureAwait(false);

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await _clientsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _clients.Remove(connectionId);
            }
            finally
            {
                _clientsLock.Release();
            }

            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleMessagePostAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string connectionId,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var message = ParseMessage(json);

        if (message is not null)
        {
            // 通知基类收到字节载荷
            OnPayloadReceived(Encoding.UTF8.GetBytes(json));

            _ = Task.Run(() =>
            {
                try
                {
                    MessageReceived?.Invoke(this, new McpMessageReceivedEventArgs
                    {
                        Message = message,
                        ConnectionId = connectionId
                    });
                }
                catch (Exception ex)
                {
                    OnErrorOccurred(ex);
                    ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs
                    {
                        Exception = ex,
                        ConnectionId = connectionId
                    });
                }
            }, cancellationToken);
        }

        response.StatusCode = 202;
        response.Close();
    }

    private static JsonRpcMessage? ParseMessage(string json)
    {
        try
        {
            return McpMessageExtensions.FromJson(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        _clientsLock.Dispose();
    }
}

internal sealed class SseClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public string ConnectionId { get; }
    public Stream OutputStream { get; }

    public SseClient(string connectionId, Stream outputStream)
    {
        ConnectionId = connectionId;
        OutputStream = outputStream;
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SseClient));
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await OutputStream.WriteAsync(data, cancellationToken);
            await OutputStream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeLock.Dispose();

        try
        {
            await OutputStream.DisposeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"释放 SSE 输出流失败: {ex.Message}");
        }
    }
}
