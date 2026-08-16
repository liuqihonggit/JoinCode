namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 全局唯一对象标识 — 值类型（readonly struct），跨域传递零分配
/// SequenceId: 原子自增 long，进程内快速索引，插件用此获取内部元素
/// UniqueId: GUID 方式，跨进程/持久化场景唯一标识，格式 "{前缀}-{GUID前8位}"
/// DisplayName: 可描述名称，人类可读，日志和UI展示用
/// Empty: 未分配标记，等同 default(ObjectId)，Type=None, SequenceId=0
/// </summary>
public readonly struct ObjectId : IEquatable<ObjectId>, IComparable<ObjectId>
{
    private static long _globalSequence;

    /// <summary>未分配标记 — Type=None, SequenceId=0, UniqueId="", DisplayName=""</summary>
    public static readonly ObjectId Empty;

    public ObjectType Type { get; }
    public long SequenceId { get; }
    public string UniqueId { get; }
    public string DisplayName { get; }

    /// <summary>是否未分配 — Type == None 且 SequenceId == 0</summary>
    public bool IsEmpty => Type == ObjectType.None && SequenceId == 0;

    /// <summary>
    /// 新建 ObjectId — 自动分配原子自增 SequenceId + 生成 GUID UniqueId
    /// </summary>
    public ObjectId(ObjectType type, string? displayName = null)
    {
        Type = type;
        SequenceId = Interlocked.Increment(ref _globalSequence);
        UniqueId = GenerateUniqueId(type);
        DisplayName = displayName ?? UniqueId;
    }

    /// <summary>
    /// 反持久化 — 保留 UniqueId，重新分配 SequenceId
    /// </summary>
    public ObjectId(ObjectType type, string uniqueId, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(uniqueId);
        Type = type;
        SequenceId = Interlocked.Increment(ref _globalSequence);
        UniqueId = uniqueId;
        DisplayName = displayName ?? uniqueId;
    }

    public override string ToString() => IsEmpty ? "None:0" : $"{Type}:{SequenceId}";

    public override int GetHashCode() => HashCode.Combine(Type, SequenceId);

    public override bool Equals(object? obj) => obj is ObjectId other && Equals(other);

    public bool Equals(ObjectId other) => Type == other.Type && SequenceId == other.SequenceId;

    public int CompareTo(ObjectId other)
    {
        var typeCompare = Type.CompareTo(other.Type);
        return typeCompare != 0 ? typeCompare : SequenceId.CompareTo(other.SequenceId);
    }

    public static bool operator ==(ObjectId left, ObjectId right) => left.Equals(right);
    public static bool operator !=(ObjectId left, ObjectId right) => !left.Equals(right);
    public static bool operator <(ObjectId left, ObjectId right) => left.CompareTo(right) < 0;
    public static bool operator >(ObjectId left, ObjectId right) => left.CompareTo(right) > 0;
    public static bool operator <=(ObjectId left, ObjectId right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ObjectId left, ObjectId right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// 从持久化字符串解析 — 格式: "Agent:1:agent-abc123" 或 "Agent:1"
    /// </summary>
    public static ObjectId Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var segments = s.Split(':');

        if (segments.Length < 2)
            throw new FormatException($"ObjectId 格式错误，至少需要 'Type:SequenceId': {s}");

        if (!Enum.TryParse<ObjectType>(segments[0], out var type))
            throw new FormatException($"未知的 ObjectType: {segments[0]}");

        if (!long.TryParse(segments[1], out var sequenceId))
            throw new FormatException($"SequenceId 不是有效数字: {segments[1]}");

        var uniqueId = segments.Length >= 3 ? segments[2] : GenerateUniqueId(type);

        return new ObjectId(type, uniqueId);
    }

    public static bool TryParse(string s, out ObjectId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;

        var segments = s.Split(':');
        if (segments.Length < 2) return false;

        if (!Enum.TryParse<ObjectType>(segments[0], out var type)) return false;
        if (!long.TryParse(segments[1], out _)) return false;

        var uniqueId = segments.Length >= 3 ? segments[2] : GenerateUniqueId(type);
        result = new ObjectId(type, uniqueId);
        return true;
    }

    /// <summary>
    /// 重置全局序列号（测试用）
    /// </summary>
    internal static void ResetSequence() => Interlocked.Exchange(ref _globalSequence, 0);

    private static string GenerateUniqueId(ObjectType type)
    {
        var prefix = type.ToValue();
        var guid = Guid.NewGuid().ToString("N")[..8];
        return $"{prefix}-{guid}";
    }
}
