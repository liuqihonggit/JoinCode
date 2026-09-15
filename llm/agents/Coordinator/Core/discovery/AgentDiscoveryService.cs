namespace Core.Agents.Coordinator;

/// <summary>
/// 跨进程 Agent 发现服务 — 基于文件注册表实现本机 agent 注册、心跳、发现
/// <para>注册表路径：~/.jcc/agents/registry.json</para>
/// <para>写入用 FileMailboxLock 跨进程互斥 + MailboxActor 串行化</para>
/// <para>心跳超时（默认30秒）的 agent 自动过滤</para>
/// </summary>
[Register(typeof(IAgentDiscovery), ServiceLifetime.Singleton)]
public sealed partial class AgentDiscoveryService : ServiceEntity, IAgentDiscovery
{
    private readonly IFileSystem _fs;
    private readonly string _registryPath;
    private readonly string _registryDir;
    private readonly ILogger<AgentDiscoveryService>? _logger;
    private readonly IClockService _clock;
    private volatile bool _disposed;

    /// <summary>
    /// 创建 Agent 发现服务
    /// </summary>
    /// <param name="fs">文件系统</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="clock">时钟服务</param>
    public AgentDiscoveryService(
        IFileSystem fs,
        ILogger<AgentDiscoveryService>? logger = null,
        IClockService? clock = null)
        : this(fs, GetDefaultRegistryPath(), logger, clock)
    {
    }

    /// <summary>
    /// 内部构造函数 — 用于测试注入注册表路径
    /// </summary>
    internal AgentDiscoveryService(
        IFileSystem fs,
        string registryPath,
        ILogger<AgentDiscoveryService>? logger = null,
        IClockService? clock = null)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _registryPath = registryPath;
        _registryDir = Path.GetDirectoryName(registryPath) ?? throw new ArgumentException("Invalid registry path", nameof(registryPath));
    }

    private static string GetDefaultRegistryPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AppDataConstants.AppDataFolder,
            "agents");
        return Path.Combine(dir, "registry.json");
    }

    /// <summary>
    /// 注册本进程的 agent 到注册表
    /// </summary>
    public async Task RegisterAsync(AgentRegistryInfo info, CancellationToken cancellationToken = default)
    {
        await UpdateRegistryAsync(agents =>
        {
            agents.RemoveAll(a => a.AgentId == info.AgentId && a.SessionId == info.SessionId);
            agents.Add(info);
        }, cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Agent registered: {AgentId} (PID={Pid})", info.AgentId, info.ProcessId);
    }

    /// <summary>
    /// 注销指定 agent
    /// </summary>
    public async Task UnregisterAsync(string agentId, string sessionId, CancellationToken cancellationToken = default)
    {
        await UpdateRegistryAsync(agents =>
        {
            agents.RemoveAll(a => a.AgentId == agentId && a.SessionId == sessionId);
        }, cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Agent unregistered: {AgentId}", agentId);
    }

    /// <summary>
    /// 更新心跳时间
    /// </summary>
    public async Task HeartbeatAsync(string agentId, string sessionId, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        await UpdateRegistryAsync(agents =>
        {
            for (var i = 0; i < agents.Count; i++)
            {
                if (agents[i].AgentId == agentId && agents[i].SessionId == sessionId)
                {
                    agents[i].LastHeartbeat = now;
                    break;
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 发现所有活跃 agent（心跳未超时）
    /// </summary>
    public async Task<IReadOnlyList<AgentRegistryInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var agents = await ReadRegistryAsync(cancellationToken).ConfigureAwait(false);
        var now = _clock.GetUtcNow();
        return agents.Where(a => now - a.LastHeartbeat < TimeSpan.FromSeconds(30)).ToList();
    }

    /// <summary>
    /// 发现指定会话的所有活跃 agent
    /// </summary>
    public async Task<IReadOnlyList<AgentRegistryInfo>> DiscoverBySessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var agents = await DiscoverAsync(cancellationToken).ConfigureAwait(false);
        return agents.Where(a => a.SessionId == sessionId).ToList();
    }

    /// <summary>
    /// 启动心跳循环 — 定期更新心跳时间
    /// </summary>
    public async Task StartHeartbeatLoopAsync(string agentId, string sessionId, TimeSpan? interval = null, CancellationToken cancellationToken = default)
    {
        var heartbeatInterval = interval ?? TimeSpan.FromSeconds(10);
        using var timer = new PeriodicTimer(heartbeatInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_disposed) return;
            try
            {
                await HeartbeatAsync(agentId, sessionId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Heartbeat failed for {AgentId}", agentId);
            }
        }
    }

    private async Task UpdateRegistryAsync(Action<List<AgentRegistryInfo>> update, CancellationToken cancellationToken)
    {
        var fs = _fs;
        await using var fileLock = await FileMailboxLock.AcquireAsync(_registryPath, TimeSpan.FromSeconds(10), cancellationToken, _logger).ConfigureAwait(false);

        var agents = await ReadRegistryRawAsync(fs, cancellationToken).ConfigureAwait(false);
        update(agents);
        await WriteRegistryRawAsync(fs, agents, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<AgentRegistryInfo>> ReadRegistryAsync(CancellationToken cancellationToken)
    {
        var fs = _fs;
        if (!fs.FileExists(_registryPath))
            return [];

        await using var fileLock = await FileMailboxLock.AcquireAsync(_registryPath, TimeSpan.FromSeconds(10), cancellationToken, _logger).ConfigureAwait(false);
        return await ReadRegistryRawAsync(fs, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<AgentRegistryInfo>> ReadRegistryRawAsync(IFileSystem fs, CancellationToken cancellationToken)
    {
        if (!fs.FileExists(_registryPath))
            return [];

        try
        {
            var json = await fs.ReadAllTextAsync(_registryPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            var agents = RelaxedJsonSerializer.Deserialize(json, AgentDiscoveryJsonContext.Default.ListAgentRegistryInfo);
            return agents ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to read agent registry, starting fresh");
            return [];
        }
    }

    private async Task WriteRegistryRawAsync(IFileSystem fs, List<AgentRegistryInfo> agents, CancellationToken cancellationToken)
    {
        if (!fs.DirectoryExists(_registryDir))
            fs.CreateDirectory(_registryDir);

        var json = JsonSerializer.Serialize(agents, AgentDiscoveryJsonContext.Default.ListAgentRegistryInfo);
        await fs.WriteAllTextAsync(_registryPath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>释放资源</summary>
    protected override void OnDispose()
    {
        _disposed = true;
    }
}

[JsonSerializable(typeof(List<AgentRegistryInfo>))]
internal sealed partial class AgentDiscoveryJsonContext : JsonSerializerContext;
