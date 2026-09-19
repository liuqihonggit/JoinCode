namespace Core.Bridge;

/// <summary>
/// 客户端注册表 — 管理 WebSocket 客户端连接
/// 从 BridgeServer 提取,降低大类字段数和客户端管理逻辑复杂度
/// </summary>
internal sealed class ClientRegistry {
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();

    /// <summary>添加客户端连接</summary>
    public void Add(string clientId, WebSocket webSocket) => _clients[clientId] = webSocket;

    /// <summary>移除客户端连接</summary>
    public bool TryRemove(string clientId) => _clients.TryRemove(clientId, out _);

    /// <summary>尝试获取客户端连接</summary>
    public bool TryGet(string clientId, [MaybeNullWhen(false)] out WebSocket webSocket)
        => _clients.TryGetValue(clientId, out webSocket);

    /// <summary>所有客户端 ID</summary>
    public IEnumerable<string> Keys => _clients.Keys;

    /// <summary>客户端数量</summary>
    public int Count => _clients.Count;

    /// <summary>所有 WebSocket 连接</summary>
    public IEnumerable<WebSocket> Values => _clients.Values;

    /// <summary>清空所有客户端</summary>
    public void Clear() => _clients.Clear();

    /// <summary>关闭所有客户端连接并清空注册表</summary>
    public async Task CloseAllAsync(WebSocketCloseStatus status, string reason, CancellationToken cancellationToken) {
        await Task.WhenAll(_clients.Values
            .Select(client => client.CloseAsync(status, reason, cancellationToken)
                .ContinueWith(_ => { }, cancellationToken))).ConfigureAwait(false);
        _clients.Clear();
    }
}

/// <summary>
/// 自定义路由注册表 — 管理 HTTP 路由处理器
/// 从 BridgeServer 提取,降低大类字段数
/// </summary>
internal sealed class RouteRegistry {
    private readonly ConcurrentDictionary<string, Func<HttpListenerContext, CancellationToken, Task>> _routes = new();

    /// <summary>注册路由处理器</summary>
    public void Register(string path, Func<HttpListenerContext, CancellationToken, Task> handler)
        => _routes[path] = handler;

    /// <summary>尝试获取路由处理器</summary>
    public bool TryGet(string path, [MaybeNullWhen(false)] out Func<HttpListenerContext, CancellationToken, Task> handler)
        => _routes.TryGetValue(path, out handler);
}