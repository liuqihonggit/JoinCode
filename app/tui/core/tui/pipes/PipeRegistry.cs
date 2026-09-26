namespace JoinCode.Tui.Pipes;

/// <summary>
/// 管道注册表 — 管理所有 Agent 的消息管道。
/// 线程安全：使用 ConcurrentDictionary 存储管道。
/// </summary>
public sealed class PipeRegistry {
    private ImmutableDictionary<string, IMessagePipe> _pipes = ImmutableDictionary<string, IMessagePipe>.Empty;

    /// <summary>已注册管道数量。</summary>
    public int Count => Volatile.Read(ref _pipes).Count;

    /// <summary>所有已注册管道（只读快照）。</summary>
    public IReadOnlyList<IMessagePipe> All => [.. Volatile.Read(ref _pipes).Values];

    /// <summary>主 Agent 管道（null 表示尚未注册）。</summary>
    public IMessagePipe? MainPipe => Volatile.Read(ref _pipes).Values.FirstOrDefault(p => p.IsMain);

    /// <summary>注册管道。已存在同 AgentId 则覆盖。</summary>
    public void Register(IMessagePipe pipe) {
        ImmutableInterlocked.Update(ref _pipes, d => d.SetItem(pipe.AgentId, pipe));
    }

    /// <summary>注销管道。</summary>
    public bool Unregister(string agentId) {
        var removed = false;
        ImmutableInterlocked.Update(ref _pipes, d => {
            if (d.ContainsKey(agentId)) {
                removed = true;
                return d.Remove(agentId);
            }
            return d;
        });
        return removed;
    }

    /// <summary>获取指定 Agent 的管道。不存在返回 null。</summary>
    public IMessagePipe? Get(string agentId) {
        return Volatile.Read(ref _pipes).TryGetValue(agentId, out var pipe) ? pipe : null;
    }

    /// <summary>是否包含指定 Agent 的管道。</summary>
    public bool Contains(string agentId) {
        return Volatile.Read(ref _pipes).ContainsKey(agentId);
    }

    /// <summary>清空所有管道。</summary>
    public void Clear() {
        Interlocked.Exchange(ref _pipes, ImmutableDictionary<string, IMessagePipe>.Empty);
    }
}