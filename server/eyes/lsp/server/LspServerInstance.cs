namespace Services.Lsp.Internal;

/// <summary>
/// LSP 服务器状态 — 与 Clock 组件的 ServiceStatus 结构相似，
/// 唯一差异：Error（可恢复错误）vs Failed（彻底失败）
/// </summary>
public enum LspServerState {
    /// <summary>已停止 — 初始状态</summary>
    [EnumValue("stopped")] Stopped,
    /// <summary>启动中 — 正在连接 LSP 进程</summary>
    [EnumValue("starting")] Starting,
    /// <summary>运行中 — 已连接且可收发请求</summary>
    [EnumValue("running")] Running,
    /// <summary>停止中 — 正在断开连接</summary>
    [EnumValue("stopping")] Stopping,
    /// <summary>可恢复错误 — 非彻底失败，可重试启动</summary>
    [EnumValue("error")] Error
}

/// <summary>
/// LSP 服务器事件 — 触发状态转换的事件（ADR 0040 事件枚举）
/// </summary>
internal enum LspServerEvent {
    /// <summary>启动请求 — Stopped/Error → Starting</summary>
    Start,
    /// <summary>连接成功 — Starting → Running</summary>
    ConnectSucceeded,
    /// <summary>连接失败 — Starting → Error</summary>
    ConnectFailed,
    /// <summary>开始停止 — Running/Starting/Error → Stopping</summary>
    BeginStop,
    /// <summary>停止成功 — Stopping → Stopped</summary>
    StopSucceeded,
    /// <summary>停止失败 — Stopping → Error</summary>
    StopFailed,
}

/// <summary>
/// LSP 服务器实例接口 — 管理单个 LSP 进程的生命周期与通信
/// </summary>
public interface ILspServerInstance : IAsyncDisposable {
    /// <summary>服务器名称</summary>
    string Name { get; }
    /// <summary>当前状态</summary>
    LspServerState State { get; }
    /// <summary>实例配置</summary>
    LspInstanceConfig Config { get; }
    /// <summary>上次错误 — null 表示无错误</summary>
    Exception? LastError { get; }
    /// <summary>是否健康 — 运行中且已连接</summary>
    bool IsHealthy { get; }

    /// <summary>错误发生事件</summary>
    event EventHandler<LspServerErrorEventArgs>? ErrorOccurred;
    /// <summary>状态变更事件</summary>
    event EventHandler<LspServerStateChangedEventArgs>? StateChanged;

    /// <summary>启动 LSP 服务器</summary>
    /// <param name="workingDirectory">工作目录 — null 使用配置默认值</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartAsync(string? workingDirectory = null, CancellationToken cancellationToken = default);
    /// <summary>停止 LSP 服务器</summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StopAsync(CancellationToken cancellationToken = default);
    /// <summary>重启 LSP 服务器</summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task RestartAsync(CancellationToken cancellationToken = default);
    /// <summary>发送 JSON-RPC 请求并等待响应</summary>
    /// <param name="method">请求方法名</param>
    /// <param name="params">请求参数 — null 表示无参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应 JSON 节点 — null 表示无返回值</returns>
    Task<JsonNode?> SendRequestAsync(string method, object? @params, CancellationToken cancellationToken = default);
    /// <summary>发送 JSON-RPC 通知（无响应）</summary>
    /// <param name="method">通知方法名</param>
    /// <param name="params">通知参数 — null 表示无参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SendNotificationAsync(string method, object? @params, CancellationToken cancellationToken = default);
    /// <summary>注册通知处理器</summary>
    /// <param name="method">通知方法名</param>
    /// <param name="handler">通知处理委托</param>
    void OnNotification(string method, Func<JsonNode?, CancellationToken, ValueTask> handler);
    /// <summary>注册请求处理器</summary>
    /// <param name="method">请求方法名</param>
    /// <param name="handler">请求处理委托 — 返回响应 JSON 节点</param>
    void OnRequest(string method, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>> handler);
}

/// <summary>
/// LSP 服务器错误事件参数
/// </summary>
public sealed partial class LspServerErrorEventArgs : EventArgs {
    /// <summary>错误异常</summary>
    public required Exception Error { get; init; }
    /// <summary>服务器名称</summary>
    public required string ServerName { get; init; }
}

/// <summary>
/// LSP 服务器状态变更事件参数
/// </summary>
public sealed partial class LspServerStateChangedEventArgs : EventArgs {
    /// <summary>旧状态</summary>
    public required LspServerState OldState { get; init; }
    /// <summary>新状态</summary>
    public required LspServerState NewState { get; init; }
}

/// <summary>
/// LSP 实例配置 — 定义 LSP 进程的启动参数与连接选项
/// </summary>
public sealed partial class LspInstanceConfig {
    /// <summary>服务器名称 — 唯一标识</summary>
    public required string Name { get; init; }
    /// <summary>语言标识 — 如 "csharp"、"python"</summary>
    public required string LanguageId { get; init; }
    /// <summary>启动命令 — LSP 进程的可执行路径</summary>
    public required string Command { get; init; }
    /// <summary>命令行参数</summary>
    public List<string> Arguments { get; init; } = [];
    /// <summary>环境变量</summary>
    public Dictionary<string, string> Environment { get; init; } = [];
    /// <summary>工作目录 — null 使用进程当前目录</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>启动超时 — null 使用默认值</summary>
    public TimeSpan? StartupTimeout { get; init; }
    /// <summary>最大重启次数 — null 使用默认值 3</summary>
    public int? MaxRestarts { get; init; }
    /// <summary>文件扩展名到语言标识的映射</summary>
    public Dictionary<string, string> ExtensionToLanguage { get; init; } = [];
}

/// <summary>
/// LSP 服务器实例 — 管理单个 LSP 进程的生命周期、状态机转换与 JSON-RPC 通信
/// </summary>
[FsmStateMachine(typeof(LspServerState), typeof(LspServerEvent), LspServerState.Stopped)]
[Transition(LspServerState.Stopped, LspServerEvent.Start, LspServerState.Starting)]
[Transition(LspServerState.Error, LspServerEvent.Start, LspServerState.Starting)]
[Transition(LspServerState.Starting, LspServerEvent.ConnectSucceeded, LspServerState.Running)]
[Transition(LspServerState.Starting, LspServerEvent.ConnectFailed, LspServerState.Error)]
[Transition(LspServerState.Starting, LspServerEvent.BeginStop, LspServerState.Stopping)]
[Transition(LspServerState.Running, LspServerEvent.BeginStop, LspServerState.Stopping)]
[Transition(LspServerState.Error, LspServerEvent.BeginStop, LspServerState.Stopping)]
[Transition(LspServerState.Stopping, LspServerEvent.StopSucceeded, LspServerState.Stopped)]
[Transition(LspServerState.Stopping, LspServerEvent.StopFailed, LspServerState.Error)]
public sealed partial class LspServerInstance : ILspServerInstance {
    private const int LspErrorContentModified = -32801;
    private const int MaxRetriesForTransientErrors = 3;
    private const int RetryBaseDelayMs = 500;
    private const int DefaultMaxRestarts = 3;

    private readonly LspInstanceConfig _config;
    private readonly ILogger _logger;
    private readonly LspClient _client;
    private readonly Fsm<LspServerState, LspServerEvent> _stateMachine;

    private Exception? _lastError;
    private int _crashRecoveryCount;
    private int _restartCount;
    private int _isDisposed;

    /// <summary>服务器名称</summary>
    public string Name => _config.Name;
    /// <summary>实例配置</summary>
    public LspInstanceConfig Config => _config;

    /// <summary>当前状态</summary>
    public LspServerState State => _stateMachine.CurrentState;

    /// <summary>上次错误 — null 表示无错误</summary>
    public Exception? LastError => Volatile.Read(ref _lastError);
    /// <summary>是否健康 — 运行中且已连接</summary>
    public bool IsHealthy => State == LspServerState.Running && _client.IsConnected;

    /// <summary>错误发生事件</summary>
    public event EventHandler<LspServerErrorEventArgs>? ErrorOccurred;
    /// <summary>状态变更事件</summary>
    public event EventHandler<LspServerStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 构造 LSP 服务器实例
    /// </summary>
    /// <param name="config">实例配置</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="processService">进程服务抽象</param>
    /// <param name="logger">日志器</param>
    public LspServerInstance(LspInstanceConfig config, IFileSystem fs, IProcessService processService, ILogger logger) {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = new LspClient(fs, processService, logger);
        _stateMachine = new Fsm<LspServerState, LspServerEvent>(_fsmSortedKeys, _fsmRules, LspServerState.Stopped);
        _stateMachine.StateChanged += OnStateChanged;
        _stateMachine.StateChanged += (_, e) => FsmDispatchEvent(e);
    }

    private void OnStateChanged(object? sender, TransitionResult<LspServerState, LspServerEvent> e) {
        _logger.LogInformation("LSP server '{Name}' state: {OldState} → {NewState}", Name, e.FromState, e.ToState);
        StateChanged?.Invoke(this, new LspServerStateChangedEventArgs { OldState = e.FromState, NewState = e.ToState });
    }

    /// <summary>
    /// 启动 LSP 服务器
    /// </summary>
    /// <param name="workingDirectory">工作目录 — null 使用配置默认值</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StartAsync(string? workingDirectory = null, CancellationToken cancellationToken = default) {
        if (State is LspServerState.Running or LspServerState.Starting) {
            _logger.LogDebug("LSP server '{Name}' is already {State}", Name, State);
            return;
        }

        var maxRestarts = _config.MaxRestarts ?? DefaultMaxRestarts;
        if (State == LspServerState.Error && _crashRecoveryCount > maxRestarts) {
            var error = new InvalidOperationException($"LSP server '{Name}' exceeded max crash recovery attempts ({maxRestarts})");
            _lastError = error;
            ErrorOccurred?.Invoke(this, new LspServerErrorEventArgs { Error = error, ServerName = Name });
            throw error;
        }

        try {
            _stateMachine.Trigger(LspServerEvent.Start);

            var effectiveWorkDir = workingDirectory ?? _config.WorkingDirectory;
            var connected = await _client.ConnectAsync(new LspServerConfig {
                LanguageId = _config.LanguageId,
                Command = _config.Command,
                Arguments = _config.Arguments,
                WorkingDirectory = effectiveWorkDir,
            }, cancellationToken).ConfigureAwait(false);

            if (!connected) {
                throw new InvalidOperationException($"Failed to connect to LSP server '{Name}'");
            }

            _stateMachine.Trigger(LspServerEvent.ConnectSucceeded);
            _crashRecoveryCount = 0;
            _logger.LogInformation("LSP server '{Name}' started successfully", Name);
        } catch (Exception ex) {
            _lastError = ex;
            _stateMachine.ForceSet(LspServerState.Error);
            ErrorOccurred?.Invoke(this, new LspServerErrorEventArgs { Error = ex, ServerName = Name });
            _logger.LogError(ex, "Failed to start LSP server '{Name}'", Name);
            throw;
        }
    }

    /// <summary>
    /// 停止 LSP 服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task StopAsync(CancellationToken cancellationToken = default) {
        if (State is LspServerState.Stopped or LspServerState.Stopping) {
            return;
        }

        try {
            _stateMachine.Trigger(LspServerEvent.BeginStop);
            await _client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            _stateMachine.Trigger(LspServerEvent.StopSucceeded);
            _logger.LogInformation("LSP server '{Name}' stopped", Name);
        } catch (Exception ex) {
            _lastError = ex;
            _stateMachine.ForceSet(LspServerState.Error);
            ErrorOccurred?.Invoke(this, new LspServerErrorEventArgs { Error = ex, ServerName = Name });
            throw;
        }
    }

    /// <summary>
    /// 重启 LSP 服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RestartAsync(CancellationToken cancellationToken = default) {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        _restartCount++;
        var maxRestarts = _config.MaxRestarts ?? DefaultMaxRestarts;
        if (_restartCount > maxRestarts) {
            throw new InvalidOperationException($"LSP server '{Name}' exceeded max restart attempts ({maxRestarts})");
        }

        await StartAsync(null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 发送 JSON-RPC 请求并等待响应 — 对 ContentModified 等瞬态错误自动重试
    /// </summary>
    /// <param name="method">请求方法名</param>
    /// <param name="params">请求参数 — null 表示无参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应 JSON 节点 — null 表示无返回值</returns>
    public async Task<JsonNode?> SendRequestAsync(string method, object? @params, CancellationToken cancellationToken = default) {
        if (!IsHealthy) {
            var errorMsg = _lastError != null ? $", last error: {_lastError.Message}" : "";
            throw new InvalidOperationException($"Cannot send request to LSP server '{Name}': server is {State}{errorMsg}");
        }

        Exception? lastAttemptError = null;

        for (var attempt = 0; attempt <= MaxRetriesForTransientErrors; attempt++) {
            try {
                return await SendRequestCoreAsync(method, @params, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (IsContentModifiedError(ex) && attempt < MaxRetriesForTransientErrors) {
                lastAttemptError = ex;
                var delay = RetryBaseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogDebug("LSP request '{Method}' to '{Name}' got ContentModified, retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    method, Name, delay, attempt + 1, MaxRetriesForTransientErrors);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            } catch (Exception ex) {
                lastAttemptError = ex;
                break;
            }
        }

        throw new InvalidOperationException($"LSP request '{method}' failed for server '{Name}': {lastAttemptError?.Message ?? "unknown error"}", lastAttemptError);
    }

    /// <summary>
    /// 发送 JSON-RPC 通知（无响应）
    /// </summary>
    /// <param name="method">通知方法名</param>
    /// <param name="params">通知参数 — null 表示无参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task SendNotificationAsync(string method, object? @params, CancellationToken cancellationToken = default) {
        if (!IsHealthy) {
            throw new InvalidOperationException($"Cannot send notification to LSP server '{Name}': server is {State}");
        }

        try {
            await SendNotificationCoreAsync(method, @params, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogError(ex, "LSP notification '{Method}' failed for server '{Name}'", method, Name);
            throw;
        }
    }

    private async Task<JsonNode?> SendRequestCoreAsync(string method, object? @params, CancellationToken cancellationToken) {
        var node = @params switch {
            JsonNode jn => jn,
            Dictionary<string, JsonElement> dict => JsonSerializer.SerializeToNode(dict, LspJsonContext.Default.DictionaryStringJsonElement),
            LspTextDocumentPositionParams p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspTextDocumentPositionParams),
            LspReferenceParams p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspReferenceParams),
            LspWorkspaceSymbolParams p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspWorkspaceSymbolParams),
            LspCallHierarchyItemParam p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspCallHierarchyItemParam),
            LspDidOpenTextDocumentParams p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspDidOpenTextDocumentParams),
            _ => null
        };
        return await _client.SendRequestCoreAsync(method, node, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendNotificationCoreAsync(string method, object? @params, CancellationToken cancellationToken) {
        var node = @params switch {
            JsonNode jn => jn,
            Dictionary<string, JsonElement> dict => JsonSerializer.SerializeToNode(dict, LspJsonContext.Default.DictionaryStringJsonElement),
            LspTextDocumentPositionParams p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspTextDocumentPositionParams),
            LspReferenceParams p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspReferenceParams),
            LspWorkspaceSymbolParams p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspWorkspaceSymbolParams),
            LspCallHierarchyItemParam p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspCallHierarchyItemParam),
            LspDidOpenTextDocumentParams p => JsonSerializer.SerializeToNode(p, LspJsonContext.Default.LspDidOpenTextDocumentParams),
            _ => null
        };
        await _client.SendNotificationAsync(method, node, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 注册通知处理器
    /// </summary>
    /// <param name="method">通知方法名</param>
    /// <param name="handler">通知处理委托</param>
    public void OnNotification(string method, Func<JsonNode?, CancellationToken, ValueTask> handler) {
        _client.OnNotification(method, handler);
    }

    /// <summary>
    /// 注册请求处理器
    /// </summary>
    /// <param name="method">请求方法名</param>
    /// <param name="handler">请求处理委托 — 返回响应 JSON 节点</param>
    public void OnRequest(string method, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>> handler) {
        _client.OnRequest(method, handler);
    }

    private static bool IsContentModifiedError(Exception ex) {
        return ex is InvalidOperationException ioe &&
               ioe.Data.Contains("LspErrorCode") &&
               ioe.Data["LspErrorCode"] is int code &&
               code == LspErrorContentModified;
    }

    /// <summary>
    /// 异步释放 — 停止服务器并释放底层客户端资源
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;

        await StopAsync().ConfigureAwait(false);
        await _client.DisposeAsync().ConfigureAwait(false);
    }
}