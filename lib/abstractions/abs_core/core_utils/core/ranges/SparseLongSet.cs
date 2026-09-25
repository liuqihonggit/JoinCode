namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 稀疏 long 集合 — 排序存储 + delta varint 编码压缩序列化。
/// <para>内存中存排序 ImmutableArray&lt;long&gt;（8 bytes/元素，O(log n) 二分查找）。</para>
/// <para>EncodeDeltas() 产出 varint delta 编码 byte 流（小 delta 1 byte，大 delta 5 bytes），用于持久化/传输压缩。</para>
/// <para>不可变结构，每次 Add/Remove 返回新实例，配合 ImmutableInterlocked.Update 无锁 CAS。</para>
/// </summary>
public readonly struct SparseLongSet : IEquatable<SparseLongSet> {
    private readonly ImmutableArray<long> _values;

    /// <summary>空集合。</summary>
    public static readonly SparseLongSet Empty = new(ImmutableArray<long>.Empty);

    private SparseLongSet(ImmutableArray<long> values) => _values = values;

    /// <summary>元素个数。</summary>
    public int Count => _values.Length;

    /// <summary>是否为空。</summary>
    public bool IsEmpty => _values.Length == 0;

    /// <summary>
    /// 添加元素 — 返回新集合（去重，保持升序）。
    /// </summary>
    /// <param name="value">要添加的 long 值。</param>
    /// <returns>包含 value 的新集合（若已存在则返回 this）。</returns>
    public SparseLongSet Add(long value) {
        if (_values.Length == 0)
            return new(ImmutableArray.Create(value));

        var idx = BinarySearch(value);
        if (idx >= 0) return this;

        var insertAt = ~idx;
        var builder = _values.ToBuilder();
        builder.Insert(insertAt, value);
        return new(builder.ToImmutable());
    }

    /// <summary>
    /// 移除元素 — 返回新集合。
    /// </summary>
    /// <param name="value">要移除的 long 值。</param>
    /// <returns>不含 value 的新集合（若不存在则返回 this）。</returns>
    public SparseLongSet Remove(long value) {
        if (_values.Length == 0) return this;

        var idx = BinarySearch(value);
        if (idx < 0) return this;

        var builder = _values.ToBuilder();
        builder.RemoveAt(idx);
        return new(builder.ToImmutable());
    }

    /// <summary>
    /// 判断是否包含指定值 — 二分查找 O(log n)。
    /// </summary>
    /// <param name="value">要查找的 long 值。</param>
    /// <returns>包含返回 true。</returns>
    public bool Contains(long value) => BinarySearch(value) >= 0;

    /// <summary>
    /// 枚举所有元素 — 升序。
    /// </summary>
    /// <returns>所有 long 值的升序枚举。</returns>
    public IEnumerable<long> Enumerate() {
        for (var i = 0; i < _values.Length; i++)
            yield return _values[i];
    }

    /// <summary>
    /// 编码为 delta varint 字节流 — 第一个值 fixed 8 bytes + 后续 delta varint。
    /// <para>小 delta（&lt;128）1 byte，大 delta 5 bytes。用于持久化/传输压缩。</para>
    /// </summary>
    /// <returns>压缩字节流。</returns>
    public byte[] EncodeDeltas() {
        if (_values.Length == 0) return [];

        var ms = new MemoryStream(8 + _values.Length * 2);
        var first = _values[0];
        ms.Write(BitConverter.GetBytes(first));

        for (var i = 1; i < _values.Length; i++) {
            var delta = _values[i] - _values[i - 1];
            WriteVarint(ms, (ulong)delta);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// 从 delta varint 字节流解码构造集合。
    /// </summary>
    /// <param name="encoded">EncodeDeltas 产出的字节流。</param>
    /// <returns>解码后的集合。</returns>
    public static SparseLongSet Decode(ReadOnlySpan<byte> encoded) {
        if (encoded.Length == 0) return Empty;
        if (encoded.Length < 8)
            throw new FormatException("编码流过短，至少需要 8 字节存第一个值");

        var first = BitConverter.ToInt64(encoded[..8]);
        if (encoded.Length == 8)
            return new(ImmutableArray.Create(first));

        var values = new List<long> { first };
        var pos = 8;
        var prev = first;
        while (pos < encoded.Length) {
            var delta = (long)ReadVarint(encoded, ref pos);
            prev += delta;
            values.Add(prev);
        }
        return new(values.ToImmutableArray());
    }

    /// <summary>判断是否等于另一个 SparseLongSet。</summary>
    /// <param name="other">另一个集合。</param>
    public bool Equals(SparseLongSet other) {
        if (_values.Length != other._values.Length) return false;
        for (var i = 0; i < _values.Length; i++) {
            if (_values[i] != other._values[i]) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is SparseLongSet other && Equals(other);
    public override int GetHashCode() {
        var h = new HashCode();
        for (var i = 0; i < _values.Length; i++)
            h.Add(_values[i]);
        return h.ToHashCode();
    }
    public static bool operator ==(SparseLongSet left, SparseLongSet right) => left.Equals(right);
    public static bool operator !=(SparseLongSet left, SparseLongSet right) => !left.Equals(right);

    private int BinarySearch(long value) {
        var lo = 0;
        var hi = _values.Length - 1;
        while (lo <= hi) {
            var mid = lo + ((hi - lo) >> 1);
            var cmp = _values[mid].CompareTo(value);
            if (cmp == 0) return mid;
            if (cmp < 0) lo = mid + 1;
            else hi = mid - 1;
        }
        return ~lo;
    }

    private static void WriteVarint(Stream s, ulong value) {
        while (value >= 0x80) {
            s.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        s.WriteByte((byte)value);
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int pos) {
        var shift = 0;
        ulong result = 0;
        byte b;
        do {
            b = bytes[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }
}
