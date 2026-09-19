namespace Core.Agents.Coordinator.Core.Lifecycle;

/// <summary>
/// 子代理启动时间跟踪器 — 记录 agentId→启动时间,支持查询持续时长与移除
/// </summary>
internal sealed class AgentStartTimer {
    private readonly ConcurrentDictionary<string, DateTime> _startTimes = new();

    /// <summary>
    /// 记录子代理的启动时间
    /// </summary>
    internal void Record(string agentId, DateTime startTime) {
        _startTimes[agentId] = startTime;
    }

    /// <summary>
    /// 移除并返回持续时长(毫秒);未记录则返回 null
    /// </summary>
    internal long? TryRemoveDurationMs(string agentId, DateTime now) {
        return _startTimes.TryRemove(agentId, out var startTime)
            ? (long)(now - startTime).TotalMilliseconds
            : null;
    }

    /// <summary>
    /// 仅移除启动时间记录,不计算时长
    /// </summary>
    internal void Remove(string agentId) {
        _startTimes.TryRemove(agentId, out _);
    }

    /// <summary>
    /// 查询启动时间(不移除);未记录则返回 null
    /// </summary>
    internal DateTime? TryGet(string agentId) {
        return _startTimes.TryGetValue(agentId, out var startTime) ? startTime : null;
    }
}