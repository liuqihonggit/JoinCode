namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 全局唯一对象标识 — 值类型（readonly struct），跨域传递零分配
/// SequenceId: 按 ObjectType 独立原子自增 long，同类型连续递增（区间压缩前提），进程内快速索引
/// UniqueId: GUID 方式，跨进程/持久化场景唯一标识，格式 "{前缀}-{GUID前8位}"
/// DisplayName: 可描述名称，人类可读，日志和UI展示用
/// Empty: 未分配标记，等同 default(ObjectId)，Type=None, SequenceId=0
/// 全局唯一性由 (Type, SequenceId) 联合保证 — ADR 0117
/// </summary>
public readonly struct ObjectId : IEquatable<ObjectId>, IComparable<ObjectId> {
    private static readonly long[] _slots = new long[32];

    /// <summary>未分配标记 — Type=None, SequenceId=0, UniqueId="", DisplayName=""</summary>
    public static readonly ObjectId Empty;

    /// <summary>获取对象类型。</summary>
    public ObjectType Type { get; }
    /// <summary>获取序列标识。</summary>
    public long SequenceId { get; }
    /// <summary>获取唯一标识。</summary>
    public string UniqueId { get; }
    /// <summary>获取显示名称。</summary>
    public string DisplayName { get; }

    /// <summary>是否未分配 — Type == None 且 SequenceId == 0</summary>
    public bool IsEmpty => Type == ObjectType.None && SequenceId == 0;

    /// <summary>
    /// 新建 ObjectId — 按 ObjectType 独立原子自增 SequenceId + 生成 GUID UniqueId
    /// </summary>
    public ObjectId(ObjectType type, string? displayName = null) {
        Type = type;
        SequenceId = Interlocked.Increment(ref _slots[(int)type]);
        UniqueId = GenerateUniqueId(type);
        DisplayName = displayName ?? UniqueId;
    }

    /// <summary>
    /// 反持久化 — 保留 UniqueId，重新分配 SequenceId
    /// </summary>
    public ObjectId(ObjectType type, string uniqueId, string? displayName = null) {
        ArgumentNullException.ThrowIfNull(uniqueId);
        Type = type;
        SequenceId = Interlocked.Increment(ref _slots[(int)type]);
        UniqueId = uniqueId;
        DisplayName = displayName ?? uniqueId;
    }

    /// <summary>
    /// 查找键构造 — 不分配 SequenceId/字符串，仅用于从 (ObjectType, SequenceId) 反查字典。
    /// <para>ObjectId.Equals/GetHashCode 只看 Type+SequenceId，此键与原键 hash/equals 一致 — ADR 0117</para>
    /// </summary>
    internal ObjectId(ObjectType type, long sequenceId) {
        Type = type;
        SequenceId = sequenceId;
        UniqueId = string.Empty;
        DisplayName = string.Empty;
    }

    public override string ToString() => IsEmpty ? "None:0" : $"{Type}:{SequenceId}";

    public override int GetHashCode() => HashCode.Combine(Type, SequenceId);

    public override bool Equals(object? obj) => obj is ObjectId other && Equals(other);

    /// <summary>判断是否等于另一个 ObjectId。</summary>
    /// <param name="other">另一个 ObjectId。</param>
    public bool Equals(ObjectId other) => Type == other.Type && SequenceId == other.SequenceId;

    /// <summary>与另一个 ObjectId 比较。</summary>
    /// <param name="other">另一个 ObjectId。</param>
    public int CompareTo(ObjectId other) {
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
    public static ObjectId Parse(string s) {
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

    /// <summary>尝试从字符串解析 ObjectId。</summary>
    /// <param name="s">输入字符串。</param>
    /// <param name="result">解析结果。</param>
    public static bool TryParse(string s, out ObjectId result) {
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
    /// 重置全局序列号（测试用）— 按 ObjectType 独立计数后重置所有槽
    /// </summary>
    internal static void ResetSequence() {
        for (var i = 0; i < _slots.Length; i++)
            Interlocked.Exchange(ref _slots[i], 0);
    }

    private static string GenerateUniqueId(ObjectType type) {
        var prefix = type.ToValue();
        var guid = Guid.NewGuid().ToString("N")[..8];
        return $"{prefix}-{guid}";
    }
}