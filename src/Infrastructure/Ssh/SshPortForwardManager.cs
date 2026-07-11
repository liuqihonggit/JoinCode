
namespace Core.Ssh;

public sealed class SshPortForwardManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SshForwardedPort> _forwards = new();
    private readonly ILogger? _logger;
    private int _isDisposed;

    public SshPortForwardManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    public async Task<ISshForwardedPort> AddLocalForwardAsync(
        string sessionId,
        SshSessionConfig config,
        int localPort,
        string remoteHost,
        int remotePort,
        CancellationToken ct = default)
    {
        var forward = new SshForwardedPort(
            SshForwardType.Local,
            $"127.0.0.1:{localPort}",
            $"{remoteHost}:{remotePort}",
            sessionId,
            config,
            _logger);

        _forwards[forward.ForwardId] = forward;
        await forward.StartAsync(ct).ConfigureAwait(false);

        _logger?.LogInformation("SSH 本地端口转发已创建: {LocalPort} -> {RemoteHost}:{RemotePort}",
            localPort, remoteHost, remotePort);

        return forward;
    }

    public async Task<ISshForwardedPort> AddRemoteForwardAsync(
        string sessionId,
        SshSessionConfig config,
        int remotePort,
        string localHost,
        int localPort,
        CancellationToken ct = default)
    {
        var forward = new SshForwardedPort(
            SshForwardType.Remote,
            $"{localHost}:{localPort}",
            $"127.0.0.1:{remotePort}",
            sessionId,
            config,
            _logger);

        _forwards[forward.ForwardId] = forward;
        await forward.StartAsync(ct).ConfigureAwait(false);

        _logger?.LogInformation("SSH 远程端口转发已创建: {RemotePort} -> {LocalHost}:{LocalPort}",
            remotePort, localHost, localPort);

        return forward;
    }

    public IReadOnlyList<ISshForwardedPort> GetActiveForwards()
    {
        return _forwards.Values.Where(f => f.IsForwarding).ToList();
    }

    public async Task StopAllAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(_forwards.Values.Select(forward => forward.StopAsync(ct))).ConfigureAwait(false);

        _forwards.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        await StopAllAsync().ConfigureAwait(false);
    }
}
