
namespace Core.Bridge;

using JoinCode.Abstractions.Attributes;

// BridgeServerMessage, BridgeConnectedData, BridgeHealthData, BridgeClientsData,
// BridgeErrorData, BridgeFileContentData, BridgeSelectionSetData, BridgeCommandExecutedData
// 已迁移到 JoinCode.Transport 命名空间 (Transport.Contracts)

public sealed partial class BridgeSessionListData
{
    [JsonPropertyName("sessions")]
    public required List<BridgeSession> Sessions { get; init; }
}

/// <summary>
/// 桥接服务器 - 与 IDE 扩展通信
/// </summary>
[Register]
public sealed partial class BridgeServer : IDisposable
{
    private readonly HttpListener _httpListener;
    private readonly ConcurrentDictionary<string, WebSocket> _clients;
    [Inject] private readonly ILogger<BridgeServer>? _logger;
    private readonly IClockService _clock;
    private readonly IFileOperationService _fileOperationService;
    private readonly CancellationTokenSource _cts;
    private readonly int _port;
    private Task? _listenerTask;
    private readonly BridgeJwtService? _jwtService;
    private readonly BridgeSessionRunner? _sessionRunner;
    private readonly ITrustedDeviceStore? _trustedDeviceStore;
    private readonly PeerSessionManager? _peerSessionManager;
    private readonly BridgeUIService? _bridgeUIService;
    private readonly IShellExecutionService? _shellService;
    private readonly IIdeIntegrationService? _ideService;
    private FlushGate<BridgeServerMessage>? _outgoingFlushGate;
    private volatile int _gateActive;
    private readonly ConcurrentDictionary<string, Func<HttpListenerContext, CancellationToken, Task>> _customRoutes = new();

    public BridgeServer(
        IFileOperationService fileOperationService,
        BridgeServerSecurity? security = null,
        BridgeServerSession? session = null,
        int port = 3456,
        ILogger<BridgeServer>? logger = null,
        IClockService? clock = null,
        IShellExecutionService? shellService = null,
        IIdeIntegrationService? ideService = null)
    {
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _port = port;
        _jwtService = security?.JwtService;
        _sessionRunner = session?.SessionRunner;
        _trustedDeviceStore = security?.TrustedDeviceStore;
        _peerSessionManager = session?.PeerSessionManager;
        _bridgeUIService = session?.UIService;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _shellService = shellService;
        _ideService = ideService;
        _clients = new ConcurrentDictionary<string, WebSocket>();
        _cts = new CancellationTokenSource();
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://localhost:{port}/");

        // 初始化 FlushGate 用于批量发送消息
        _outgoingFlushGate = new FlushGate<BridgeServerMessage>(
            new FlushGateOptions
            {
                MaxBatchSize = 50,
                FlushIntervalMs = 100
            },
            logger);
        _outgoingFlushGate.BatchFlushed += OnOutgoingBatchFlushed;

        // 订阅对等会话消息事件
        if (_peerSessionManager != null)
        {
            _peerSessionManager.PeerMessageSent += OnPeerMessageSent;
        }
    }

    /// <summary>
    /// 注册自定义 HTTP 路由处理器
    /// </summary>
    /// <param name="path">路由路径（如 "/code-sessions"）</param>
    /// <param name="handler">请求处理委托</param>
    public void RegisterRoute(string path, Func<HttpListenerContext, CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(handler);

        _customRoutes[path] = handler;
        _logger?.LogInformation("[BridgeServer] 注册自定义路由: {Path}", path);
    }

    /// <summary>
    /// 启动服务器
    /// </summary>
    public void Start()
    {
        if (_listenerTask != null)
        {
            _logger?.LogWarning("[BridgeServer] 服务器已在运行");
            return;
        }

        _httpListener.Start();

        // 启动 FlushGate 定时刷新循环
        if (_outgoingFlushGate != null)
        {
            _ = _outgoingFlushGate.StartAsync(_cts.Token);
            _gateActive = 1;
        }

        _logger?.LogInformation("[BridgeServer] 服务器已启动，端口: {Port}", _port);

        _listenerTask = Task.Run(ListenAsync);
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts.Cancel();

        // 先停止 FlushGate，刷新剩余消息后再关闭连接
        _gateActive = 0;
        if (_outgoingFlushGate != null)
        {
            await _outgoingFlushGate.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        _httpListener.Stop();

        await Task.WhenAll(_clients.Values
            .Select(client => client.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Server shutting down",
                cancellationToken)
                .ContinueWith(_ => { }, cancellationToken))).ConfigureAwait(false);

        _clients.Clear();

        if (_listenerTask != null)
        {
            await _listenerTask.ConfigureAwait(false);
            _listenerTask = null;
        }

        _logger?.LogInformation("[BridgeServer] 服务器已停止");
    }

    /// <summary>
    /// 监听连接
    /// </summary>
    private async Task ListenAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var context = await _httpListener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context, _cts.Token));
            }
            catch (HttpListenerException) when (_cts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[BridgeServer] 处理请求失败");
            }
        }
    }

    /// <summary>
    /// 处理请求
    /// </summary>
    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (context.Request.IsWebSocketRequest)
        {
            await HandleWebSocketAsync(context, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await HandleHttpRequestAsync(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理 WebSocket 连接
    /// </summary>
    private async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        string clientId = Guid.NewGuid().ToString("N")[..8];

        try
        {
            var wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
            var webSocket = wsContext.WebSocket;

            _clients[clientId] = webSocket;
            _logger?.LogInformation("[BridgeServer] 客户端 {ClientId} 已连接", clientId);

            // Verify JWT token if available
            if (_jwtService != null)
            {
                var token = context.Request.Headers["Authorization"]?.Replace("Bearer ", "");
                if (!string.IsNullOrEmpty(token))
                {
                    var validationResult = _jwtService.ValidateToken(token);
                    if (!validationResult.IsValid)
                    {
                        _logger?.LogWarning("[BridgeServer] 客户端 JWT 验证失败: {Error}", validationResult.Error);
                        await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Authentication failed", cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    clientId = validationResult.Payload?.Sub ?? clientId;
                    _logger?.LogInformation("[BridgeServer] 客户端 JWT 验证通过: {ClientId}", clientId);
                }
            }

            // Check device trust if available
            if (_trustedDeviceStore != null)
            {
                var deviceId = context.Request.Headers["X-Device-Id"];
                if (!string.IsNullOrEmpty(deviceId) && !await _trustedDeviceStore.IsTrustedAsync(deviceId).ConfigureAwait(false))
                {
                    _logger?.LogWarning("[BridgeServer] 设备未受信任: {DeviceId}", deviceId);
                    await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Device not trusted", cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            // Register session if runner is available
            if (_sessionRunner != null)
            {
                await _sessionRunner.StartSessionAsync(clientId, new Dictionary<string, string> { ["transport"] = "websocket" }).ConfigureAwait(false);
            }

            // 发送欢迎消息
            await SendMessageAsync(clientId, new BridgeServerMessage
            {
                Type = "connected",
                Data = JsonSerializer.SerializeToElement(new BridgeConnectedData { ClientId = clientId, Version = "1.0" }, BridgeJsonContext.Default.BridgeConnectedData)
            }, cancellationToken).ConfigureAwait(false);

            // 通知 UI 服务
            _bridgeUIService?.RegisterSession(new BridgeSessionDisplay
            {
                SessionId = clientId,
                ClientName = clientId,
                Status = "connected",
                ConnectedAt = _clock.GetUtcNowOffset().ToUnixTimeMilliseconds()
            });

            // 接收消息循环
            var buffer = new byte[WorkflowConstants.Limits.BufferSizeBytes];
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleMessageAsync(clientId, message, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (WebSocketException ex)
        {
            _logger?.LogError(ex, "[BridgeServer] WebSocket 错误");
        }
        finally
        {
            // Close session if runner is available
            if (_sessionRunner != null)
            {
                var activeSessions = _sessionRunner.GetActiveSessions();
                var session = activeSessions.FirstOrDefault(s => s.ClientId == clientId);
                if (session != null)
                {
                    await _sessionRunner.StopSessionAsync(session.SessionId).ConfigureAwait(false);
                }
            }

            _bridgeUIService?.UnregisterSession(clientId);
            _clients.TryRemove(clientId, out _);
            _logger?.LogInformation("[BridgeServer] 客户端 {ClientId} 已断开", clientId);
        }
    }

    /// <summary>
    /// 处理 HTTP 请求
    /// </summary>
    private async Task HandleHttpRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;

        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            if (_customRoutes.TryGetValue(path, out var customHandler))
            {
                await customHandler(context, _cts.Token).ConfigureAwait(false);
                return;
            }

            switch (path)
            {
                case "/health":
                    await WriteJsonResponseAsync(response, new BridgeHealthData { Status = "ok", Clients = _clients.Count }, BridgeJsonContext.Default.BridgeHealthData).ConfigureAwait(false);
                    break;

                case "/clients":
                    await WriteJsonResponseAsync(response, new BridgeClientsData { Clients = _clients.Keys.ToList() }, BridgeJsonContext.Default.BridgeClientsData).ConfigureAwait(false);
                    break;

                case "/sessions":
                    if (_sessionRunner != null)
                    {
                        var sessions = _sessionRunner.GetActiveSessions().ToList();
                        await WriteJsonResponseAsync(response, new BridgeSessionListData { Sessions = sessions }, BridgeJsonContext.Default.BridgeSessionListData).ConfigureAwait(false);
                    }
                    else
                    {
                        response.StatusCode = 404;
                        await WriteJsonResponseAsync(response, new BridgeErrorData { Error = "Session management not available" }, BridgeJsonContext.Default.BridgeErrorData).ConfigureAwait(false);
                    }
                    break;

                default:
                    response.StatusCode = 404;
                    await WriteJsonResponseAsync(response, new BridgeErrorData { Error = "Not found" }, BridgeJsonContext.Default.BridgeErrorData).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeServer] 处理 HTTP 请求失败");
            response.StatusCode = 500;
            await WriteJsonResponseAsync(response, new BridgeErrorData { Error = ex.Message }, BridgeJsonContext.Default.BridgeErrorData).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理消息
    /// </summary>
    private async Task HandleMessageAsync(string clientId, string messageJson, CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize(messageJson, BridgeJsonContext.Default.BridgeServerMessage);

            if (message == null)
            {
                await SendMessageAsync(clientId, new BridgeServerMessage
                {
                    Type = "error",
                    Data = JsonSerializer.SerializeToElement(new BridgeErrorData { Error = "Invalid message format" }, BridgeJsonContext.Default.BridgeErrorData)
                }, cancellationToken).ConfigureAwait(false);
                return;
            }

            _logger?.LogDebug("[BridgeServer] 收到消息 [{Type}] 来自 {ClientId}", message.Type, clientId);

            switch (message.Type)
            {
                case "ping":
                    await SendMessageAsync(clientId, new BridgeServerMessage { Type = "pong" }, cancellationToken).ConfigureAwait(false);
                    break;

                case "getFile":
                    await HandleGetFileAsync(clientId, message, cancellationToken).ConfigureAwait(false);
                    break;

                case "setSelection":
                    await HandleSetSelectionAsync(clientId, message, cancellationToken).ConfigureAwait(false);
                    break;

                case "executeCommand":
                    await HandleExecuteCommandAsync(clientId, message, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    await SendMessageAsync(clientId, new BridgeServerMessage
                    {
                        Type = "error",
                        Data = JsonSerializer.SerializeToElement(new BridgeErrorData { Error = $"Unknown message type: {message.Type}" }, BridgeJsonContext.Default.BridgeErrorData)
                    }, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeServer] 处理消息失败");
            await SendMessageAsync(clientId, new BridgeServerMessage
            {
                Type = "error",
                Data = JsonSerializer.SerializeToElement(new BridgeErrorData { Error = ex.Message }, BridgeJsonContext.Default.BridgeErrorData)
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理获取文件请求
    /// </summary>
    private async Task HandleGetFileAsync(string clientId, BridgeServerMessage message, CancellationToken cancellationToken)
    {
        var path = message.Data?.GetProperty("path").GetString();

        if (string.IsNullOrEmpty(path) || !_fileOperationService.FileExists(path))
        {
            await SendMessageAsync(clientId, new BridgeServerMessage
            {
                Type = "fileContent",
                Data = JsonSerializer.SerializeToElement(new BridgeFileContentData { Path = path ?? string.Empty, Error = "File not found" }, BridgeJsonContext.Default.BridgeFileContentData)
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var readResult = await _fileOperationService.ReadFileAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (readResult.Success)
            {
                await SendMessageAsync(clientId, new BridgeServerMessage
                {
                    Type = "fileContent",
                    Data = JsonSerializer.SerializeToElement(new BridgeFileContentData { Path = path, Content = readResult.Content }, BridgeJsonContext.Default.BridgeFileContentData)
                }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await SendMessageAsync(clientId, new BridgeServerMessage
                {
                    Type = "fileContent",
                    Data = JsonSerializer.SerializeToElement(new BridgeFileContentData { Path = path, Error = readResult.ErrorMessage }, BridgeJsonContext.Default.BridgeFileContentData)
                }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await SendMessageAsync(clientId, new BridgeServerMessage
            {
                Type = "fileContent",
                Data = JsonSerializer.SerializeToElement(new BridgeFileContentData { Path = path, Error = ex.Message }, BridgeJsonContext.Default.BridgeFileContentData)
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 处理设置选区请求 — 调用 IIdeIntegrationService.SetSelectionAsync 定位光标
    /// </summary>
    private async Task HandleSetSelectionAsync(string clientId, BridgeServerMessage message, CancellationToken cancellationToken)
    {
        var response = await BuildSetSelectionResponseAsync(message, cancellationToken).ConfigureAwait(false);
        await SendMessageAsync(clientId, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 处理执行命令请求 — 调用 IShellExecutionService.ExecuteAsync 执行命令
    /// </summary>
    private async Task HandleExecuteCommandAsync(string clientId, BridgeServerMessage message, CancellationToken cancellationToken)
    {
        var response = await BuildExecuteCommandResponseAsync(message, cancellationToken).ConfigureAwait(false);
        await SendMessageAsync(clientId, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建 setSelection 响应消息 — internal 便于单元测试
    /// 决策：SetSelection 通过 IDE CLI --goto 实现，不引入 LSP
    /// </summary>
    internal async Task<BridgeServerMessage> BuildSetSelectionResponseAsync(BridgeServerMessage message, CancellationToken cancellationToken)
    {
        var data = message.Data;
        var filePath = data?.GetProperty("file").GetString();
        var startLine = data?.GetProperty("startLine").GetInt32() ?? 0;
        var startCol = data?.GetProperty("startCol").GetInt32() ?? 1;
        var endLine = data?.GetProperty("endLine").GetInt32() ?? startLine;
        var endCol = data?.GetProperty("endCol").GetInt32() ?? startCol;

        if (_ideService is null)
        {
            _logger?.LogDebug("[BridgeServer] setSelection 请求: IDE 服务未注入");
            return new BridgeServerMessage
            {
                Type = "selectionSet",
                Data = JsonSerializer.SerializeToElement(new BridgeSelectionSetData { Success = false, Error = "IDE 服务未注入" }, BridgeJsonContext.Default.BridgeSelectionSetData)
            };
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new BridgeServerMessage
            {
                Type = "selectionSet",
                Data = JsonSerializer.SerializeToElement(new BridgeSelectionSetData { Success = false, Error = "文件路径为空" }, BridgeJsonContext.Default.BridgeSelectionSetData)
            };
        }

        try
        {
            var ok = await _ideService.SetSelectionAsync(filePath, startLine, startCol, endLine, endCol, cancellationToken).ConfigureAwait(false);
            return new BridgeServerMessage
            {
                Type = "selectionSet",
                Data = JsonSerializer.SerializeToElement(new BridgeSelectionSetData { Success = ok, Error = ok ? null : "IDE 未连接或定位失败" }, BridgeJsonContext.Default.BridgeSelectionSetData)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeServer] setSelection 失败");
            return new BridgeServerMessage
            {
                Type = "selectionSet",
                Data = JsonSerializer.SerializeToElement(new BridgeSelectionSetData { Success = false, Error = ex.Message }, BridgeJsonContext.Default.BridgeSelectionSetData)
            };
        }
    }

    /// <summary>
    /// 构建 executeCommand 响应消息 — internal 便于单元测试
    /// 决策：复用 IShellExecutionService（含沙箱/拦截器），避免在 BridgeServer 重复实现
    /// </summary>
    internal async Task<BridgeServerMessage> BuildExecuteCommandResponseAsync(BridgeServerMessage message, CancellationToken cancellationToken)
    {
        var command = message.Data?.GetProperty("command").GetString();

        if (string.IsNullOrWhiteSpace(command))
        {
            return new BridgeServerMessage
            {
                Type = "commandExecuted",
                Data = JsonSerializer.SerializeToElement(new BridgeCommandExecutedData { Command = command, Success = false, Error = "命令为空" }, BridgeJsonContext.Default.BridgeCommandExecutedData)
            };
        }

        if (_shellService is null)
        {
            _logger?.LogDebug("[BridgeServer] executeCommand 请求: {Command} — Shell 服务未注入", command);
            return new BridgeServerMessage
            {
                Type = "commandExecuted",
                Data = JsonSerializer.SerializeToElement(new BridgeCommandExecutedData { Command = command, Success = false, Error = "Shell 服务未注入" }, BridgeJsonContext.Default.BridgeCommandExecutedData)
            };
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var result = await _shellService.ExecuteAsync(command, timeout: 30000, cancellationToken: cancellationToken).ConfigureAwait(false);
            sw.Stop();

            return new BridgeServerMessage
            {
                Type = "commandExecuted",
                Data = JsonSerializer.SerializeToElement(new BridgeCommandExecutedData
                {
                    Command = command,
                    Success = result.Success,
                    Output = result.Stdout,
                    Error = string.IsNullOrEmpty(result.Stderr) ? result.ErrorMessage : result.Stderr,
                    ExitCode = result.ExitCode,
                    DurationMs = sw.ElapsedMilliseconds
                }, BridgeJsonContext.Default.BridgeCommandExecutedData)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[BridgeServer] executeCommand 失败: {Command}", command);
            return new BridgeServerMessage
            {
                Type = "commandExecuted",
                Data = JsonSerializer.SerializeToElement(new BridgeCommandExecutedData { Command = command, Success = false, Error = ex.Message }, BridgeJsonContext.Default.BridgeCommandExecutedData)
            };
        }
    }

    /// <summary>
    /// 发送消息到客户端
    /// </summary>
    public async Task SendMessageAsync(string clientId, BridgeServerMessage message, CancellationToken cancellationToken)
    {
        if (!_clients.TryGetValue(clientId, out var webSocket))
        {
            return;
        }

        if (webSocket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(message, BridgeJsonContext.Default.BridgeServerMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        await webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 广播消息到所有客户端 - 通过 FlushGate 批量发送
    /// </summary>
    public Task BroadcastAsync(BridgeServerMessage message, CancellationToken cancellationToken)
    {
        // 如果 FlushGate 正在运行，将消息入队等待批量刷新
        if (_outgoingFlushGate != null && _gateActive == 1)
        {
            return _outgoingFlushGate.AddAsync(message, cancellationToken);
        }

        // FlushGate 不可用时直接发送
        return BroadcastDirectAsync(message, cancellationToken);
    }

    /// <summary>
    /// 直接广播消息到所有客户端（不经过 FlushGate）
    /// 用于 FlushGate 刷新回调内部，避免递归
    /// </summary>
    private Task BroadcastDirectAsync(BridgeServerMessage message, CancellationToken cancellationToken)
    {
        return Task.WhenAll(_clients.Keys
            .Select(clientId => SendMessageAsync(clientId, message, cancellationToken)));
    }

    /// <summary>
    /// 对等会话消息转发回调 - 将 P2P 消息广播到所有 WebSocket 客户端
    /// </summary>
    private void OnPeerMessageSent(object? sender, PeerMessageEventArgs e)
    {
        try
        {
            var serverMessage = new BridgeServerMessage
            {
                Type = "peer_message",
                Data = JsonDocument.Parse(e.Message.ToJson()).RootElement
            };
            _ = BroadcastAsync(serverMessage, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[BridgeServer] 转发对等消息失败");
        }
    }

    /// <summary>
    /// 批量消息刷新回调 - 将 FlushGate 批量收集的消息逐条直接发送到所有客户端
    /// 使用 BroadcastDirectAsync 避免递归回到 FlushGate
    /// </summary>
    private void OnOutgoingBatchFlushed(object? sender, BatchFlushedEventArgs<BridgeServerMessage> e)
    {
        // FlushGate 批量刷新时，逐条直接广播到所有客户端（不经过 FlushGate）
        foreach (var message in e.Items)
        {
            _ = BroadcastDirectAsync(message, CancellationToken.None);
        }
    }

    /// <summary>
    /// 写入 JSON 响应
    /// </summary>
    private async Task WriteJsonResponseAsync<T>(HttpListenerResponse response, T data, JsonTypeInfo<T> jsonTypeInfo)
    {
        var json = JsonSerializer.Serialize(data, jsonTypeInfo);
        var bytes = Encoding.UTF8.GetBytes(json);

        response.ContentType = HttpContentType.ApplicationJson.ToValue();
        response.ContentLength64 = bytes.Length;

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }

    public void Dispose()
    {
        _ = StopAsync(_cts.Token);
        _httpListener.Close();
        _cts.Dispose();
    }
}
