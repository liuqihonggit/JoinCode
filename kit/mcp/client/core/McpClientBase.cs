
namespace McpClient;

/// <summary>
/// MCP 客户端抽象基类 — 封装 MCP 协议握手、请求/通知收发、工具/资源/提示模板调用、Elicitation 处理等通用逻辑。
/// 派生类实现具体传输层的 ConnectAsync/DisconnectAsync/SendRequestAsync/SendNotificationAsync。
/// </summary>
public abstract class McpClientBase : IMcpClient {
    /// <summary>客户端配置选项。</summary>
    protected readonly McpClientOptions _options;
    /// <summary>日志记录器（可为 null）。</summary>
    protected readonly ILogger? _logger;

    private int _requestIdCounter;
    private Implementation? _serverInfo;
    private ServerCapabilities? _serverCapabilities;

    /// <summary>请求注册表 Actor，管理待响应请求的生命周期。</summary>
    protected readonly McpRequestRegistryActor _requestRegistry;

    /// <summary>
    /// Elicitation 请求处理器 — 对齐 TS client.setRequestHandler(ElicitRequestSchema, ...)
    /// 默认返回 cancel，连接成功后由上层替换为真实 handler
    /// </summary>
    private IElicitationHandler _elicitationHandler = new DefaultElicitationHandler();

    /// <summary>
    /// 服务器名称（用于 Elicitation handler 的 serverName 参数）
    /// </summary>
    protected string ServerName { get; set; } = string.Empty;

    /// <summary>客户端是否已连接到服务器。</summary>
    public bool IsConnected { get; protected set; }

    /// <summary>服务器实现信息 — 握手成功后填充。</summary>
    public Implementation? ServerInfo => _serverInfo;

    /// <summary>服务器能力声明 — 握手成功后填充,用于判断是否支持工具/资源/提示模板。</summary>
    public ServerCapabilities? ServerCapabilities => _serverCapabilities;

    /// <summary>通知接收事件 — 收到服务器推送的 JSON-RPC 通知时触发。</summary>
    public event EventHandler<McpNotificationReceivedEventArgs>? NotificationReceived;

    /// <summary>连接丢失事件 — 传输层断开或服务器进程退出时触发。</summary>
    public event EventHandler<McpConnectionLostEventArgs>? ConnectionLost;

    /// <summary>触发连接丢失事件,并将客户端标记为已断开。</summary>
    /// <param name="e">连接丢失事件参数。</param>
    protected void OnConnectionLost(McpConnectionLostEventArgs e) {
        IsConnected = false;
        ConnectionLost?.Invoke(this, e);
    }

    /// <summary>
    /// Elicitation 请求事件 — 对齐 TS AppState.elicitation.queue
    /// 当服务器发起 elicitation/create 请求时触发
    /// </summary>
    public event EventHandler<McpElicitationRequestEventArgs>? ElicitationRequestReceived;

    /// <summary>构造 McpClientBase 实例 — 初始化选项、日志与请求注册表 Actor。</summary>
    /// <param name="options">客户端选项,提供协议版本、超时等配置。</param>
    /// <param name="logger">日志记录器,为 null 时不记录日志。</param>
    protected McpClientBase(McpClientOptions options, ILogger? logger = null) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _requestRegistry = new McpRequestRegistryActor(logger);
    }

    /// <summary>
    /// 注册 Elicitation 处理器 — 对齐 TS registerElicitationHandler
    /// </summary>
    /// <param name="handler">Elicitation 处理器实例。</param>
    public void SetElicitationHandler(IElicitationHandler handler) {
        _elicitationHandler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>触发通知接收事件 — 由子类在消息循环中调用。</summary>
    /// <param name="e">通知接收事件参数。</param>
    protected void OnNotificationReceived(McpNotificationReceivedEventArgs e) {
        NotificationReceived?.Invoke(this, e);
    }

    /// <summary>触发 Elicitation 请求事件 — 由 HandleServerRequestAsync 调用。</summary>
    /// <param name="e">Elicitation 请求事件参数。</param>
    protected void OnElicitationRequestReceived(McpElicitationRequestEventArgs e) {
        ElicitationRequestReceived?.Invoke(this, e);
    }

    /// <summary>
    /// 处理服务器发来的请求（如 elicitation/create）— 由子类在消息循环中调用
    /// </summary>
    protected async Task HandleServerRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken) {
        var method = request.Method;

        if (method == McpMethodEnumConstants.ElicitationCreate) {
            await HandleElicitationRequestAsync(request, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 未识别的服务器请求，返回 MethodNotFound
        _logger?.LogWarning("收到未识别的服务器请求: {Method}", method);
    }

    private async Task HandleElicitationRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken) {
        _logger?.LogInformation("收到 Elicitation 请求: {ServerName}", ServerName);

        ElicitRequestParams? elicParams = null;
        if (request.Params.HasValue) {
            elicParams = request.Params.Value.Deserialize(McpJsonContext.Default.ElicitRequestParams);
        }

        if (elicParams == null) {
            _logger?.LogWarning("Elicitation 请求参数为空");
            return;
        }

        try {
            var result = await _elicitationHandler.HandleElicitationAsync(ServerName, request.Id, elicParams, cancellationToken).ConfigureAwait(false);

            // 触发事件通知上层
            OnElicitationRequestReceived(new McpElicitationRequestEventArgs {
                ServerName = ServerName,
                RequestId = request.Id,
                Params = elicParams,
                Result = result
            });
        } catch (OperationCanceledException) {
            _logger?.LogDebug("Elicitation 请求被取消");
        } catch (Exception ex) {
            _logger?.LogError(ex, "处理 Elicitation 请求失败");
        }
    }

    /// <summary>异步连接到 MCP 服务器 — 由派生类实现具体传输层连接逻辑。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步连接操作的任务。</returns>
    public abstract Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>异步断开与 MCP 服务器的连接 — 由派生类实现具体传输层断开逻辑。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步断开操作的任务。</returns>
    public abstract Task DisconnectAsync(CancellationToken cancellationToken = default);
    /// <summary>异步发送 JSON-RPC 请求 — 由派生类实现具体传输层发送逻辑。</summary>
    /// <param name="request">JSON-RPC 请求对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>服务器返回的 JSON-RPC 响应。</returns>
    protected abstract Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken);

    /// <summary>获取下一个递增的请求 ID — 线程安全。</summary>
    /// <returns>新的请求 ID。</returns>
    protected int GetNextRequestId() => Interlocked.Increment(ref _requestIdCounter);

    /// <summary>处理服务器响应 — 将响应派发到请求注册表完成对应 pending request。</summary>
    /// <param name="response">服务器返回的 JSON-RPC 响应。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected async Task ProcessResponseAsync(JsonRpcResponse response, CancellationToken cancellationToken = default) {
        if (response.Id == null) return;

        int requestId = response.GetIdAsInt();
        await _requestRegistry.CompleteAsync(requestId, response, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 安全的 fire-and-forget 响应处理 — 供传输层接收循环调用
    /// 防止客户端释放后到达的响应在 Actor 已释放时抛 ObjectDisposedException
    /// 成为未观察异常被静默丢弃（多级报错：捕获并记录，不崩溃、不污染接收循环）
    /// </summary>
    protected async Task FireAndForgetProcessResponseAsync(JsonRpcResponse response) {
        try {
            await ProcessResponseAsync(response, CancellationToken.None).ConfigureAwait(false);
        } catch (OperationCanceledException) { } catch (Exception ex) {
            _logger?.LogWarning(ex, "处理 MCP 服务器响应失败（客户端可能已释放）: {Id}", response.Id.ToString());
        }
    }

    /// <summary>取消所有 pending 请求 — 断开连接时调用。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected async Task CancelPendingRequestsAsync(CancellationToken cancellationToken = default) {
        await _requestRegistry.CancelAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>执行 MCP 握手 — 发送 initialize 请求并校验协议版本与服务器能力。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected async Task PerformHandshakeAsync(CancellationToken cancellationToken) {
        _logger?.LogInformation("开始 MCP 握手...");

        var initRequest = new InitializeRequestParams {
            ProtocolVersion = _options.ProtocolVersion,
            ClientInfo = new Implementation {
                Name = _options.ClientName,
                Version = _options.ClientVersion
            },
            Capabilities = new ClientCapabilities {
                // 对齐 TS: capabilities: { roots: {}, elicitation: {} }
                Roots = JsonDocument.Parse("{}").RootElement.Clone(),
                Elicitation = JsonDocument.Parse("{}").RootElement.Clone(),
            }
        };

        var request = new JsonRpcRequest {
            Id = GetNextRequestId(),
            Method = McpMethod.Initialize.ToValue(),
            Params = JsonSerializer.SerializeToElement(initRequest, McpJsonContext.Default.InitializeRequestParams)
        };

        var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Error != null) {
            throw new McpProtocolException($"[MCP017] 初始化失败: {response.Error.Message}");
        }

        var result = response.DeserializeResult(McpJsonContext.Default.InitializeResult);

        if (result == null) {
            throw new McpProtocolException("[MCP015] 无法解析初始化响应");
        }

        if (!McpProtocolVersion.Supported.Contains(result.ProtocolVersion)) {
            throw new McpProtocolException(
                $"[MCP018] 服务器返回不支持的协议版本: {result.ProtocolVersion}, 本端支持: {string.Join(", ", McpProtocolVersion.Supported)}");
        }

        _serverInfo = result.ServerInfo;
        _serverCapabilities = result.Capabilities;

        _logger?.LogInformation("MCP 握手成功，服务器: {ServerName} v{ServerVersion}",
            _serverInfo.Name, _serverInfo.Version);

        await SendInitializedNotificationAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SendInitializedNotificationAsync(CancellationToken cancellationToken) {
        var notification = new JsonRpcNotification {
            Method = McpMethod.Initialized.ToValue()
        };

        await SendNotificationAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>异步发送 JSON-RPC 通知 — 由派生类实现具体传输层发送逻辑。</summary>
    /// <param name="notification">JSON-RPC 通知对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    protected abstract Task SendNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken);

    private async Task<JsonRpcResponse> SendRequestWithRetryAsync(JsonRpcRequest request, CancellationToken cancellationToken) {
        // 降级为透传 — 网络重试统一由 ResilientHttpExecutor (Gateway) 处理，避免嵌套放大；保留单次请求 timeout
        using var cts = TimeoutHelper.CreateLinkedTimeout(cancellationToken, TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        return await SendRequestAsync(request, cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 列出服务器可用工具 — 调用 tools/list 方法。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>工具列表操作结果,失败时包含错误信息。</returns>
    public async Task<OperationResult<IReadOnlyList<ToolInfo>>> ListToolsAsync(CancellationToken cancellationToken = default) {
        EnsureConnected();

        var request = new JsonRpcRequest {
            Id = GetNextRequestId(),
            Method = McpMethod.ToolsList.ToValue()
        };

        try {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null) {
                return OperationResult<IReadOnlyList<ToolInfo>>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpClientJsonContext.Default.McpToolsListResponse);

            return OperationResult<IReadOnlyList<ToolInfo>>.Ok(result?.Tools ?? new List<ToolInfo>());
        } catch (Exception ex) {
            _logger?.LogError(ex, "列出工具失败");
            return OperationResult<IReadOnlyList<ToolInfo>>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 调用服务器工具 — 调用 tools/call 方法,支持进度回调。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="arguments">工具参数字典,可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="onProgress">进度回调,为 null 时不订阅进度通知。</param>
    /// <returns>工具调用结果,包含内容列表与错误标志。</returns>
    public async Task<ToolResult> CallToolAsync(
        string toolName,
        Dictionary<string, JsonElement>? arguments = null,
        CancellationToken cancellationToken = default,
        McpProgressCallback? onProgress = null) {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrEmpty(toolName);

        int? progressToken = onProgress is not null ? GetNextRequestId() : null;

        var requestParams = new Dictionary<string, JsonElement> {
            ["name"] = JsonSerializer.SerializeToElement(toolName, McpClientJsonContext.Default.String),
            ["arguments"] = JsonSerializer.SerializeToElement(
                arguments ?? new Dictionary<string, JsonElement>(),
                McpClientJsonContext.Default.DictionaryStringJsonElement)
        };

        if (progressToken.HasValue) {
            requestParams["_meta"] = JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement> {
                    ["progressToken"] = JsonSerializer.SerializeToElement(progressToken.Value, McpClientJsonContext.Default.Int32)
                },
                McpClientJsonContext.Default.DictionaryStringJsonElement);
        }

        var request = new JsonRpcRequest {
            Id = GetNextRequestId(),
            Method = McpMethod.ToolsCall.ToValue(),
            Params = JsonSerializer.SerializeToElement(requestParams, McpJsonContext.Default.DictionaryStringJsonElement)
        };

        EventHandler<McpNotificationReceivedEventArgs>? progressHandler = null;
        try {
            if (progressToken.HasValue && onProgress is not null) {
                var token = progressToken.Value;
                progressHandler = (_, args) => {
                    if (args.Method == McpMethod.NotificationProgress.ToValue() && args.Params.HasValue) {
                        try {
                            var progressParams = args.Params.Value;
                            double? progress = null;
                            double? total = null;
                            string? message = null;

                            if (progressParams.TryGetProperty("progressToken", out var tokenEl) && tokenEl.ValueKind == JsonValueKind.Number && tokenEl.GetInt32() != token) {
                                return;
                            }

                            if (progressParams.TryGetProperty("progress", out var progressEl) && progressEl.ValueKind == JsonValueKind.Number) {
                                progress = progressEl.GetDouble();
                            }

                            if (progressParams.TryGetProperty("total", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number) {
                                total = totalEl.GetDouble();
                            }

                            if (progressParams.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String) {
                                message = msgEl.GetString();
                            }

                            onProgress(new McpToolProgress {
                                Type = "mcp_progress",
                                Status = McpProgressStatusEnumConstants.Progress,
                                Progress = progress,
                                Total = total,
                                ProgressMessage = message
                            });
                        } catch (Exception ex) {
                            _logger?.LogWarning(ex, "解析进度通知失败");
                        }
                    }
                };

                NotificationReceived += progressHandler;
            }

            try {
                var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.Error != null) {
                    return new ToolResult {
                        IsError = true,
                        Content = new List<ToolContent>
                        {
                            new() { Type = ToolContentType.Text, Text = response.Error.Message }
                        }
                    };
                }

                var result = response.DeserializeResult(McpClientJsonContext.Default.ToolResult);

                return result ?? new ToolResult {
                    Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Empty response" } }
                };
            } catch (Exception ex) {
                _logger?.LogError(ex, "调用工具 {ToolName} 失败", toolName);
                return new ToolResult {
                    IsError = true,
                    Content = new List<ToolContent>
                    {
                        new() { Type = ToolContentType.Text, Text = $"Error: {ex.Message}" }
                    }
                };
            }
        } finally {
            if (progressHandler is not null) {
                NotificationReceived -= progressHandler;
            }
        }
    }

    /// <summary>
    /// 列出服务器可用资源 — 调用 resources/list 方法,服务器不支持时返回失败。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>资源列表操作结果,失败时包含错误信息。</returns>
    public async Task<OperationResult<IReadOnlyList<McpResource>>> ListResourcesAsync(CancellationToken cancellationToken = default) {
        EnsureConnected();

        if (ServerCapabilities?.Resources == null) {
            return OperationResult<IReadOnlyList<McpResource>>.Fail("服务器不支持资源功能");
        }

        var request = new JsonRpcRequest {
            Id = GetNextRequestId(),
            Method = McpMethod.ResourcesList.ToValue()
        };

        try {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null) {
                return OperationResult<IReadOnlyList<McpResource>>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpJsonContext.Default.McpResourcesListResponse);

            return OperationResult<IReadOnlyList<McpResource>>.Ok(result?.Resources ?? new List<McpResource>());
        } catch (Exception ex) {
            _logger?.LogError(ex, "列出资源失败");
            return OperationResult<IReadOnlyList<McpResource>>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 读取指定 URI 的资源内容 — 调用 resources/read 方法,返回首个内容项。
    /// </summary>
    /// <param name="uri">资源 URI。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>资源内容操作结果,失败时包含错误信息。</returns>
    public async Task<OperationResult<McpResourceContent?>> ReadResourceAsync(
        string uri,
        CancellationToken cancellationToken = default) {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrEmpty(uri);

        if (ServerCapabilities?.Resources == null) {
            return OperationResult<McpResourceContent?>.Fail("服务器不支持资源功能");
        }

        var request = new JsonRpcRequest {
            Id = GetNextRequestId(),
            Method = McpMethod.ResourcesRead.ToValue(),
            Params = JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement> { ["uri"] = JsonSerializer.SerializeToElement(uri, McpClientJsonContext.Default.String) },
                McpJsonContext.Default.DictionaryStringJsonElement)
        };

        try {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null) {
                return OperationResult<McpResourceContent?>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpJsonContext.Default.McpResourceReadResponse);

            var content = result?.Contents.FirstOrDefault();
            return OperationResult<McpResourceContent?>.Ok(content);
        } catch (Exception ex) {
            _logger?.LogError(ex, "读取资源 {Uri} 失败", uri);
            return OperationResult<McpResourceContent?>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 列出服务器可用提示模板 — 调用 prompts/list 方法,服务器不支持时返回失败。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提示模板列表操作结果,失败时包含错误信息。</returns>
    public async Task<OperationResult<IReadOnlyList<McpPrompt>>> ListPromptsAsync(CancellationToken cancellationToken = default) {
        EnsureConnected();

        if (ServerCapabilities?.Prompts == null) {
            return OperationResult<IReadOnlyList<McpPrompt>>.Fail("服务器不支持提示模板功能");
        }

        var request = new JsonRpcRequest {
            Id = GetNextRequestId(),
            Method = McpMethod.PromptsList.ToValue()
        };

        try {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null) {
                return OperationResult<IReadOnlyList<McpPrompt>>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpJsonContext.Default.McpPromptsListResponse);

            return OperationResult<IReadOnlyList<McpPrompt>>.Ok(result?.Prompts ?? new List<McpPrompt>());
        } catch (Exception ex) {
            _logger?.LogError(ex, "列出提示模板失败");
            return OperationResult<IReadOnlyList<McpPrompt>>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// 获取指定提示模板内容 — 调用 prompts/get 方法,支持传入参数。
    /// </summary>
    /// <param name="name">提示模板名称。</param>
    /// <param name="arguments">模板参数字典,可为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提示模板消息操作结果,失败时包含错误信息。</returns>
    public async Task<OperationResult<McpPromptMessage?>> GetPromptAsync(
        string name,
        Dictionary<string, JsonElement>? arguments = null,
        CancellationToken cancellationToken = default) {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (ServerCapabilities?.Prompts == null) {
            return OperationResult<McpPromptMessage?>.Fail("服务器不支持提示模板功能");
        }

        var request = new JsonRpcRequest {
            Id = GetNextRequestId(),
            Method = McpMethod.PromptsGet.ToValue(),
            Params = JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement> {
                    ["name"] = JsonSerializer.SerializeToElement(name, McpClientJsonContext.Default.String),
                    ["arguments"] = JsonSerializer.SerializeToElement(
                        arguments ?? new Dictionary<string, JsonElement>(),
                        McpClientJsonContext.Default.DictionaryStringJsonElement)
                },
                McpJsonContext.Default.DictionaryStringJsonElement)
        };

        try {
            var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Error != null) {
                return OperationResult<McpPromptMessage?>.Fail(response.Error.Message);
            }

            var result = response.DeserializeResult(McpJsonContext.Default.McpPromptGetResponse);

            var message = result == null ? null : new McpPromptMessage {
                Description = result.Description,
                Messages = result.Messages
            };

            return OperationResult<McpPromptMessage?>.Ok(message);
        } catch (Exception ex) {
            _logger?.LogError(ex, "获取提示模板 {Name} 失败", name);
            return OperationResult<McpPromptMessage?>.Fail(ex.Message);
        }
    }

    /// <summary>确保客户端已连接 — 未连接时抛出 InvalidOperationException。</summary>
    protected void EnsureConnected() {
        if (!IsConnected) {
            throw new InvalidOperationException(McpErrorMessages.McpClientNotConnected);
        }
    }

    /// <summary>
    /// 向服务器发送 Ping 请求 — 用于心跳检测与连接保活。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务,Ping 失败时抛出 McpProtocolException。</returns>
    public async Task PingAsync(CancellationToken cancellationToken = default) {
        EnsureConnected();

        var request = new JsonRpcRequest {
            Id = GetNextRequestId(),
            Method = McpMethod.Ping.ToValue()
        };

        var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Error != null) {
            throw new McpProtocolException($"[MCP019] Ping 失败: {response.Error.Message}");
        }
    }

    /// <summary>
    /// 设置服务器日志级别 — 调用 logging/setLevel 方法。
    /// </summary>
    /// <param name="level">日志级别字符串,如 "debug"/"info"/"warning"/"error"。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示异步操作的任务,设置失败时抛出 McpProtocolException。</returns>
    public async Task SetLogLevelAsync(string level, CancellationToken cancellationToken = default) {
        EnsureConnected();
        ArgumentException.ThrowIfNullOrEmpty(level);

        var request = new JsonRpcRequest {
            Id = GetNextRequestId(),
            Method = McpMethod.LoggingSetLevel.ToValue(),
            Params = JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement> {
                    ["level"] = JsonSerializer.SerializeToElement(level, McpClientJsonContext.Default.String)
                },
                McpJsonContext.Default.DictionaryStringJsonElement)
        };

        var response = await SendRequestWithRetryAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.Error != null) {
            throw new McpProtocolException($"[MCP020] 设置日志级别失败: {response.Error.Message}");
        }
    }

    /// <summary>异步释放客户端资源 — 由派生类实现具体释放逻辑。</summary>
    /// <returns>表示异步释放操作的任务。</returns>
    public abstract ValueTask DisposeAsync();
}