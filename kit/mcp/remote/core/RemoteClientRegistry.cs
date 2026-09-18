namespace McpToolRegistry;

/// <summary>
/// 远程客户端注册表 — 管理客户端的注册、注销、查询、清空
/// 持有以 clientId 为 key 的客户端字典，提供所有客户端生命周期操作
/// </summary>
internal sealed class RemoteClientRegistry
{
    private readonly ConcurrentDictionary<string, McpClientEntry> _clients = new();
    private readonly IClockService _clock;

    /// <summary>初始化 <see cref="RemoteClientRegistry"/> 实例</summary>
    /// <param name="clock">时钟服务（用于记录注册时间）</param>
    public RemoteClientRegistry(IClockService clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>当前已注册客户端数量</summary>
    public int Count => _clients.Count;

    /// <summary>注册客户端（返回是否成功，失败=已存在）</summary>
    public bool TryAdd(string clientId, IMcpClient client)
    {
        var entry = new McpClientEntry
        {
            ClientId = clientId,
            Client = client,
            RegisteredAt = _clock.GetUtcNow()
        };
        return _clients.TryAdd(clientId, entry);
    }

    /// <summary>获取客户端（未找到返回 null）</summary>
    public IMcpClient? GetClient(string clientId)
        => _clients.GetValueOrDefault(clientId)?.Client;

    /// <summary>获取所有客户端（clientId → IMcpClient 快照）</summary>
    public IReadOnlyDictionary<string, IMcpClient> GetAll()
        => _clients.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.Client);

    /// <summary>获取所有客户端条目（用于 DisposeAsync 逐个释放）</summary>
    public IEnumerable<McpClientEntry> GetAllEntries() => _clients.Values;

    /// <summary>注销客户端（DisposeAsync 客户端 + 移除记录），返回是否找到</summary>
    public async Task<bool> TryRemoveAsync(string clientId)
    {
        if (_clients.TryGetValue(clientId, out var entry))
        {
            await entry.Client.DisposeAsync().ConfigureAwait(false);
            _clients.TryRemove(clientId, out _);
            return true;
        }
        return false;
    }

    /// <summary>清空所有客户端（逐个 DisposeAsync 后清空字典）</summary>
    public async Task ClearAllAsync()
    {
        await Task.WhenAll(_clients.Values
            .Select(entry => entry.Client.DisposeAsync().AsTask())).ConfigureAwait(false);
        _clients.Clear();
    }
}
