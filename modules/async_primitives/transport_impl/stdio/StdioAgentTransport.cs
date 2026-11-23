namespace JoinCode.Transport;

/// <summary>
/// Stdio 传输实现 — 通过子进程 stdin/stdout 通讯
/// </summary>
public sealed partial class StdioAgentTransport : IAgentTransport {
    private readonly StdioProcessManager _processManager;
    private readonly StdioProcessConfig _config;
    private readonly ILogger<StdioAgentTransport>? _logger;
    private TransportState _state;
    private int _disposed;

    /// <inheritdoc/>
    public string TransportType => "stdio";

    /// <inheritdoc/>
    public TransportState State {
        get => _state;
        private set {
            if (_state != value) {
                _state = value;
                OnStateChanged?.Invoke(this, value);
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<TransportMessageEventArgs>? OnMessage;
    /// <inheritdoc/>
    public event EventHandler<TransportState>? OnStateChanged;

    /// <summary>
    /// 构造 Stdio 代理传输
    /// </summary>
    /// <param name="config">Stdio 进程配置</param>
    /// <param name="logger">日志记录器（可选）</param>
    public StdioAgentTransport(
        StdioProcessConfig config,
        ILogger<StdioAgentTransport>? logger = null) {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _processManager = new StdioProcessManager();
        _state = TransportState.Disconnected;
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken ct = default) {
        if (State == TransportState.Connected) return;

        State = TransportState.Connecting;
        try {
            await _processManager.StartAsync(_config, ct).ConfigureAwait(false);
            State = TransportState.Connected;
            _logger?.LogInformation("[StdioTransport] 已连接到 {Path} {Args}", _config.ExecutablePath, _config.Arguments);
        } catch (Exception ex) {
            State = TransportState.Failed;
            _logger?.LogError(ex, "[StdioTransport] 连接失败");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken ct = default) {
        if (State != TransportState.Connected) return;

        try {
            await _processManager.StopAsync().ConfigureAwait(false);
            State = TransportState.Disconnected;
            _logger?.LogInformation("[StdioTransport] 已断开");
        } catch (Exception ex) {
            _logger?.LogError(ex, "[StdioTransport] 断开失败");
            State = TransportState.Failed;
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task SendMessageAsync(string message, CancellationToken ct = default) {
        if (State != TransportState.Connected)
            throw new InvalidOperationException($"[TRN007] 传输未连接，当前状态: {State}");

        await _processManager.SendAsync(message, ct).ConfigureAwait(false);
        OnMessage?.Invoke(this, new TransportMessageEventArgs {
            Message = message,
            Channel = TransportChannel.Output
        });
    }

    /// <inheritdoc/>
    public async Task<string> WaitForOutputAsync(Func<string, bool> predicate, TimeSpan? timeout = null, CancellationToken ct = default) {
        if (State != TransportState.Connected)
            throw new InvalidOperationException($"[TRN007] 传输未连接，当前状态: {State}");

        var result = await _processManager.WaitForOutputAsync(predicate, timeout, ct).ConfigureAwait(false);
        OnMessage?.Invoke(this, new TransportMessageEventArgs {
            Message = result,
            Channel = TransportChannel.Output
        });
        return result;
    }

    /// <inheritdoc/>
    public async Task<string> WaitForErrorAsync(Func<string, bool> predicate, TimeSpan? timeout = null, CancellationToken ct = default) {
        if (State != TransportState.Connected)
            throw new InvalidOperationException($"[TRN007] 传输未连接，当前状态: {State}");

        var result = await _processManager.WaitForErrorAsync(predicate, timeout, ct).ConfigureAwait(false);
        OnMessage?.Invoke(this, new TransportMessageEventArgs {
            Message = result,
            Channel = TransportChannel.Error
        });
        return result;
    }

    /// <inheritdoc/>
    public Task<string> GetOutputAsync() => _processManager.GetOutputAsync();

    /// <inheritdoc/>
    public Task<string> GetOutputIncrementalAsync() => _processManager.GetOutputIncrementalAsync();

    /// <inheritdoc/>
    public Task<string> GetErrorAsync() => _processManager.GetErrorAsync();

    /// <inheritdoc/>
    public Task<string> GetErrorIncrementalAsync() => _processManager.GetErrorIncrementalAsync();

    /// <inheritdoc/>
    public Task ClearOutputAsync() => _processManager.ClearOutputAsync();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        var task = _processManager.DisposeAsync();
        State = TransportState.Disconnected;
        return task;
    }
}