
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

        var forwardArg = ForwardType switch
        {
            SshForwardType.Local => $"-L {LocalEndpoint} {RemoteEndpoint}",
            SshForwardType.Remote => $"-R {RemoteEndpoint} {LocalEndpoint}",
            _ => throw new ArgumentOutOfRangeException(nameof(ForwardType))
        };

        var args = new StringBuilder();
        args.Append(" -N");
        args.Append($" -o ConnectTimeout={_config.ConnectionTimeoutMs / 1000}");
        args.Append(_config.KnownHostsPolicy switch
        {
            SshKnownHostsPolicy.Strict => " -o StrictHostKeyChecking=yes",
            SshKnownHostsPolicy.AcceptNew => " -o StrictHostKeyChecking=accept-new",
            SshKnownHostsPolicy.Ignore => " -o StrictHostKeyChecking=no",
            _ => " -o StrictHostKeyChecking=accept-new"
        });

        if (_config.AuthMethod == SshAuthMethod.PrivateKey && _config.PrivateKey != null)
        {
            var keyFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDataConstants.AppDataFolder, "ssh", $"key_{_sessionId}");
            args.Append($" -i \"{keyFile}\"");
        }

        args.Append($" {forwardArg}");
        args.Append($" -p {_config.Port}");
        args.Append($" {_config.Username}@{_config.Host}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "ssh",
            Arguments = args.ToString(),
            UseShellExecute = false,
            CreateNoWindow = true
        };

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
            catch (InvalidOperationException ex) { System.Diagnostics.Trace.WriteLine($"SshForwardedPort: failed to kill forward process: {ex.Message}"); }
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
