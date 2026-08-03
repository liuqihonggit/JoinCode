namespace Infrastructure.Pipeline;

/// <summary>
/// 日志 Scope 状态 — 传递给 ILogger.BeginScope() 的结构化字典
/// 所有字段均为 nullable，缺失时输出 null 而非抛异常
/// 实现 IReadOnlyList 以兼容所有 ILogger Provider
/// </summary>
public sealed class LogScopeState : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly KeyValuePair<string, object?> _traceId;
    private readonly KeyValuePair<string, object?> _spanId;
    private readonly KeyValuePair<string, object?> _objectId;
    private readonly KeyValuePair<string, object?> _objectType;

    public LogScopeState(string? traceId, string? spanId, ObjectId? objectId)
    {
        _traceId = new("TraceId", traceId);
        _spanId = new("SpanId", spanId);
        _objectId = new("ObjectId", objectId.HasValue ? objectId.Value.ToString() : null);
        _objectType = new("ObjectType", objectId.HasValue ? objectId.Value.Type.ToValue() : null);
    }

    public int Count => 4;

    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => _traceId, 1 => _spanId, 2 => _objectId, 3 => _objectType,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

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
