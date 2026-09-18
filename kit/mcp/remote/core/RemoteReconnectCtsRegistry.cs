namespace McpToolRegistry;

/// <summary>
/// 远程重连令牌注册表 — 管理每个客户端的重连 CancellationTokenSource
/// 持有以 clientId 为 key 的 CTS 字典，提供设置、清理、取消、清空操作
/// </summary>
internal sealed class RemoteReconnectCtsRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _ctsMap = new();

    /// <summary>设置重连 CTS（取消并释放旧的，创建并注册新的）</summary>
    /// <returns>新创建的 CTS</returns>
    public CancellationTokenSource Setup(string clientId)
    {
        if (_ctsMap.TryGetValue(clientId, out var oldCts))
            _ctsMap.TryRemove(clientId, out _);

        var cts = new CancellationTokenSource();
        _ctsMap[clientId] = cts;

        oldCts?.Cancel();
        oldCts?.Dispose();
        return cts;
    }

    /// <summary>清理重连 CTS（仅当字典中的 CTS 与传入的一致时才移除，然后释放）</summary>
    public void Cleanup(string clientId, CancellationTokenSource cts)
    {
        if (_ctsMap.TryGetValue(clientId, out var currentCts) && currentCts == cts)
            _ctsMap.TryRemove(clientId, out _);

        cts.Dispose();
    }

    /// <summary>获取客户端的重连 CTS（未找到返回 null）</summary>
    public CancellationTokenSource? TryGet(string clientId)
        => _ctsMap.GetValueOrDefault(clientId);

    /// <summary>取消并移除指定客户端的重连 CTS（注销时调用）</summary>
    public void CancelAndRemove(string clientId)
    {
        if (_ctsMap.TryGetValue(clientId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _ctsMap.TryRemove(clientId, out _);
        }
    }

    /// <summary>取消所有重连 CTS（释放时调用，不移除，后续 Clear 清空）</summary>
    public void CancelAll()
    {
        foreach (var cts in _ctsMap.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>清空所有 CTS 记录</summary>
    public void Clear() => _ctsMap.Clear();
}
