namespace McpToolRegistry;

/// <summary>
/// 远程客户端注册表 — 管理客户端的注册、注销、查询、清空
/// 持有以 clientId 为 key 的不可变字典，无锁 CAS 更新
/// </summary>
internal sealed class RemoteClientRegistry {
    private ImmutableDictionary<string, McpClientEntry> _clients = ImmutableDictionary<string, McpClientEntry>.Empty;
    private readonly IClockService _clock;

    /// <summary>初始化 <see cref="RemoteClientRegistry"/> 实例</summary>
    /// <param name="clock">时钟服务（用于记录注册时间）</param>
    public RemoteClientRegistry(IClockService clock) {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>当前已注册客户端数量</summary>
    public int Count => _clients.Count;

    /// <summary>注册客户端（返回是否成功，失败=已存在）</summary>
    public bool TryAdd(string clientId, IMcpClient client) {
        var entry = new McpClientEntry {
            ClientId = clientId,
            Client = client,
            RegisteredAt = _clock.GetUtcNow()
        };
        while (true) {
            var current = _clients;
            if (current.ContainsKey(clientId)) return false;
            var updated = current.Add(clientId, entry);
            if (Interlocked.CompareExchange(ref _clients, updated, current) == current) return true;
        }
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
    public async Task<bool> TryRemoveAsync(string clientId) {
        while (true) {
            var current = _clients;
            if (!current.TryGetValue(clientId, out var entry)) return false;
            var updated = current.Remove(clientId);
            if (Interlocked.CompareExchange(ref _clients, updated, current) == current) {
                await entry.Client.DisposeAsync().ConfigureAwait(false);
                return true;
            }
        }
    }

    /// <summary>清空所有客户端（逐个 DisposeAsync 后原子替换为空字典）</summary>
    public async Task ClearAllAsync() {
        var snapshot = Interlocked.Exchange(ref _clients, ImmutableDictionary<string, McpClientEntry>.Empty);
        await Task.WhenAll(snapshot.Values
            .Select(entry => entry.Client.DisposeAsync().AsTask())).ConfigureAwait(false);
    }
}
