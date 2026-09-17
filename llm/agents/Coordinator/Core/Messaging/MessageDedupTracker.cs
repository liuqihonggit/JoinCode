namespace Core.Agents.Coordinator;

/// <summary>
/// 消息去重跟踪器 — 按 AgentId 分桶记录已投递的 MessageId，
/// 同一 Agent 的重复消息（相同 MessageId）被识别为重复。
/// </summary>
internal sealed class MessageDedupTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _delivered = new();

    /// <summary>
    /// 检查消息是否已投递给指定 Agent；若未投递则标记为已投递。
    /// </summary>
    /// <param name="agentId">目标 Agent 标识。</param>
    /// <param name="messageId">消息标识。</param>
    /// <returns>true=重复（已投递过）；false=首次投递（已标记）。</returns>
    public bool IsDuplicate(string agentId, string messageId)
    {
        var set = _delivered.GetOrAdd(agentId, _ => new ConcurrentDictionary<string, byte>());
        return !set.TryAdd(messageId, 0);
    }

    /// <summary>
    /// 清除指定 Agent 的所有去重记录 — 注销 Agent 时调用。
    /// </summary>
    public void Clear(string agentId)
        => _delivered.TryRemove(agentId, out _);
}
