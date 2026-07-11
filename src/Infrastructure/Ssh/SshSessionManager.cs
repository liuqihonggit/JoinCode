
namespace Core.Ssh;

[Register]
public sealed partial class SshSessionManager : ISshSessionManager
{
    private readonly ConcurrentDictionary<string, SshSession> _sessions = new();
    [Inject] private readonly ILogger<SshSessionManager>? _logger;
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly ITelemetryService? _telemetryService;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private int _isDisposed;

    public event EventHandler<SshSessionStateChangedEventArgs>? SessionStateChanged;

    public async Task<ISshSession> CreateSessionAsync(
        SshSessionConfig config,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

        ArgumentNullException.ThrowIfNull(config);

        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = new SshSession(config, _fs, _logger);
            _sessions[session.SessionId] = session;
            session.ConnectionStateChanged += OnSessionConnectionStateChanged;

            _logger?.LogInformation("SSH 会话已创建: {SessionId} -> {Username}@{Host}:{Port}",
                session.SessionId, config.Username, config.Host, config.Port);

            RecordSessionMetrics("create", true);
            return session;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public ISshSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public IReadOnlyList<ISshSession> GetActiveSessions()
    {
        return _sessions.Values
            .Where(s => s.ConnectionState == SshConnectionState.Connected)
            .ToList();
    }

    public async Task DestroySessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.ConnectionStateChanged -= OnSessionConnectionStateChanged;
                await session.DisposeAsync().ConfigureAwait(false);
                _logger?.LogInformation("SSH 会话已销毁: {SessionId}", sessionId);
                RecordSessionMetrics("destroy", true);
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void RecordSessionMetrics(string operation, bool isSuccess) =>
        _telemetryService?.RecordCount("ssh.session.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "SSH session operation count");

    private void OnSessionConnectionStateChanged(object? sender, SshConnectionStateChangedEventArgs e)
    {
        SessionStateChanged?.Invoke(this, new SshSessionStateChangedEventArgs
        {
            SessionId = e.SessionId,
            NewState = e.NewState,
            PreviousState = e.PreviousState,
            Error = e.Error
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var sessions = _sessions.Values.ToList();
            foreach (var session in sessions)
            {
                session.ConnectionStateChanged -= OnSessionConnectionStateChanged;
            }

            await Task.WhenAll(sessions.Select(s => s.DisposeAsync().AsTask())).ConfigureAwait(false);

            _sessions.Clear();
        }
        finally
        {
            _stateLock.Release();
            _stateLock.Dispose();
        }
    }
}
