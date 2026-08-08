
namespace Core.Ssh;

public sealed class SshForwardedPort : ISshForwardedPort
{
    private readonly string _sessionId;
    private readonly SshSessionConfig _config;
    private readonly ILogger? _logger;
    private Process? _forwardProcess;
    private int _isDisposed;

    public string ForwardId { get; }
    public SshForwardType ForwardType { get; }
    public string LocalEndpoint { get; }
    public string RemoteEndpoint { get; }
    public bool IsForwarding { get; private set; }

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

    public Task StartAsync(CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

        var args = new List<string>
        {
            "-N",
            "-o",
            $"ConnectTimeout={_config.ConnectionTimeoutMs / 1000}",
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

    public Task StopAsync(CancellationToken ct = default)
    {
        DisposableHelper.ThrowIfDisposed(ref _isDisposed, this);

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

    public async ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed))
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _forwardProcess?.Dispose();
    }
}
