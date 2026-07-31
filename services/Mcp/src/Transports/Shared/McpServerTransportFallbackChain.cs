namespace McpClient.Transports;

public sealed class McpServerTransportFallbackChain : IMcpTransport
{
    private readonly IMcpTransport[] _transports;
    private readonly ILogger? _logger;
    private IMcpTransport? _activeTransport;
    private int _activeIndex = -1;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private int _disposed;

    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;
    public event EventHandler<TransportFallbackEventArgs>? FallbackOccurred;

    public bool IsRunning => _activeTransport?.IsRunning ?? false;
    public string ActiveTransportType => _activeTransport?.GetType().Name ?? "none";
    public int ActiveTransportIndex => _activeIndex;

    public McpServerTransportFallbackChain(
        IMcpTransport[] transports,
        ILogger? logger = null)
    {
        _transports = transports ?? throw new ArgumentNullException(nameof(transports));
        _logger = logger;

        if (_transports.Length == 0)
            throw new ArgumentException("At least one transport is required", nameof(transports));
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return;

        for (var i = 0; i < _transports.Length; i++)
        {
            try
            {
                _logger?.LogInformation("[ServerFallback] Starting transport {Type} (priority={Priority})",
                    _transports[i].GetType().Name, i + 1);

                await _transports[i].StartAsync(ct).ConfigureAwait(false);
                _activeTransport = _transports[i];
                _activeIndex = i;
                WireEvents(_activeTransport);

                _logger?.LogInformation("[ServerFallback] Server started on {Type} (priority={Priority})",
                    _transports[i].GetType().Name, i + 1);
                return;
            }
            catch (Exception ex) when (i < _transports.Length - 1)
            {
                _logger?.LogWarning(ex, "[ServerFallback] Transport {Type} start failed, falling back to next",
                    _transports[i].GetType().Name);
            }
        }

        throw new InvalidOperationException("All server transports failed to start");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_activeTransport is not null)
        {
            UnwireEvents(_activeTransport);
            await _activeTransport.StopAsync(ct).ConfigureAwait(false);
            _activeTransport = null;
            _activeIndex = -1;
        }
    }

    public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (_activeTransport is null)
            throw new InvalidOperationException("No active transport");

        await _activeTransport.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task OnActiveTransportErrorAsync(Exception ex)
    {
        if (_activeIndex < 0 || _activeIndex >= _transports.Length - 1) return;

        await _switchLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_activeIndex >= _transports.Length - 1) return;

            var fromType = _transports[_activeIndex].GetType().Name;
            var nextIndex = _activeIndex + 1;

            _logger?.LogWarning(ex, "[ServerFallback] Transport {FromType} runtime error, degrading to {ToType}",
                fromType, _transports[nextIndex].GetType().Name);

            if (_activeTransport is not null)
            {
                UnwireEvents(_activeTransport);
                await _activeTransport.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }

            try
            {
                await _transports[nextIndex].StartAsync(CancellationToken.None).ConfigureAwait(false);
                _activeTransport = _transports[nextIndex];
                _activeIndex = nextIndex;
                WireEvents(_activeTransport);

                FallbackOccurred?.Invoke(this, new TransportFallbackEventArgs
                {
                    FromTransportType = fromType,
                    ToTransportType = _transports[nextIndex].GetType().Name,
                    Reason = ex.Message,
                    IsServerSide = true,
                    FromPriority = _activeIndex,
                    ToPriority = nextIndex + 1,
                });

                _logger?.LogInformation("[ServerFallback] Degraded to {Type} successfully",
                    _transports[nextIndex].GetType().Name);
            }
            catch (Exception fallbackEx)
            {
                _logger?.LogError(fallbackEx, "[ServerFallback] Degradation to {Type} also failed",
                    _transports[nextIndex].GetType().Name);
            }
        }
        finally
        {
            _switchLock.Release();
        }
    }

    private void WireEvents(IMcpTransport transport)
    {
        transport.MessageReceived += OnTransportMessageReceived;
        transport.ErrorOccurred += OnTransportError;
    }

    private void UnwireEvents(IMcpTransport transport)
    {
        transport.MessageReceived -= OnTransportMessageReceived;
        transport.ErrorOccurred -= OnTransportError;
    }

    private void OnTransportMessageReceived(object? sender, McpMessageReceivedEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    private void OnTransportError(object? sender, McpTransportErrorEventArgs e)
    {
        ErrorOccurred?.Invoke(this, e);

        if (IsRunning)
        {
            _ = OnActiveTransportErrorAsync(e.Exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        await StopAsync().ConfigureAwait(false);
        _switchLock.Dispose();

        foreach (var transport in _transports)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
