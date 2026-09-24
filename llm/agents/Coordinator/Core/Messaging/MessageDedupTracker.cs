namespace Core.Agents.Coordinator;

/// <summary>
/// 消息去重跟踪器 — 按 AgentId 分桶记录已投递的 MessageId，
/// 同一 Agent 的重复消息（相同 MessageId）被识别为重复。
/// <para>无锁不可变嵌套: ImmutableDictionary&lt;agentId, ImmutableHashSet&lt;messageId&gt;&gt; + ImmutableInterlocked.Update 原子更新</para>
/// </summary>
internal sealed class MessageDedupTracker {
    private ImmutableDictionary<string, ImmutableHashSet<string>> _delivered = ImmutableDictionary<string, ImmutableHashSet<string>>.Empty;

    /// <summary>
    /// 检查消息是否已投递给指定 Agent；若未投递则标记为已投递。
    /// </summary>
    /// <param name="agentId">目标 Agent 标识。</param>
    /// <param name="messageId">消息标识。</param>
    /// <returns>true=重复（已投递过）；false=首次投递（已标记）。</returns>
    public bool IsDuplicate(string agentId, string messageId) {
        var box = new StrongBox<bool>();
        ImmutableInterlocked.Update(ref _delivered, static (dict, arg) => {
            var set = dict.GetValueOrDefault(arg.agentId) ?? ImmutableHashSet<string>.Empty;
            if (set.Contains(arg.messageId)) {
                arg.box.Value = false;
                return dict;
            }
            arg.box.Value = true;
            return dict.SetItem(arg.agentId, set.Add(arg.messageId));
        }, (agentId, messageId, box));
        return !box.Value;
    }

    /// <summary>
    /// 清除指定 Agent 的所有去重记录 — 注销 Agent 时调用。
    /// </summary>
    public void Clear(string agentId)
        => ImmutableInterlocked.Update(ref _delivered, static (dict, id) => dict.Remove(id), agentId);
}
