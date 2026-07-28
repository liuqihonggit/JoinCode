namespace JoinCode.Transport;

/// <summary>
/// Stdio 传输实现 — 通过子进程 stdin/stdout 通讯
/// </summary>
public sealed partial class StdioAgentTransport : IAgentTransport
{
    private readonly StdioProcessManager _processManager;
    private readonly StdioProcessConfig _config;
    [Inject] private readonly ILogger<StdioAgentTransport>? _logger;
    private TransportState _state;

    public string TransportType => "stdio";

    public TransportState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnStateChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<TransportMessageEventArgs>? OnMessage;
    public event EventHandler<TransportState>? OnStateChanged;

    public StdioAgentTransport(
        StdioProcessConfig config,
        ILogger<StdioAgentTransport>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _processManager = new StdioProcessManager();
        _state = TransportState.Disconnected;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State == TransportState.Connected) return;

        State = TransportState.Connecting;
        try
        {
            await _processManager.StartAsync(_config, ct).ConfigureAwait(false);
            State = TransportState.Connected;
            _logger?.LogInformation("[StdioTransport] 已连接到 {Path} {Args}", _config.ExecutablePath, _config.Arguments);
        }
        catch (Exception ex)
        {
            State = TransportState.Failed;
            _logger?.LogError(ex, "[StdioTransport] 连接失败");
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (State != TransportState.Connected) return;

        try
        {
            await _processManager.StopAsync().ConfigureAwait(false);
            State = TransportState.Disconnected;
            _logger?.LogInformation("[StdioTransport] 已断开");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[StdioTransport] 断开失败");
            State = TransportState.Failed;
            throw;
        }
    }

    public async Task SendMessageAsync(string message, CancellationToken ct = default)
    {
        if (State != TransportState.Connected)
            throw new InvalidOperationException($"传输未连接，当前状态: {State}");

        await _processManager.SendAsync(message, ct).ConfigureAwait(false);
        OnMessage?.Invoke(this, new TransportMessageEventArgs
        {
            Message = message,
            Channel = TransportChannel.Output
        });
    }

    public async Task<string> WaitForOutputAsync(Func<string, bool> predicate, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (State != TransportState.Connected)
            throw new InvalidOperationException($"传输未连接，当前状态: {State}");

        var result = await _processManager.WaitForOutputAsync(predicate, timeout, ct).ConfigureAwait(false);
        OnMessage?.Invoke(this, new TransportMessageEventArgs
        {
            Message = result,
            Channel = TransportChannel.Output
        });
        return result;
    }

    public async Task<string> WaitForErrorAsync(Func<string, bool> predicate, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (State != TransportState.Connected)
            throw new InvalidOperationException($"传输未连接，当前状态: {State}");

        var result = await _processManager.WaitForErrorAsync(predicate, timeout, ct).ConfigureAwait(false);
        OnMessage?.Invoke(this, new TransportMessageEventArgs
        {
            Message = result,
            Channel = TransportChannel.Error
        });
        return result;
    }

    public Task<string> GetOutputAsync() => _processManager.GetOutputAsync();

    public Task<string> GetOutputIncrementalAsync() => _processManager.GetOutputIncrementalAsync();

    public Task<string> GetErrorAsync() => _processManager.GetErrorAsync();

    public Task<string> GetErrorIncrementalAsync() => _processManager.GetErrorIncrementalAsync();

    public Task ClearOutputAsync() => _processManager.ClearOutputAsync();

    public async ValueTask DisposeAsync()
    {
        await _processManager.DisposeAsync().ConfigureAwait(false);
        State = TransportState.Disconnected;
    }
}
