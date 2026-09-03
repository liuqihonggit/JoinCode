
namespace McpClient;

/// <summary>
/// MCP Stdio 客户端 - 通过标准输入输出与 MCP 服务器通信
/// </summary>
public sealed class McpStdioClient : McpClientBase
{
    private readonly McpServerConnectionConfig _config;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;
    private readonly IProcessService? _processService;
    private IInteractiveProcess? _interactiveProcess;
    private StreamWriter? _stdinWriter;
    private StreamReader? _stdoutReader;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;
    private ITelemetrySpan? _connectionSpan;

    public McpStdioClient(McpServerConnectionConfig config, McpClientOptions? options = null, ILogger? logger = null, ITelemetryService? telemetryService = null, IClockService? clock = null, IProcessService? processService = null)
        : base(options ?? new McpClientOptions(), logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
        _processService = processService;
        ServerName = _config.Name;
    }

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger?.LogWarning("MCP 客户端已连接");
            return;
        }

        _logger?.LogInformation("正在连接到 MCP 服务器: {ServerName}", _config.Name);

        _connectionSpan = _telemetryService?.StartSpan($"mcp.connect.{_config.Name}", TelemetrySpanKind.Client);
        _connectionSpan?.SetTag("mcp.server.name", _config.Name);
        _connectionSpan?.SetTag("mcp.server.endpoint", _config.Endpoint);

        var envVars = _config.Environment != null
            ? McpEnvExpander.ExpandEnvironmentValues(_config.Environment)
            : null;

        try
        {
            var effectiveProcessService = _processService ?? IO.ProcessService.ProcessServiceFactory.Create();
            var opts = new InteractiveProcessOptions
            {
                FileName = _config.Endpoint,
                EnvironmentVariables = envVars ?? new Dictionary<string, string>(),
                RedirectStandardError = true,
            };

            _interactiveProcess = await effectiveProcessService.StartInteractiveAsync(opts, cancellationToken);
            _interactiveProcess.ErrorDataReceived += OnInteractiveErrorDataReceived;

            _stdinWriter = _interactiveProcess.StandardInput;
            _stdoutReader = _interactiveProcess.StandardOutput;

            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token), _readCts.Token);

            await PerformHandshakeAsync(cancellationToken);

            IsConnected = true;
            _connectionSpan?.SetStatus(TelemetryStatusCode.Ok);
            _logger?.LogInformation("MCP 客户端连接成功");

            if (_telemetryService != null)
            {
                var connectCounter = _telemetryService.GetCounter("mcp.client.connects", "count", "MCP client connection count");
                connectCounter.Add(1, new Dictionary<string, string> { ["server"] = _config.Name });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "连接 MCP 服务器失败");
            _connectionSpan?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            _connectionSpan?.RecordException(ex);
            if (_connectionSpan is not null)
            {
                await _connectionSpan.DisposeAsync().ConfigureAwait(false);
                _connectionSpan = null;
            }

            if (_telemetryService != null)
            {
                var errorCounter = _telemetryService.GetCounter("mcp.client.connect.errors", "count", "MCP client connection error count");
                errorCounter.Add(1, new Dictionary<string, string> { ["server"] = _config.Name });
            }

            await CleanupAsync();
            throw;
        }
    }

    public override async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        _logger?.LogInformation("正在断开 MCP 客户端连接...");

        await CleanupAsync();
        IsConnected = false;

        _connectionSpan?.SetStatus(TelemetryStatusCode.Ok);
        if (_connectionSpan is not null)
        {
            await _connectionSpan.DisposeAsync().ConfigureAwait(false);
            _connectionSpan = null;
        }

        _logger?.LogInformation("MCP 客户端已断开连接");
    }

    private async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        _readCts?.Cancel();

        if (_readTask != null)
        {
            try
            {
                await _readTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("等待读取任务完成时出错: {Error}", ex.Message);
            }
        }

        if (_interactiveProcess != null)
        {
            await _interactiveProcess.DisposeAsync();
            _interactiveProcess = null;
        }

        _stdinWriter?.Dispose();
        _stdoutReader?.Dispose();

        _readCts?.Dispose();

        await CancelPendingRequestsAsync(cancellationToken);
    }

    private void OnInteractiveErrorDataReceived(object? sender, string data)
    {
        if (!string.IsNullOrEmpty(data))
        {
            _logger?.LogError("MCP 服务器错误输出: {Error}", data);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stdoutReader != null)
            {
                var line = await _stdoutReader.ReadLineAsync(cancellationToken);
                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                _logger?.LogDebug("收到消息: {Message}", line);

                try
                {
                    var message = McpMessageExtensions.FromJson(line);
                    await ProcessMessageAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "解析消息失败: {Message}", line);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "读取循环异常");
        }

        if (IsConnected && !cancellationToken.IsCancellationRequested)
        {
            _logger?.LogWarning("MCP Stdio 服务器进程意外退出: {ServerName}", _config.Name);
            OnConnectionLost(new McpConnectionLostEventArgs
            {
                ServerName = _config.Name,
                TransportType = "stdio"
            });
        }
    }

    private async Task ProcessMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case JsonRpcResponse response:
                await ProcessResponseAsync(response, cancellationToken);
                break;

            case JsonRpcNotification notification:
                ProcessNotification(notification);
                break;

            case JsonRpcRequest request:
                await HandleServerRequestAsync(request, cancellationToken);
                break;
        }
    }

    private void ProcessNotification(JsonRpcNotification notification)
    {
        _logger?.LogDebug("收到通知: {Method}", notification.Method);

        OnNotificationReceived(new McpNotificationReceivedEventArgs
        {
            Method = notification.Method,
            Params = notification.Params
        });
    }

    protected override async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (_stdinWriter == null)
        {
            throw new InvalidOperationException(McpErrorMessages.NotConnectedToServer);
        }

        await using var requestSpan = _telemetryService?.StartSpan($"mcp.request.{request.Method}", TelemetrySpanKind.Client, _connectionSpan);
        requestSpan?.SetTag("mcp.request.method", request.Method);
        requestSpan?.SetTag("mcp.request.id", request.Id.ToString());
        var requestStart = _clock.GetUtcNowOffset();

        var tcs = new TaskCompletionSource<JsonRpcResponse>();
        int requestId = request.GetIdAsInt();

        var guard = await _requestLock.LockAsync(cancellationToken);
        try
        {
            _pendingRequests[requestId] = tcs;
        }
        finally
        {
            guard.Dispose();
        }

        try
        {
            var json = request.ToJson();
            _logger?.LogDebug("发送请求: {Json}", json);

            var guard1 = await _requestLock.LockAsync(cancellationToken);
            try
            {
                await _stdinWriter.WriteLineAsync(json);
            }
            finally
            {
                guard1.Dispose();
            }

            using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

            var response = await tcs.Task.WaitAsync(cts.Token);

            requestSpan?.SetStatus(response.Error != null ? TelemetryStatusCode.Error : TelemetryStatusCode.Ok);
            RecordRequestMetrics(request.Method, requestStart);

            return response;
        }
        catch (Exception ex)
        {
            var guard2 = await _requestLock.LockAsync(cancellationToken);
            try
            {
                _pendingRequests.Remove(requestId);
            }
            finally
            {
                guard2.Dispose();
            }

            requestSpan?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            requestSpan?.RecordException(ex);
            RecordRequestMetrics(request.Method, requestStart, isError: true);

            throw;
        }
    }

    protected override async Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        if (_stdinWriter == null)
        {
            throw new InvalidOperationException(McpErrorMessages.NotConnectedToServer);
        }

        var json = notification.ToJson();
        _logger?.LogDebug("发送通知: {Json}", json);

        var guard3 = await _requestLock.LockAsync(cancellationToken);
        try
        {
            await _stdinWriter.WriteLineAsync(json);
        }
        finally
        {
            guard3.Dispose();
        }
    }

    private void RecordRequestMetrics(string method, DateTimeOffset startTime, bool isError = false)
    {
        var durationMs = (_clock.GetUtcNowOffset() - startTime).TotalMilliseconds;
        var tags = new Dictionary<string, string> { ["method"] = method, ["error"] = isError.ToString() };
        _telemetryService?.RecordHistogram("mcp.request.duration", durationMs, tags, "ms", "MCP request duration");
        _telemetryService?.RecordCount("mcp.request.count", tags, "count", "MCP request count");
    }

    public override async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        if (_connectionSpan is not null)
        {
            await _connectionSpan.DisposeAsync().ConfigureAwait(false);
            _connectionSpan = null;
        }
        _requestLock.Dispose();
    }
}
