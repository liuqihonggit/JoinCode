
namespace Core.Ssh;

/// <summary>
/// SshSessionManager Actor 命令 — Channel 中的消息类型
/// </summary>
public interface ISshCommand;

internal sealed record CreateSessionCmd(SshSessionConfig Config, CancellationToken Ct, TaskCompletionSource<ISshSession> Tcs) : ISshCommand;
internal sealed record DestroySessionCmd(string SessionId, CancellationToken Ct, TaskCompletionSource Tcs) : ISshCommand;
internal sealed record CleanupSessionsCmd(TaskCompletionSource Tcs) : ISshCommand;

/// <summary>
/// SSH 会话管理器 — Actor 化串行处理会话创建、销毁与清理命令，线程独占 _sessions 字典
/// </summary>
[Register(typeof(ISshSessionManager), ServiceLifetime.Singleton)]
public sealed partial class SshSessionManager : ActorBase<ISshCommand, Unit>, ISshSessionManager
{

    /// <summary>
    /// 构造会话管理器
    /// </summary>
    /// <param name="fs">文件系统抽象，传递给会话实例用于写入私钥</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="telemetryService">可选遥测服务，用于记录会话操作计数</param>
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

    /// <summary>子会话连接状态变更时触发，参数携带会话标识与状态信息</summary>
    public event EventHandler<SshSessionStateChangedEventArgs>? SessionStateChanged;

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 创建新的 SSH 会话 — 通过 Actor 串行处理，订阅其状态变更事件
    /// </summary>
    /// <param name="config">SSH 会话配置</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已创建的 SSH 会话实例</returns>
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

    /// <summary>
    /// 按标识获取已存在的会话实例
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>命中时返回会话实例，未命中时返回 null</returns>
    public ISshSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    /// <summary>
    /// 获取所有处于 Connected 状态的活动会话
    /// </summary>
    /// <returns>活动会话集合</returns>
    public IEnumerable<ISshSession> GetActiveSessions()
    {
        return _sessions.Values
            .Where(s => s.ConnectionState == SshConnectionState.Connected);
    }

    /// <summary>
    /// 销毁指定会话 — 通过 Actor 串行处理，移除并释放对应会话
    /// </summary>
    /// <param name="sessionId">待销毁会话标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步销毁操作的任务</returns>
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
        ToolTelemetryHelper.RecordToolCount(_telemetryService, "ssh.session.count", operation, isSuccess, "SSH session operation count");

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

    /// <summary>消费者异常回调 — 记录日志</summary>
    /// <param name="ex">异常对象</param>
    protected override void OnConsumerError(Exception ex)
    {
        _logger?.LogError(ex, "[SshSessionManager] Actor Consumer 异常");
    }

    /// <summary>
    /// 异步释放资源 — 通过 Actor 串行清理所有会话后释放基类
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
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
