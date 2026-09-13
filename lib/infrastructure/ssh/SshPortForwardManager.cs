
namespace Core.Ssh;

/// <summary>
/// SSH 端口转发管理器 — 集中管理多个 SshForwardedPort 实例，支持本地/远程转发的添加、查询与统一停止
/// </summary>
public sealed class SshPortForwardManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SshForwardedPort> _forwards = new();
    private readonly ILogger? _logger;
    private int _isDisposed;

    /// <summary>
    /// 构造端口转发管理器
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public SshPortForwardManager(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 添加并启动本地端口转发 — 将本地端口映射到远程主机端口
    /// </summary>
    /// <param name="sessionId">所属 SSH 会话标识</param>
    /// <param name="config">SSH 会话配置</param>
    /// <param name="localPort">本地监听端口</param>
    /// <param name="remoteHost">远程目标主机</param>
    /// <param name="remotePort">远程目标端口</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已启动的本地端口转发实例</returns>
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

    /// <summary>
    /// 添加并启动远程端口转发 — 将远程主机端口映射回本地端口
    /// </summary>
    /// <param name="sessionId">所属 SSH 会话标识</param>
    /// <param name="config">SSH 会话配置</param>
    /// <param name="remotePort">远程监听端口</param>
    /// <param name="localHost">本地绑定主机</param>
    /// <param name="localPort">本地目标端口</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已启动的远程端口转发实例</returns>
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

    /// <summary>
    /// 获取所有处于活动状态的端口转发
    /// </summary>
    /// <returns>活动端口转发的集合</returns>
    public IEnumerable<ISshForwardedPort> GetActiveForwards()
    {
        return _forwards.Values.Where(f => f.IsForwarding);
    }

    /// <summary>
    /// 停止并清空所有端口转发
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步停止操作的任务</returns>
    public async Task StopAllAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(_forwards.Values.Select(forward => forward.StopAsync(ct))).ConfigureAwait(false);

        _forwards.Clear();
    }

    /// <summary>
    /// 异步释放资源 — 停止所有端口转发
    /// </summary>
    /// <returns>表示异步释放操作的任务</returns>
    public async ValueTask DisposeAsync()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _isDisposed))
        {
            return;
        }

        await StopAllAsync().ConfigureAwait(false);
    }
}
