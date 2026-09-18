using System.Threading;

namespace Core.Ssh;

/// <summary>
/// SSH 端口转发实现 — 通过启动 ssh 子进程建立本地或远程端口转发
/// </summary>
public sealed class SshForwardedPort : ISshForwardedPort
{
    private readonly string _sessionId;
    private readonly SshSessionConfig _config;
    private readonly ILogger? _logger;
    private Process? _forwardProcess;
    private int _isDisposed;

    /// <summary>获取本次转发的唯一标识符</summary>
    public string ForwardId { get; }
    /// <summary>获取转发类型（本地或远程）</summary>
    public SshForwardType ForwardType { get; }
    /// <summary>获取本地端点地址（host:port 形式）</summary>
    public string LocalEndpoint { get; }
    /// <summary>获取远程端点地址（host:port 形式）</summary>
    public string RemoteEndpoint { get; }
    /// <summary>获取端口转发是否处于活动状态</summary>
    public bool IsForwarding { get; private set; }

    /// <summary>
    /// 构造 SSH 端口转发实例
    /// </summary>
    /// <param name="forwardType">转发类型</param>
    /// <param name="localEndpoint">本地端点地址</param>
    /// <param name="remoteEndpoint">远程端点地址</param>
    /// <param name="sessionId">所属 SSH 会话标识</param>
    /// <param name="config">SSH 会话配置</param>
    /// <param name="logger">可选日志记录器</param>
    public SshForwardedPort(
        SshForwardType forwardType,
        string localEndpoint,
        string remoteEndpoint,
        string sessionId,
        SshSessionConfig config,
        ILogger? logger = null)
    {
        ForwardId = Guid.NewGuid().ToString("N")[..12];
        ForwardType = forwardType;
        LocalEndpoint = localEndpoint;
        RemoteEndpoint = remoteEndpoint;
        _sessionId = sessionId;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 启动端口转发 — 拉起 ssh 子进程并按转发类型构造参数
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步启动操作的任务</returns>
    public Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        var args = new List<string>
        {
            "-N",
            "-o",
            $"ConnectTimeout={_config.ConnectionTimeoutMs / 1000}",
            "-o",
            "ServerAliveInterval=30",
            "-o",
            "ServerAliveCountMax=3",
            "-o",
            "ExitOnForwardFailure=yes",
            "-o",
            "StrictHostKeyChecking=" + (_config.KnownHostsPolicy switch
            {
                SshKnownHostsPolicy.Strict => "yes",
                SshKnownHostsPolicy.AcceptNew => "accept-new",
                SshKnownHostsPolicy.Ignore => "no",
                _ => "accept-new"
            }),
        };

        if (_config.AuthMethod == SshAuthMethod.PrivateKey && _config.PrivateKey != null)
        {
            var keyFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                AppDataConstants.AppDataFolder, "ssh", $"key_{_sessionId}");
            args.Add("-i");
            args.Add(keyFile);
        }

        args.Add(ForwardType switch
        {
            SshForwardType.Local => "-L",
            SshForwardType.Remote => "-R",
            _ => throw new ArgumentOutOfRangeException(nameof(ForwardType))
        });
        args.Add(ForwardType == SshForwardType.Local ? LocalEndpoint : RemoteEndpoint);
        args.Add(ForwardType == SshForwardType.Local ? RemoteEndpoint : LocalEndpoint);

        args.Add("-p");
        args.Add(_config.Port.ToString());
        args.Add($"{_config.Username}@{_config.Host}");

        var builder = new IO.ProcessService.ProcessStartInfoBuilder(new IO.ProcessService.ProcessEncodingProvider());
        var startInfo = builder.Build(new ProcessOptions
        {
            FileName = "ssh",
            ArgumentList = args,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        });

        _forwardProcess = Process.Start(startInfo);
        IsForwarding = _forwardProcess != null && !_forwardProcess.HasExited;

        _logger?.LogInformation("SSH 端口转发已启动: {ForwardId} ({Type} {Local} -> {Remote})",
            ForwardId, ForwardType, LocalEndpoint, RemoteEndpoint);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止端口转发 — 终止 ssh 子进程并标记为非活动
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步停止操作的任务</returns>
    public Task StopAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        if (_forwardProcess != null && !_forwardProcess.HasExited)
        {
            try
            {
                _forwardProcess.Kill();
            }
            catch (InvalidOperationException ex) { _logger?.LogWarning(ex, "SshForwardedPort: 终止端口转发进程失败"); }
        }

        IsForwarding = false;
        _logger?.LogInformation("SSH 端口转发已停止: {ForwardId}", ForwardId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步释放资源 — 停止转发并销毁底层 ssh 子进程
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _forwardProcess?.Dispose();
        return new ValueTask(StopAsync());
    }
}
