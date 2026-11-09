namespace Core.Agents;

/// <summary>
/// 子代理活动历史 — 环形缓冲上限 200 条，无锁 CAS 更新，volatile 读取
/// </summary>
public sealed class AgentActivityHistory {
    private const int MaxEntries = 200;
    private readonly IClockService? _clock;
    private volatile ImmutableList<JoinCode.Abstractions.Interfaces.AgentActivityEntry> _entries = ImmutableList<JoinCode.Abstractions.Interfaces.AgentActivityEntry>.Empty;

    /// <summary>
    /// 构造活动历史
    /// </summary>
    /// <param name="clock">时钟服务（可选，用于测试时间控制）</param>
    public AgentActivityHistory(IClockService? clock = null) {
        _clock = clock;
    }

    /// <summary>当前活动条数</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// 追加一条活动 — 无锁 CAS 循环，超过上限时丢弃最旧条目
    /// </summary>
    public void Append(JoinCode.Abstractions.Interfaces.AgentActivityType type, string text, string? glyph = null, string? toolCallId = null, string? toolName = null, bool isError = false) {
        var entry = new JoinCode.Abstractions.Interfaces.AgentActivityEntry {
            Timestamp = _clock?.GetUtcNow() ?? DateTime.UtcNow,
            Type = type,
            Text = text,
            Glyph = glyph,
            ToolCallId = toolCallId,
            ToolName = toolName,
            IsError = isError,
        };

        while (true) {
            var snapshot = _entries;
            var updated = snapshot.Add(entry);
            if (updated.Count > MaxEntries)
                updated = updated.RemoveRange(0, updated.Count - MaxEntries);
            if (Interlocked.CompareExchange(ref _entries, updated, snapshot) == snapshot) return;
        }
    }

    /// <summary>
    /// 获取所有活动条目快照（按时间顺序，无锁读取 volatile 引用）
    /// </summary>
    public IReadOnlyList<JoinCode.Abstractions.Interfaces.AgentActivityEntry> Snapshot() => _entries;

    /// <summary>
    /// 获取最近 N 条活动条目快照（按时间顺序，无锁读取）
    /// </summary>
    public IReadOnlyList<JoinCode.Abstractions.Interfaces.AgentActivityEntry> Recent(int count) {
        var snapshot = _entries;
        return snapshot.Count <= count ? snapshot : snapshot.GetRange(snapshot.Count - count, count);
    }

    /// <summary>
    /// 获取最后一条活动的文本（无活动返回 null，无锁读取）
    /// </summary>
    public string? LastActivityText() {
        var snapshot = _entries;
        return snapshot.Count > 0 ? snapshot[^1].Text : null;
    }
}
