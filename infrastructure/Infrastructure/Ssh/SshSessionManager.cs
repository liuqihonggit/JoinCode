
namespace Core.Ssh;

/// <summary>
/// SshSessionManager Actor 命令 — Channel 中的消息类型
/// </summary>
public interface ISshCommand;

internal sealed record CreateSessionCmd(SshSessionConfig Config, CancellationToken Ct, TaskCompletionSource<ISshSession> Tcs) : ISshCommand;
internal sealed record DestroySessionCmd(string SessionId, CancellationToken Ct, TaskCompletionSource Tcs) : ISshCommand;
internal sealed record CleanupSessionsCmd(TaskCompletionSource Tcs) : ISshCommand;

[Register(typeof(ISshSessionManager), ServiceLifetime.Singleton)]
public sealed partial class SshSessionManager : ActorBase<ISshCommand, Unit>, ISshSessionManager
{

    public SshSessionManager(IFileSystem fs, ILogger<SshSessionManager>? logger = null, ITelemetryService? telemetryService = null)
        : base()
    {
        _fs = fs;
        _logger = logger;
        _telemetryService = telemetryService;
    }
    private readonly ConcurrentDictionary<string, SshSession> _sessions = new();
    private readonly ILogger<SshSessionManager>? _logger;
    private readonly IFileSystem _fs;
    private readonly ITelemetryService? _telemetryService;
    private int _isDisposed;

    public event EventHandler<SshSessionStateChangedEventArgs>? SessionStateChanged;

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<ISshSession> CreateSessionAsync(
        SshSessionConfig config,
        CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);
        ArgumentNullException.ThrowIfNull(config);

        var tcs = CreateTcs<ISshSession>();
        await SendAsync(new CreateSessionCmd(config, ct, tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
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

        var tcs = CreateTcs();
        await SendAsync(new DestroySessionCmd(sessionId, ct, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
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

    /// <summary>
    /// Actor Consumer — 线程独占 _sessions，串行处理命令，无需锁。
    /// </summary>
    protected override async ValueTask HandleAsync(ISshCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case CreateSessionCmd cmd:
            {
                try
                {
                    var session = new SshSession(cmd.Config, _fs, _logger);
                    _sessions[session.SessionId] = session;
                    session.ConnectionStateChanged += OnSessionConnectionStateChanged;

                    _logger?.LogInformation("SSH 会话已创建: {SessionId} -> {Username}@{Host}:{Port}",
                        session.SessionId, cmd.Config.Username, cmd.Config.Host, cmd.Config.Port);

                    RecordSessionMetrics("create", true);
                    cmd.Tcs.TrySetResult(session);
                }
                catch (Exception ex) { cmd.Tcs.TrySetException(ex); }
                break;
            }

            case DestroySessionCmd cmd:
            {
                if (_sessions.TryRemove(cmd.SessionId, out var session))
                {
                    session.ConnectionStateChanged -= OnSessionConnectionStateChanged;
                    await session.DisposeAsync().ConfigureAwait(false);
                    _logger?.LogInformation("SSH 会话已销毁: {SessionId}", cmd.SessionId);
                    RecordSessionMetrics("destroy", true);
                }
                cmd.Tcs.TrySetResult();
                break;
            }

            case CleanupSessionsCmd cmd:
            {
                var sessions = _sessions.Values.ToList();
                foreach (var session in sessions)
                {
                    session.ConnectionStateChanged -= OnSessionConnectionStateChanged;
                }

                await Task.WhenAll(sessions.Select(s => s.DisposeAsync().AsTask())).ConfigureAwait(false);

                _sessions.Clear();
                cmd.Tcs.TrySetResult();
                break;
            }
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[SshSessionManager] Actor Consumer 异常");
    }

    public async override ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed))
        {
            return;
        }

        var tcs = CreateTcs();
        await SendAsync(new CleanupSessionsCmd(tcs), CancellationToken.None).ConfigureAwait(false);
        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[SshSessionManager] 清理会话异常");
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }
}
