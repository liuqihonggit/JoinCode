namespace Services.Lsp.Internal;

public enum LspServerState
{
    [EnumValue("stopped")] Stopped,
    [EnumValue("starting")] Starting,
    [EnumValue("running")] Running,
    [EnumValue("stopping")] Stopping,
    [EnumValue("error")] Error
}

public interface ILspServerInstance : IAsyncDisposable
{
    string Name { get; }
    LspServerState State { get; }
    LspInstanceConfig Config { get; }
    Exception? LastError { get; }
    bool IsHealthy { get; }

    event EventHandler<LspServerErrorEventArgs>? ErrorOccurred;
    event EventHandler<LspServerStateChangedEventArgs>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task RestartAsync(CancellationToken cancellationToken = default);
    Task<JsonNode?> SendRequestAsync(string method, object? @params, CancellationToken cancellationToken = default);
    Task SendNotificationAsync(string method, object? @params, CancellationToken cancellationToken = default);
    void OnNotification(string method, Func<JsonNode?, CancellationToken, ValueTask> handler);
    void OnRequest(string method, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>> handler);
}

public sealed partial class LspServerErrorEventArgs : EventArgs
{
    public required Exception Error { get; init; }
    public required string ServerName { get; init; }
}

public sealed partial class LspServerStateChangedEventArgs : EventArgs
{
    public required LspServerState OldState { get; init; }
    public required LspServerState NewState { get; init; }
}

public sealed partial class LspInstanceConfig
{
    public required string Name { get; init; }
    public required string LanguageId { get; init; }
    public required string Command { get; init; }
    public List<string> Arguments { get; init; } = [];
    public Dictionary<string, string>? Environment { get; init; }
    public string? WorkingDirectory { get; init; }
    public TimeSpan? StartupTimeout { get; init; }
    public int? MaxRestarts { get; init; }
    public Dictionary<string, string> ExtensionToLanguage { get; init; } = [];
}

public sealed partial class LspServerInstance : ILspServerInstance
{
    private const int LspErrorContentModified = -32801;
    private const int MaxRetriesForTransientErrors = 3;
    private const int RetryBaseDelayMs = 500;
    private const int DefaultMaxRestarts = 3;

    private readonly LspInstanceConfig _config;
    [Inject] private readonly ILogger<LspServerInstance> _logger;
    private readonly LspClient _client;

    private int _currentState = (int)LspServerState.Stopped;
    private Exception? _lastError;
    private int _crashRecoveryCount;
    private int _restartCount;
    private int _isDisposed;

    public string Name => _config.Name;
    public LspInstanceConfig Config => _config;

    public LspServerState State
    {
        get => (LspServerState)Volatile.Read(ref _currentState);
        private set
        {
            var oldState = (LspServerState)Interlocked.Exchange(ref _currentState, (int)value);
            if (oldState != value)
            {
                _logger.LogInformation("LSP server '{Name}' state: {OldState} → {NewState}", Name, oldState, value);
                StateChanged?.Invoke(this, new LspServerStateChangedEventArgs { OldState = oldState, NewState = value });
            }
        }
    }

    public Exception? LastError => Volatile.Read(ref _lastError);
    public bool IsHealthy => State == LspServerState.Running && _client.IsConnected;

    public event EventHandler<LspServerErrorEventArgs>? ErrorOccurred;
    public event EventHandler<LspServerStateChangedEventArgs>? StateChanged;

    public LspServerInstance(LspInstanceConfig config, IFileSystem fs, IProcessService processService, ILogger<LspServerInstance> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = new LspClient(fs, processService);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (State is LspServerState.Running or LspServerState.Starting)
        {
            _logger.LogDebug("LSP server '{Name}' is already {State}", Name, State);
            return;
        }

        var maxRestarts = _config.MaxRestarts ?? DefaultMaxRestarts;
        if (State == LspServerState.Error && _crashRecoveryCount > maxRestarts)
        {
            var error = new InvalidOperationException($"LSP server '{Name}' exceeded max crash recovery attempts ({maxRestarts})");
            _lastError = error;
            ErrorOccurred?.Invoke(this, new LspServerErrorEventArgs { Error = error, ServerName = Name });
            throw error;
        }

        try
        {
            State = LspServerState.Starting;

            var connected = await _client.ConnectAsync(new LspServerConfig
            {
                LanguageId = _config.LanguageId,
                Command = _config.Command,
                Arguments = _config.Arguments
            }, cancellationToken).ConfigureAwait(false);

            if (!connected)
            {
                throw new InvalidOperationException($"Failed to connect to LSP server '{Name}'");
            }

            State = LspServerState.Running;
            _crashRecoveryCount = 0;
            _logger.LogInformation("LSP server '{Name}' started successfully", Name);
        }
        catch (Exception ex)
        {
            _lastError = ex;
            State = LspServerState.Error;
            ErrorOccurred?.Invoke(this, new LspServerErrorEventArgs { Error = ex, ServerName = Name });
            _logger.LogError(ex, "Failed to start LSP server '{Name}'", Name);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (State is LspServerState.Stopped or LspServerState.Stopping)
        {
            return;
        }

        try
        {
            State = LspServerState.Stopping;
            await _client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            State = LspServerState.Stopped;
            _logger.LogInformation("LSP server '{Name}' stopped", Name);
        }
        catch (Exception ex)
        {
            _lastError = ex;
            State = LspServerState.Error;
            ErrorOccurred?.Invoke(this, new LspServerErrorEventArgs { Error = ex, ServerName = Name });
            throw;
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        _restartCount++;
        var maxRestarts = _config.MaxRestarts ?? DefaultMaxRestarts;
        if (_restartCount > maxRestarts)
        {
            throw new InvalidOperationException($"LSP server '{Name}' exceeded max restart attempts ({maxRestarts})");
        }

        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonNode?> SendRequestAsync(string method, object? @params, CancellationToken cancellationToken = default)
    {
        if (!IsHealthy)
        {
            var errorMsg = _lastError != null ? $", last error: {_lastError.Message}" : "";
            throw new InvalidOperationException($"Cannot send request to LSP server '{Name}': server is {State}{errorMsg}");
        }

        Exception? lastAttemptError = null;

        for (var attempt = 0; attempt <= MaxRetriesForTransientErrors; attempt++)
        {
            try
            {
                return await SendRequestCoreAsync(method, @params, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsContentModifiedError(ex) && attempt < MaxRetriesForTransientErrors)
            {
                lastAttemptError = ex;
                var delay = RetryBaseDelayMs * (int)Math.Pow(2, attempt);
                _logger.LogDebug("LSP request '{Method}' to '{Name}' got ContentModified, retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    method, Name, delay, attempt + 1, MaxRetriesForTransientErrors);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (Exception ex)
            {
                lastAttemptError = ex;
                break;
            }
        }

        throw new InvalidOperationException($"LSP request '{method}' failed for server '{Name}': {lastAttemptError?.Message ?? "unknown error"}", lastAttemptError);
    }

    public async Task SendNotificationAsync(string method, object? @params, CancellationToken cancellationToken = default)
    {
        if (!IsHealthy)
        {
            throw new InvalidOperationException($"Cannot send notification to LSP server '{Name}': server is {State}");
        }

        try
        {
            await SendNotificationCoreAsync(method, @params, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LSP notification '{Method}' failed for server '{Name}'", method, Name);
            throw;
        }
    }

    private async Task<JsonNode?> SendRequestCoreAsync(string method, object? @params, CancellationToken cancellationToken)
    {
        var node = @params switch
        {
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

    private async Task SendNotificationCoreAsync(string method, object? @params, CancellationToken cancellationToken)
    {
        var node = @params switch
        {
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

    public void OnNotification(string method, Func<JsonNode?, CancellationToken, ValueTask> handler)
    {
        _client.OnNotification(method, handler);
    }

    public void OnRequest(string method, Func<string, JsonNode?, CancellationToken, ValueTask<JsonNode?>> handler)
    {
        _client.OnRequest(method, handler);
    }

    private static bool IsContentModifiedError(Exception ex)
    {
        return ex is InvalidOperationException ioe &&
               ioe.Data.Contains("LspErrorCode") &&
               ioe.Data["LspErrorCode"] is int code &&
               code == LspErrorContentModified;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1) return;

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"LSP server instance stop failed during dispose: {ex.Message}");
        }

        await _client.DisposeAsync().ConfigureAwait(false);
    }
}
