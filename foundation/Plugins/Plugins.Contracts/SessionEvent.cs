namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话格式版本闸门 — 对齐 DSH SESSION_FORMAT_VERSION
/// <para>当前 =3，日志首行 version 比当前新时拒绝加载</para>
/// </summary>
public static class SessionFormatVersion
{
    /// <summary>当前格式版本</summary>
    public const int Current = 3;
}

/// <summary>表面操作类型</summary>
public enum SurfaceOpKind
{
    /// <summary>追加</summary>
    Append,

    /// <summary>替换 [startSeq, endSeq] 范围</summary>
    Replace,
}

/// <summary>
/// 表面操作 — 对齐 DSH surfaceOp
/// <para>append：追加新事件</para>
/// <para>replace：替换 [startSeq, endSeq] 范围的旧事件</para>
/// <para>仅 system/message、user/message、assistant/message、tool/result 合法</para>
/// </summary>
public sealed record SurfaceOp
{
    /// <summary>操作类型</summary>
    public required SurfaceOpKind Kind { get; init; }

    /// <summary>replace 起始 seq（仅 Replace 有效）</summary>
    public int? StartSeq { get; init; }

    /// <summary>replace 结束 seq（仅 Replace 有效）</summary>
    public int? EndSeq { get; init; }

    /// <summary>构造 append 操作</summary>
    public static SurfaceOp Append() => new() { Kind = SurfaceOpKind.Append };

    /// <summary>构造 replace 操作</summary>
    public static SurfaceOp Replace(int startSeq, int endSeq) => new()
    {
        Kind = SurfaceOpKind.Replace,
        StartSeq = startSeq,
        EndSeq = endSeq,
    };
}

/// <summary>
/// 会话事件 — 对齐 DSH SessionEvent
/// <para>seq：单调递增持久化排序键</para>
/// <para>time：Unix epoch 毫秒</para>
/// <para>type：事件类型（如 user/message、tool/call）</para>
/// <para>data：事件数据负载</para>
/// <para>surfaceOp：表面操作（仅 surface 事件）</para>
/// <para>sourceEventSeqs：引用的源事件 seq（压缩替换→被遮蔽条目）</para>
/// <para>ignorable：未知类型可跳过；缺失=必选，拒绝重建</para>
/// </summary>
public sealed record SessionEvent
{
    /// <summary>单调递增持久化排序键</summary>
    public required int Seq { get; init; }

    /// <summary>Unix epoch 毫秒</summary>
    public required long Time { get; init; }

    /// <summary>事件类型</summary>
    public required string Type { get; init; }

    /// <summary>事件数据负载</summary>
    public object? Data { get; init; }

    /// <summary>表面操作（仅 surface 事件）</summary>
    public SurfaceOp? SurfaceOp { get; init; }

    /// <summary>引用的源事件 seq（压缩替换→被遮蔽条目）</summary>
    public int[]? SourceEventSeqs { get; init; }

    /// <summary>未知类型可跳过；缺失=必选</summary>
    public bool Ignorable { get; init; }
}

/// <summary>
/// 会话事件日志头 — 格式版本闸门
/// <para>对齐 DSH 日志首行 {"type":"session","version":3,...}</para>
/// </summary>
public sealed record SessionEventLogHeader
{
    /// <summary>日志类型标识</summary>
    public const string Type = "session";

    /// <summary>格式版本</summary>
    public required int Version { get; init; }

    /// <summary>会话 ID</summary>
    public required string Id { get; init; }

    /// <summary>创建时间（Unix epoch 毫秒）</summary>
    public required long CreatedAt { get; init; }
}

/// <summary>
/// 会话事件流 — append-only + seq 自增 + 格式闸门
/// <para>对齐 DSH 会话日志：遥测/投影/恢复消费同一事件流</para>
/// <para>线程安全：lock + Interlocked</para>
/// <para>时钟可注入：Func&lt;long&gt;? clock = null，测试可控</para>
/// </summary>
public sealed class SessionEventLog
{
    private int _seqCounter;
    private readonly List<SessionEvent> _events = new();
    private readonly object _lock = new();
    private readonly Func<long>? _clock;

    /// <param name="clock">时钟注入（Unix epoch 毫秒），null 用 UtcNow</param>
    public SessionEventLog(Func<long>? clock = null) => _clock = clock;

    /// <summary>
    /// 追加事件 — seq 自增递增
    /// </summary>
    public SessionEvent Append(
        string type,
        object? data = null,
        SurfaceOp? surfaceOp = null,
        int[]? sourceEventSeqs = null,
        bool ignorable = false)
    {
        var seq = Interlocked.Increment(ref _seqCounter);
        var time = _clock?.Invoke() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var evt = new SessionEvent
        {
            Seq = seq,
            Time = time,
            Type = type,
            Data = data,
            SurfaceOp = surfaceOp,
            SourceEventSeqs = sourceEventSeqs,
            Ignorable = ignorable,
        };
        lock (_lock)
        {
            _events.Add(evt);
        }
        return evt;
    }

    /// <summary>所有事件（快照拷贝）</summary>
    public IReadOnlyList<SessionEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return _events.ToArray();
            }
        }
    }

    /// <summary>事件数量</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _events.Count;
            }
        }
    }

    /// <summary>按 seq 查找</summary>
    public SessionEvent? Find(int seq)
    {
        lock (_lock)
        {
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i].Seq == seq) return _events[i];
            }
            return null;
        }
    }

    /// <summary>从某 seq 之后的事件（不含该 seq）</summary>
    public IReadOnlyList<SessionEvent> After(int seq)
    {
        lock (_lock)
        {
            var result = new List<SessionEvent>();
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i].Seq > seq) result.Add(_events[i]);
            }
            return result;
        }
    }

    /// <summary>到某 seq 为止的事件（含该 seq）</summary>
    public IReadOnlyList<SessionEvent> Until(int seq)
    {
        lock (_lock)
        {
            var result = new List<SessionEvent>();
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i].Seq <= seq) result.Add(_events[i]);
            }
            return result;
        }
    }
}

/// <summary>
/// 会话事件重建器 — 格式闸门 + 未知类型拒绝
/// <para>对齐 DSH：未知类型无 ignorable→拒绝；有 ignorable→跳过</para>
/// </summary>
public static class SessionEventRebuilder
{
    /// <summary>
    /// 校验日志头版本 — 比当前新拒绝加载
    /// </summary>
    public static void ValidateHeader(SessionEventLogHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.Version > SessionFormatVersion.Current)
        {
            throw new InvalidOperationException(
                $"[INF-SESSION-FORMAT] 日志版本 {header.Version} 比当前 {SessionFormatVersion.Current} 新，请升级");
        }
    }

    /// <summary>
    /// 重建事件流 — 拒绝未知非 ignorable
    /// <para>knownTypes=null 时不校验类型（全部接受）</para>
    /// </summary>
    public static IReadOnlyList<SessionEvent> Rebuild(
        IReadOnlyList<SessionEvent> events,
        IReadOnlySet<string>? knownTypes = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var result = new List<SessionEvent>(events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            var evt = events[i];
            if (knownTypes is not null && !knownTypes.Contains(evt.Type) && !evt.Ignorable)
            {
                throw new InvalidOperationException(
                    $"[INF-SESSION-UNKNOWN] 未知事件类型 {evt.Type} 且非 ignorable，拒绝重建");
            }
            result.Add(evt);
        }
        return result;
    }
}
