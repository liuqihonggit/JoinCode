
namespace McpClient.Transports;

/// <summary>
/// Stdio MCP 传输 — 继承 TransportBase 共享连接管理内核，实现 IMcpTransport 桥接 JSON-RPC 协议
/// </summary>
public sealed partial class StdioTransport : TransportBase, IMcpTransport
{
    private readonly TextReader _input;
    private readonly TextWriter _output;
    [Inject] private readonly ILogger<StdioTransport>? _logger;

    /// <summary>IMcpTransport: 收到 JSON-RPC 消息</summary>
    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;

    /// <summary>IMcpTransport: 传输错误（隐藏基类同名事件，使用 MCP 专用参数类型）</summary>
    public new event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;

    public StdioTransport(ILogger<StdioTransport>? logger = null)
        : this(new StreamReader(System.Console.OpenStandardInput()), new StreamWriter(System.Console.OpenStandardOutput()) { AutoFlush = true }, logger)
    {
    }

    public StdioTransport(TextReader input, TextWriter output, ILogger<StdioTransport>? logger = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _logger = logger;
        _logger?.LogDebug("StdioTransport initialized");

        // 订阅基类事件，桥接到 MCP 事件
        base.ErrorOccurred += (_, e) =>
            ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs { Exception = e.Exception, ConnectionId = "stdio" });
    }

    /// <inheritdoc/>
    protected override Task ConnectCoreAsync(CancellationToken ct)
    {
        // Stdio 无需建立连接，输入输出流在构造时已注入
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override Task DisconnectCoreAsync(CancellationToken ct)
    {
        // Stdio 无需断开连接
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    protected override async Task SendCoreAsync(ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException(McpErrorMessages.TransportNotRunning);
        }

        var json = Encoding.UTF8.GetString(payload.Span);
        _logger?.LogDebug("Sending message - JSON: {Json}", json);

        await _output.WriteAsync(json).ConfigureAwait(false);
        await _output.WriteAsync('\n').ConfigureAwait(false);
        await _output.FlushAsync().ConfigureAwait(false);
        _logger?.LogDebug("Message sent successfully");
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
    public override async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;

        var token = CreateCtsAndToken();
        BackgroundTask = RunReadLoopAsync(token);
        IsRunning = true;
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsRunning) return;
        IsRunning = false;
        await GracefulStopAsync(ct).ConfigureAwait(false);
    }

    private async Task RunReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsRunning)
            {
                var message = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                if (message is not null)
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            MessageReceived?.Invoke(this, new McpMessageReceivedEventArgs
                            {
                                Message = message,
                                ConnectionId = "stdio"
                            });
                        }
                        catch (Exception ex)
                        {
                            ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs
                            {
                                Exception = ex,
                                ConnectionId = "stdio"
                            });
                        }
                    }, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new McpTransportErrorEventArgs
            {
                Exception = ex,
                ConnectionId = "stdio"
            });
        }
    }

    private async Task<JsonRpcMessage?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        _logger?.LogDebug("Waiting for message headers...");

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("Read line: '{Line}'", line ?? "(null)");

            if (line is null)
            {
                _logger?.LogDebug("Input stream returned null, waiting...");
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var trimmedLine = line.TrimStart();
            if (trimmedLine.StartsWith("{") || trimmedLine.StartsWith("["))
            {
                _logger?.LogDebug("Detected JSON message without headers");
                return ParseMessage(trimmedLine);
            }

            if (string.IsNullOrEmpty(line))
            {
                _logger?.LogDebug("Empty line received, headers complete");
                break;
            }

            var colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();
                headers[key] = value;
                _logger?.LogDebug("Header: {Key} = {Value}", key, value);
            }
        }

        if (!headers.TryGetValue("Content-Length", out var contentLengthStr) ||
            !int.TryParse(contentLengthStr, out var contentLength))
        {
            _logger?.LogDebug("No valid Content-Length header found");
            return null;
        }

        _logger?.LogDebug("Reading message body, Content-Length: {Length}", contentLength);

        var buffer = new char[contentLength];
        var totalRead = 0;

        while (totalRead < contentLength)
        {
            var read = await _input.ReadAsync(
                buffer.AsMemory(totalRead, contentLength - totalRead),
                cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                throw new IOException(McpErrorMessages.UnexpectedEndOfStream);
            }

            totalRead += read;
        }

        var json = new string(buffer);
        _logger?.LogDebug("Message body read: {Json}", json);

        return ParseMessage(json);
    }

    private JsonRpcMessage ParseMessage(string json)
    {
        try
        {
            var message = McpMessageExtensions.FromJson(json);
            _logger?.LogDebug("Message parsed successfully: {Type}", message.GetType().Name);
            return message;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse JSON-RPC message: {Json}", json);
            throw McpProtocolException.ParseError(json, ex);
        }
        catch (NotSupportedException ex)
        {
            _logger?.LogError(ex, "Unsupported message type: {Json}", json);
            throw McpProtocolException.ParseError(json, ex);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
