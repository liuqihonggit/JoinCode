
namespace McpClient;

public abstract class McpClientBase : IMcpClient
{
    protected readonly McpClientOptions _options;
    protected readonly ILogger? _logger;

    private int _requestIdCounter;
    private Implementation? _serverInfo;
    private ServerCapabilities? _serverCapabilities;

    protected readonly SemaphoreSlim _requestLock = new(1, 1);
    protected readonly Dictionary<int, TaskCompletionSource<JsonRpcResponse>> _pendingRequests = new();

    /// <summary>
    /// Elicitation 请求处理器 — 对齐 TS client.setRequestHandler(ElicitRequestSchema, ...)
    /// 默认返回 cancel，连接成功后由上层替换为真实 handler
    /// </summary>
    private IElicitationHandler _elicitationHandler = new DefaultElicitationHandler();

    /// <summary>
    /// 服务器名称（用于 Elicitation handler 的 serverName 参数）
    /// </summary>
    protected string ServerName { get; set; } = string.Empty;

    public bool IsConnected { get; protected set; }
    public Implementation? ServerInfo => _serverInfo;
    public ServerCapabilities? ServerCapabilities => _serverCapabilities;

    public event EventHandler<McpNotificationReceivedEventArgs>? NotificationReceived;

    public event EventHandler<McpConnectionLostEventArgs>? ConnectionLost;

    protected void OnConnectionLost(McpConnectionLostEventArgs e)
    {
        IsConnected = false;
        ConnectionLost?.Invoke(this, e);
    }

    /// <summary>
    /// Elicitation 请求事件 — 对齐 TS AppState.elicitation.queue
    /// 当服务器发起 elicitation/create 请求时触发
    /// </summary>
    public event EventHandler<McpElicitationRequestEventArgs>? ElicitationRequestReceived;

    protected McpClientBase(McpClientOptions options, ILogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    /// <summary>
    /// 注册 Elicitation 处理器 — 对齐 TS registerElicitationHandler
    /// </summary>
    public void SetElicitationHandler(IElicitationHandler handler)
    {
        _elicitationHandler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    protected void OnNotificationReceived(McpNotificationReceivedEventArgs e)
    {
        NotificationReceived?.Invoke(this, e);
    }

    protected void OnElicitationRequestReceived(McpElicitationRequestEventArgs e)
    {
        ElicitationRequestReceived?.Invoke(this, e);
    }

    /// <summary>
    /// 处理服务器发来的请求（如 elicitation/create）— 由子类在消息循环中调用
    /// </summary>
    protected async Task HandleServerRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var method = request.Method;

        if (method == McpMethodConstants.ElicitationCreate)
        {
            await HandleElicitationRequestAsync(request, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 未识别的服务器请求，返回 MethodNotFound
        _logger?.LogWarning("收到未识别的服务器请求: {Method}", method);
    }

    private async Task HandleElicitationRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("收到 Elicitation 请求: {ServerName}", ServerName);

        ElicitRequestParams? elicParams = null;
        if (request.Params.HasValue)
        {
            elicParams = request.Params.Value.Deserialize(McpJsonContext.Default.ElicitRequestParams);
        }

        if (elicParams == null)
        {
            _logger?.LogWarning("Elicitation 请求参数为空");
            return;
        }

        try
        {
            var result = await _elicitationHandler.HandleElicitationAsync(ServerName, request.Id, elicParams, cancellationToken).ConfigureAwait(false);

            // 触发事件通知上层
            OnElicitationRequestReceived(new McpElicitationRequestEventArgs
            {
                ServerName = ServerName,
                RequestId = request.Id,
                Params = elicParams,
                Result = result
            });
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Elicitation 请求被取消");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理 Elicitation 请求失败");
        }
    }

    public abstract Task ConnectAsync(CancellationToken cancellationToken = default);
    public abstract Task DisconnectAsync(CancellationToken cancellationToken = default);
    protected abstract Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken);

    protected int GetNextRequestId() => Interlocked.Increment(ref _requestIdCounter);

    protected async Task ProcessResponseAsync(JsonRpcResponse response, CancellationToken cancellationToken = default)
    {
        if (response.Id == null) return;

        int requestId = response.GetIdAsInt();

        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_pendingRequests.TryGetValue(requestId, out var tcs))
            {
                tcs.TrySetResult(response);
                _pendingRequests.Remove(requestId);
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    protected async Task CancelPendingRequestsAsync(CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            _pendingRequests.Clear();
        }
        finally
        {
            _requestLock.Release();
        }
    }

    protected async Task PerformHandshakeAsync(CancellationToken cancellationToken)
    {
        _logger?.LogInformation("开始 MCP 握手...");

        var initRequest = new InitializeRequestParams
        {
            ProtocolVersion = _options.ProtocolVersion,
            ClientInfo = new Implementation
            {
                Name = _options.ClientName,
                Version = _options.ClientVersion
            },
            Capabilities = new ClientCapabilities
            {
                // 对齐 TS: capabilities: { roots: {}, elicitation: {} }
                Roots = JsonDocument.Parse("{}").RootElement.Clone(),
                Elicitation = JsonDocument.Parse("{}").RootElement.Clone(),
            }
        };

        var request = new JsonRpcRequest
        {
            Id = GetNextRequestId(),
            Method = McpMethod.Initialize.ToValue(),
            Params = JsonSerializer.SerializeToElement(initRequest, McpJsonContext.Default.InitializeRequestParams)
        };

        var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new McpProtocolException($"初始化失败: {response.Error.Message}");
        }

        var result = response.DeserializeResult(McpJsonContext.Default.InitializeResult);

        if (result == null)
        {
            throw new McpProtocolException("无法解析初始化响应");
        }

        _serverInfo = result.ServerInfo;
        _serverCapabilities = result.Capabilities;

        _logger?.LogInformation("MCP 握手成功，服务器: {ServerName} v{ServerVersion}",
            _serverInfo.Name, _serverInfo.Version);

        await SendInitializedNotificationAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendInitializedNotificationAsync(CancellationToken cancellationToken)
    {
        var notification = new JsonRpcNotification
        {
            Method = McpMethod.Initialized.ToValue()
        };

        await SendNotificationAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    protected abstract Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken);

    private async Task<JsonRpcResponse> SendRequestWithRetryAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        int attempt = 0;
        Exception? lastException = null;

        while (attempt < _options.MaxRetries)
        {
            try
            {
                using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

                return await SendRequestAsync(request, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;
                _logger?.LogWarning(ex, "请求失败，尝试 {Attempt}/{MaxRetries}", attempt, _options.MaxRetries);

                if (attempt < _options.MaxRetries)
                {
                    await Task.Delay(_options.RetryDelayMs * attempt, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new McpProtocolException($"请求在 {_options.MaxRetries} 次尝试后失败", lastException ?? throw new InvalidOperationException("No exception after retries."));
    }

    public async Task<OperationResult<IReadOnlyList<ToolInfo>>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var request = new JsonRpcRequest
        {
            Id = GetNextRequestId(),
            Method = McpMethod.ToolsList.ToValue()
        };

        try
        {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null)
            {
                return OperationResult<IReadOnlyList<ToolInfo>>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpClientJsonContext.Default.McpToolsListResponse);

            return OperationResult<IReadOnlyList<ToolInfo>>.Ok(result?.Tools ?? new List<ToolInfo>());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "列出工具失败");
            return OperationResult<IReadOnlyList<ToolInfo>>.Fail(ex.Message);
        }
    }

    public async Task<ToolResult> CallToolAsync(
        string toolName,
        Dictionary<string, JsonElement>? arguments = null,
        CancellationToken cancellationToken = default,
        McpProgressCallback? onProgress = null)
    {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        int? progressToken = onProgress is not null ? GetNextRequestId() : null;

        var requestParams = new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement(toolName, McpClientJsonContext.Default.String),
            ["arguments"] = JsonSerializer.SerializeToElement(
                arguments ?? new Dictionary<string, JsonElement>(),
                McpClientJsonContext.Default.DictionaryStringJsonElement)
        };

        if (progressToken.HasValue)
        {
            requestParams["_meta"] = JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement>
                {
                    ["progressToken"] = JsonSerializer.SerializeToElement(progressToken.Value, McpClientJsonContext.Default.Int32)
                },
                McpClientJsonContext.Default.DictionaryStringJsonElement);
        }

        var request = new JsonRpcRequest
        {
            Id = GetNextRequestId(),
            Method = McpMethod.ToolsCall.ToValue(),
            Params = JsonSerializer.SerializeToElement(requestParams, McpJsonContext.Default.DictionaryStringJsonElement)
        };

        EventHandler<McpNotificationReceivedEventArgs>? progressHandler = null;
        try
        {
            if (progressToken.HasValue && onProgress is not null)
            {
                var token = progressToken.Value;
                progressHandler = (_, args) =>
                {
                    if (args.Method == McpMethod.NotificationProgress.ToValue() && args.Params.HasValue)
                    {
                        try
                        {
                            var progressParams = args.Params.Value;
                            double? progress = null;
                            double? total = null;
                            string? message = null;

                            if (progressParams.TryGetProperty("progressToken", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.Number && tokenEl.GetInt32() != token)
                            {
                                return;
                            }

                            if (progressParams.TryGetProperty("progress", out var progressEl) && progressEl.ValueKind == JsonValueKind.Number)
                            {
                                progress = progressEl.GetDouble();
                            }

                            if (progressParams.TryGetProperty("total", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number)
                            {
                                total = totalEl.GetDouble();
                            }

                            if (progressParams.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
                            {
                                message = msgEl.GetString();
                            }

                            onProgress(new McpToolProgress
                            {
                                Type = "mcp_progress",
                                Status = McpProgressStatusConstants.Progress,
                                Progress = progress,
                                Total = total,
                                ProgressMessage = message
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "解析进度通知失败");
                        }
                    }
                };

                NotificationReceived += progressHandler;
            }

            try
            {
                var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.Error != null)
                {
                    return new ToolResult
                    {
                        IsError = true,
                        Content = new List<ToolContent>
                        {
                            new() { Type = ToolContentType.Text, Text = response.Error.Message }
                        }
                    };
                }

                var result = response.DeserializeResult(McpClientJsonContext.Default.ToolResult);

                return result ?? new ToolResult
                {
                    Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Empty response" } }
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "调用工具 {ToolName} 失败", toolName);
                return new ToolResult
                {
                    IsError = true,
                    Content = new List<ToolContent>
                    {
                        new() { Type = ToolContentType.Text, Text = $"Error: {ex.Message}" }
                    }
                };
            }
        }
        finally
        {
            if (progressHandler is not null)
            {
                NotificationReceived -= progressHandler;
            }
        }
    }

    public async Task<OperationResult<IReadOnlyList<McpResource>>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (ServerCapabilities?.Resources == null)
        {
            return OperationResult<IReadOnlyList<McpResource>>.Fail("服务器不支持资源功能");
        }

        var request = new JsonRpcRequest
        {
            Id = GetNextRequestId(),
            Method = McpMethod.ResourcesList.ToValue()
        };

        try
        {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null)
            {
                return OperationResult<IReadOnlyList<McpResource>>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpJsonContext.Default.McpResourcesListResponse);

            return OperationResult<IReadOnlyList<McpResource>>.Ok(result?.Resources ?? new List<McpResource>());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "列出资源失败");
            return OperationResult<IReadOnlyList<McpResource>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<McpResourceContent?>> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrEmpty(uri);

        if (ServerCapabilities?.Resources == null)
        {
            return OperationResult<McpResourceContent?>.Fail("服务器不支持资源功能");
        }

        var request = new JsonRpcRequest
        {
            Id = GetNextRequestId(),
            Method = McpMethod.ResourcesRead.ToValue(),
            Params = JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement> { ["uri"] = JsonSerializer.SerializeToElement(uri, McpClientJsonContext.Default.String) },
                McpJsonContext.Default.DictionaryStringJsonElement)
        };

        try
        {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null)
            {
                return OperationResult<McpResourceContent?>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpJsonContext.Default.McpResourceReadResponse);

            var content = result?.Contents.FirstOrDefault();
            return OperationResult<McpResourceContent?>.Ok(content);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "读取资源 {Uri} 失败", uri);
            return OperationResult<McpResourceContent?>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<IReadOnlyList<McpPrompt>>> ListPromptsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (ServerCapabilities?.Prompts == null)
        {
            return OperationResult<IReadOnlyList<McpPrompt>>.Fail("服务器不支持提示模板功能");
        }

        var request = new JsonRpcRequest
        {
            Id = GetNextRequestId(),
            Method = McpMethod.PromptsList.ToValue()
        };

        try
        {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null)
            {
                return OperationResult<IReadOnlyList<McpPrompt>>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpJsonContext.Default.McpPromptsListResponse);

            return OperationResult<IReadOnlyList<McpPrompt>>.Ok(result?.Prompts ?? new List<McpPrompt>());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "列出提示模板失败");
            return OperationResult<IReadOnlyList<McpPrompt>>.Fail(ex.Message);
        }
    }

    public async Task<OperationResult<McpPromptMessage?>> GetPromptAsync(
        string name,
        Dictionary<string, JsonElement>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (ServerCapabilities?.Prompts == null)
        {
            return OperationResult<McpPromptMessage?>.Fail("服务器不支持提示模板功能");
        }

        var request = new JsonRpcRequest
        {
            Id = GetNextRequestId(),
            Method = McpMethod.PromptsGet.ToValue(),
            Params = JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonSerializer.SerializeToElement(name, McpClientJsonContext.Default.String),
                    ["arguments"] = JsonSerializer.SerializeToElement(
                        arguments ?? new Dictionary<string, JsonElement>(),
                        McpClientJsonContext.Default.DictionaryStringJsonElement)
                },
                McpJsonContext.Default.DictionaryStringJsonElement)
        };

        try
        {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null)
            {
                return OperationResult<McpPromptMessage?>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpJsonContext.Default.McpPromptGetResponse);

            var message = result == null ? null : new McpPromptMessage
            {
                Description = result.Description,
                Messages = result.Messages
            };

            return OperationResult<McpPromptMessage?>.Ok(message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取提示模板 {Name} 失败", name);
            return OperationResult<McpPromptMessage?>.Fail(ex.Message);
        }
    }

    protected void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("MCP 客户端未连接");
        }
    }

    public async Task PingAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var request = new JsonRpcRequest
        {
            Id = GetNextRequestId(),
            Method = McpMethod.Ping.ToValue()
        };

        var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new McpProtocolException($"Ping 失败: {response.Error.Message}");
        }
    }

    public async Task SetLogLevelAsync(string level, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrEmpty(level);

        var request = new JsonRpcRequest
        {
            Id = GetNextRequestId(),
            Method = McpMethod.LoggingSetLevel.ToValue(),
            Params = JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement>
                {
                    ["level"] = JsonSerializer.SerializeToElement(level, McpClientJsonContext.Default.String)
                },
                McpJsonContext.Default.DictionaryStringJsonElement)
        };

        var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Error != null)
        {
            throw new McpProtocolException($"设置日志级别失败: {response.Error.Message}");
        }
    }

    public abstract ValueTask DisposeAsync();
}
