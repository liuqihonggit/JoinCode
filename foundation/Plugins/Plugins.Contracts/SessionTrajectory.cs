namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话轨迹 — 从事件流重建完整 run，按 type 分组 inspect
/// <para>对齐 DSH Trajectory view：inspect records by source</para>
/// <para>遥测/投影/恢复消费同一事件流</para>
/// </summary>
public sealed class SessionTrajectory
{
    private readonly SessionEventLog _log;

    /// <param name="log">事件流</param>
    public SessionTrajectory(SessionEventLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>完整轨迹（所有事件按 seq 排序）</summary>
    public IReadOnlyList<SessionEvent> FullTrajectory => _log.Events;

    /// <summary>按事件类型分组（对齐 DSH inspect by source）</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SessionEvent>> GroupByType()
    {
        var events = _log.Events;
        var groups = new Dictionary<string, List<SessionEvent>>();
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (!groups.TryGetValue(e.Type, out var list))
            {
                list = new List<SessionEvent>();
                groups[e.Type] = list;
            }
            list.Add(e);
        }
        return groups.ToFrozenDictionary(
            k => k.Key,
            k => (IReadOnlyList<SessionEvent>)k.Value);
    }

    /// <summary>某类型的事件</summary>
    public IReadOnlyList<SessionEvent> OfType(string type)
    {
        var events = _log.Events;
        var result = new List<SessionEvent>();
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Type == type) result.Add(events[i]);
        }
        return result;
    }

    /// <summary>事件总数</summary>
    public int TotalCount => _log.Count;
}

/// <summary>
/// 会话回放/分叉操作 — 对齐 DSH resume/fork/replay
/// <para>基于 SessionEventLog 事件流，同一事件流重建完整 run</para>
/// </summary>
public static class SessionReplay
{
    /// <summary>回放到指定 seq（返回 Until(seq)，含该 seq）</summary>
    public static IReadOnlyList<SessionEvent> ReplayUntil(SessionEventLog log, int seq)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.Until(seq);
    }

    /// <summary>从某 seq 之后重放（返回 After(seq)，不含该 seq）</summary>
    public static IReadOnlyList<SessionEvent> ReplayAfter(SessionEventLog log, int seq)
    {
        ArgumentNullException.ThrowIfNull(log);
        return log.After(seq);
    }

    /// <summary>
    /// 从某 seq 分叉新会话 — 复制 Until(seq) 到新 log
    /// <para>对齐 DSH fork：从某 seq 分叉新会话，复制事件流前缀</para>
    /// <para>新 log 的 seq 重新从 1 开始（独立会话）</para>
    /// </summary>
    public static SessionEventLog Fork(SessionEventLog source, int atSeq, Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var forked = new SessionEventLog(clock);
        var events = source.Until(atSeq);
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            forked.Append(e.Type, e.Data, e.SurfaceOp, e.SourceEventSeqs, e.Ignorable);
        }
        return forked;
    }

    /// <summary>
    /// 从某 seq 分叉并继续 — 复制前缀 + 追加新事件
    /// <para>fork 后的 log 可继续 Append 新事件</para>
    /// </summary>
    public static SessionEventLog ForkAndContinue(SessionEventLog source, int atSeq, Func<long>? clock = null)
    {
        return Fork(source, atSeq, clock);
    }
}
