
namespace Core.Ssh;

[Register(typeof(ISshSessionManager), ServiceLifetime.Singleton)]
public sealed partial class SshSessionManager : ISshSessionManager
{

    public SshSessionManager(IFileSystem fs, ILogger<SshSessionManager>? logger = null, ITelemetryService? telemetryService = null)
    {
        _fs = fs;
        _logger = logger;
        _telemetryService = telemetryService;
    }
    private readonly ConcurrentDictionary<string, SshSession> _sessions = new();
    private readonly ILogger<SshSessionManager>? _logger;
    private readonly IFileSystem _fs;
    private readonly ITelemetryService? _telemetryService;
    private readonly AsyncLock _stateLock = new();
    private int _isDisposed;

    public event EventHandler<SshSessionStateChangedEventArgs>? SessionStateChanged;

    public async Task<ISshSession> CreateSessionAsync(
        SshSessionConfig config,
        CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        ArgumentNullException.ThrowIfNull(config);

        using var guard = _stateLock.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");

        var session = new SshSession(config, _fs, _logger);
        _sessions[session.SessionId] = session;
        session.ConnectionStateChanged += OnSessionConnectionStateChanged;

        _logger?.LogInformation("SSH 会话已创建: {SessionId} -> {Username}@{Host}:{Port}",
            session.SessionId, config.Username, config.Host, config.Port);

        RecordSessionMetrics("create", true);
        return session;
    
    }

    public ISshSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    public IEnumerable<ISshSession> GetActiveSessions()
    {
        return _sessions.Values
            .Where(s => s.ConnectionState == SshConnectionState.Connected);
    }

    public async Task DestroySessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        using var guard = _stateLock.TryLock(ct) ?? throw new System.TimeoutException("锁等待超时");

        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.ConnectionStateChanged -= OnSessionConnectionStateChanged;
            await session.DisposeAsync().ConfigureAwait(false);
            _logger?.LogInformation("SSH 会话已销毁: {SessionId}", sessionId);
            RecordSessionMetrics("destroy", true);
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
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed))
        {
            return;
        }

        await CleanupSessionsAsync().ConfigureAwait(false);
        _stateLock.Dispose();
    }

    /// <summary>清理所有 SSH 会话（在锁保护下执行）</summary>
    private async Task CleanupSessionsAsync()
    {
        using var guard = _stateLock.TryLock() ?? throw new System.TimeoutException("锁等待超时");
        var sessions = _sessions.Values.ToList();
        foreach (var session in sessions)
        {
            session.ConnectionStateChanged -= OnSessionConnectionStateChanged;
        }

        await Task.WhenAll(sessions.Select(s => s.DisposeAsync().AsTask())).ConfigureAwait(false);

        _sessions.Clear();
    }
}
