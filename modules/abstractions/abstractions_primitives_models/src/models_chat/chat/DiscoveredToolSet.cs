namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>已发现工具集合 — 基于 ConcurrentDictionary 提供线程安全的工具发现与查询</summary>
public sealed class DiscoveredToolSet {
    private readonly ConcurrentDictionary<string, byte> _discoveredNames = new(StringComparer.Ordinal);

    /// <summary>获取已发现工具名称集合。</summary>
    public Task<IReadOnlySet<string>> GetNamesAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(_discoveredNames.Keys, StringComparer.Ordinal));
    }

    /// <summary>获取已发现工具数量。</summary>
    public Task<int> GetCountAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_discoveredNames.Count);
    }

    /// <summary>判断指定工具是否已发现。</summary>
    public Task<bool> IsDiscoveredAsync(string toolName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_discoveredNames.ContainsKey(toolName));
    }

    /// <summary>发现指定工具。</summary>
    public Task<bool> DiscoverAsync(string toolName, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrEmpty(toolName);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_discoveredNames.TryAdd(toolName, 0));
    }

    /// <summary>批量发现工具。</summary>
    public Task<int> DiscoverRangeAsync(IEnumerable<string> toolNames, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(toolNames);
        ct.ThrowIfCancellationRequested();
        var added = 0;
        foreach (var name in toolNames) {
            if (_discoveredNames.TryAdd(name, 0))
                added++;
        }
        return Task.FromResult(added);
    }

    /// <summary>遗忘指定工具。</summary>
    public Task<bool> ForgetAsync(string toolName, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_discoveredNames.TryRemove(toolName, out _));
    }

    /// <summary>清空已发现工具集合。</summary>
    public Task ClearAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        _discoveredNames.Clear();
        return Task.CompletedTask;
    }

    /// <summary>创建已发现工具名称快照。</summary>
    public Task<string[]> SnapshotAsync(CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_discoveredNames.Keys.Order().ToArray());
    }

    /// <summary>从快照恢复已发现工具集合。</summary>
    public Task RestoreFromSnapshotAsync(string[] names, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(names);
        ct.ThrowIfCancellationRequested();
        _discoveredNames.Clear();
        foreach (var name in names) {
            _discoveredNames.TryAdd(name, 0);
        }
        return Task.CompletedTask;
    }
}
