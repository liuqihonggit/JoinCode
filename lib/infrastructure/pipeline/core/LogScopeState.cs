namespace Infrastructure.Pipeline;

/// <summary>
/// 日志 Scope 状态 — 传递给 ILogger.BeginScope() 的结构化字典
/// ObjectId.Empty 时 ObjectId/ObjectType 字段输出 null
/// 实现 IReadOnlyList 以兼容所有 ILogger Provider
/// </summary>
public sealed class LogScopeState : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly KeyValuePair<string, object?> _traceId;
    private readonly KeyValuePair<string, object?> _spanId;
    private readonly KeyValuePair<string, object?> _objectId;
    private readonly KeyValuePair<string, object?> _objectType;

    /// <summary>
    /// 构造日志 Scope 状态
    /// </summary>
    /// <param name="traceId">追踪 ID</param>
    /// <param name="spanId">Span ID</param>
    /// <param name="objectId">对象 ID，Empty 时 ObjectId/ObjectType 输出 null</param>
    public LogScopeState(string? traceId, string? spanId, ObjectId objectId)
    {
        _traceId = new("TraceId", traceId);
        _spanId = new("SpanId", spanId);
        _objectId = new("ObjectId", objectId.IsEmpty ? null : objectId.ToString());
        _objectType = new("ObjectType", objectId.IsEmpty ? null : objectId.Type.ToValue());
    }

    /// <summary>获取状态键值对数量</summary>
    public int Count => 4;

    /// <summary>
    /// 按索引获取状态键值对
    /// </summary>
    /// <param name="index">索引（0=TraceId, 1=SpanId, 2=ObjectId, 3=ObjectType）</param>
    /// <returns>对应索引的键值对</returns>
    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => _traceId, 1 => _spanId, 2 => _objectId, 3 => _objectType,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    /// <summary>
    /// 获取状态键值对枚举器
    /// </summary>
    /// <returns>状态键值对枚举器</returns>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return _traceId;
        yield return _spanId;
        yield return _objectId;
        yield return _objectType;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// 控制台日志输出格式: [TraceId=xxx SpanId=yyy ObjectId=Agent:1]
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder(64);
        sb.Append('[');
        if (_traceId.Value is not null) { sb.Append("TraceId="); sb.Append(_traceId.Value); sb.Append(' '); }
        if (_spanId.Value is not null) { sb.Append("SpanId="); sb.Append(_spanId.Value); sb.Append(' '); }
        if (_objectId.Value is not null) { sb.Append("ObjectId="); sb.Append(_objectId.Value); }
        if (sb.Length > 1 && sb[^1] == ' ') sb.Length--;
        sb.Append(']');
        return sb.ToString();
    }
}
