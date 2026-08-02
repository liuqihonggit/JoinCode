namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 全局唯一对象标识 — 值类型（readonly struct），跨域传递零分配
/// 格式: "{ObjectType}:{领域ID}"，如 "Agent:agent-abc123"
/// 跨域引用只用 ObjectId，不直接用 string Id
/// </summary>
public readonly struct ObjectId : IEquatable<ObjectId>, IComparable<ObjectId>
{
    public ObjectType Type { get; }
    public string Id { get; }

    public ObjectId(ObjectType type, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Type = type;
        Id = id;
    }

    public override string ToString() => $"{Type}:{Id}";

    public override int GetHashCode() => HashCode.Combine(Type, Id);

    public override bool Equals(object? obj) => obj is ObjectId other && Equals(other);

    public bool Equals(ObjectId other) => Type == other.Type && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public int CompareTo(ObjectId other)
    {
        var typeCompare = Type.CompareTo(other.Type);
        return typeCompare != 0 ? typeCompare : string.Compare(Id, other.Id, StringComparison.Ordinal);
    }

    public static bool operator ==(ObjectId left, ObjectId right) => left.Equals(right);
    public static bool operator !=(ObjectId left, ObjectId right) => !left.Equals(right);
    public static bool operator <(ObjectId left, ObjectId right) => left.CompareTo(right) < 0;
    public static bool operator >(ObjectId left, ObjectId right) => left.CompareTo(right) > 0;
    public static bool operator <=(ObjectId left, ObjectId right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ObjectId left, ObjectId right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// 从持久化字符串解析 — 格式: "Agent:agent-abc123"
    /// </summary>
    public static ObjectId Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var separator = s.IndexOf(':');
        if (separator < 0)
            throw new FormatException($"ObjectId 格式错误，缺少 ':' 分隔符: {s}");

        var typeStr = s[..separator];
        var id = s[(separator + 1)..];

        if (!Enum.TryParse<ObjectType>(typeStr, out var type))
            throw new FormatException($"未知的 ObjectType: {typeStr}");

        return new ObjectId(type, id);
    }

    public static bool TryParse(string s, out ObjectId result)
    {
        result = default;
        if (string.IsNullOrEmpty(s)) return false;

        var separator = s.IndexOf(':');
        if (separator < 0) return false;

        var typeStr = s[..separator];
        var id = s[(separator + 1)..];

        if (!Enum.TryParse<ObjectType>(typeStr, out var type)) return false;

        result = new ObjectId(type, id);
        return true;
    }
}
