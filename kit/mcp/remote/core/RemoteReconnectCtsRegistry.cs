namespace McpToolRegistry;

/// <summary>
/// 远程重连令牌注册表 — 管理每个客户端的重连 CancellationTokenSource
/// 持有以 clientId 为 key 的不可变字典，无锁 CAS 更新
/// </summary>
internal sealed class RemoteReconnectCtsRegistry {
    private ImmutableDictionary<string, CancellationTokenSource> _ctsMap = ImmutableDictionary<string, CancellationTokenSource>.Empty;

    /// <summary>设置重连 CTS（取消并释放旧的，创建并注册新的）</summary>
    /// <returns>新创建的 CTS</returns>
    public CancellationTokenSource Setup(string clientId) {
        while (true) {
            var current = _ctsMap;
            var oldCts = current.GetValueOrDefault(clientId);
            var cts = new CancellationTokenSource();
            var updated = current.SetItem(clientId, cts);
            if (Interlocked.CompareExchange(ref _ctsMap, updated, current) == current) {
                oldCts?.Cancel();
                oldCts?.Dispose();
                return cts;
            }
            cts.Dispose();
        }
    }

    /// <summary>清理重连 CTS（仅当字典中的 CTS 与传入的一致时才移除，然后释放）</summary>
    public void Cleanup(string clientId, CancellationTokenSource cts) {
        while (true) {
            var current = _ctsMap;
            if (!current.TryGetValue(clientId, out var currentCts) || currentCts != cts) {
                cts.Dispose();
                return;
            }
            var updated = current.Remove(clientId);
            if (Interlocked.CompareExchange(ref _ctsMap, updated, current) == current) {
                cts.Dispose();
                return;
            }
        }
    }

    /// <summary>获取客户端的重连 CTS（未找到返回 null）</summary>
    public CancellationTokenSource? TryGet(string clientId)
        => _ctsMap.GetValueOrDefault(clientId);

    /// <summary>取消并移除指定客户端的重连 CTS（注销时调用）</summary>
    public void CancelAndRemove(string clientId) {
        while (true) {
            var current = _ctsMap;
            if (!current.TryGetValue(clientId, out var cts)) return;
            var updated = current.Remove(clientId);
            if (Interlocked.CompareExchange(ref _ctsMap, updated, current) == current) {
                cts.Cancel();
                cts.Dispose();
                return;
            }
        }
    }

    /// <summary>取消所有重连 CTS（释放时调用，不移除，后续 Clear 清空）</summary>
    public void CancelAll() {
        foreach (var cts in _ctsMap.Values) {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>清空所有 CTS 记录</summary>
    public void Clear()
        => Interlocked.Exchange(ref _ctsMap, ImmutableDictionary<string, CancellationTokenSource>.Empty);
}
