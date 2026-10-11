namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 连续区间压缩集合 — 排序、不相交、非相邻的 long 区间集合。
/// <para>连续段合并为 [start~end] 只存 2 个 long（16 bytes），中间不逐个写入。</para>
/// <para>同类型 SequenceId 严格连续递增时，N 个元素压成单区间 [1~N]（16 bytes）。</para>
/// <para>不可变结构，每次 Add/Remove 返回新实例，配合 ImmutableInterlocked.Update 无锁 CAS。</para>
/// </summary>
public readonly struct LongRangeSet : IEquatable<LongRangeSet> {
    private readonly ImmutableArray<(long Start, long End)> _ranges;

    /// <summary>空集合。</summary>
    public static readonly LongRangeSet Empty = new(ImmutableArray<(long, long)>.Empty);

    private LongRangeSet(ImmutableArray<(long, long)> ranges) => _ranges = ranges;

    /// <summary>区间段数（压缩后的区间个数，非元素总数）。</summary>
    public int RangeCount => _ranges.Length;

    /// <summary>元素总数（所有区间长度之和）。</summary>
    public long Count {
        get {
            var total = 0L;
            for (var i = 0; i < _ranges.Length; i++) {
                var (start, end) = _ranges[i];
                total += end - start + 1;
            }
            return total;
        }
    }

    /// <summary>是否为空。</summary>
    public bool IsEmpty => _ranges.Length == 0;

    /// <summary>
    /// 添加元素 — 返回新集合。自动合并相邻/重叠区间。
    /// </summary>
    /// <param name="value">要添加的 long 值。</param>
    /// <returns>包含 value 的新集合（若已存在则返回 this）。</returns>
    public LongRangeSet Add(long value) {
        if (_ranges.Length == 0)
            return new(ImmutableArray.Create((value, value)));

        var idx = LowerBound(value);
        var canMergeLeft = idx > 0 && _ranges[idx - 1].End >= value - 1;
        var canMergeRight = idx < _ranges.Length && _ranges[idx].Start <= value + 1;

        if (canMergeLeft && canMergeRight) {
            var (lStart, _) = _ranges[idx - 1];
            var (_, rEnd) = _ranges[idx];
            return new(ReplaceTwoWithOne(idx - 1, (lStart, rEnd)));
        }
        if (canMergeLeft) {
            var (lStart, lEnd) = _ranges[idx - 1];
            return new(ReplaceAt(idx - 1, (lStart, Math.Max(lEnd, value))));
        }
        if (canMergeRight) {
            var (rStart, rEnd) = _ranges[idx];
            return new(ReplaceAt(idx, (Math.Min(rStart, value), rEnd)));
        }
        return new(InsertAt(idx, (value, value)));
    }

    /// <summary>
    /// 移除元素 — 返回新集合。可能拆分区间。
    /// </summary>
    /// <param name="value">要移除的 long 值。</param>
    /// <returns>不含 value 的新集合（若不存在则返回 this）。</returns>
    public LongRangeSet Remove(long value) {
        if (_ranges.Length == 0) return this;

        var idx = LowerBound(value);
        if (idx > 0) idx--;
        if (idx >= _ranges.Length) return this;

        var (start, end) = _ranges[idx];
        if (value < start || value > end) return this;

        if (start == end)
            return new(RemoveAt(idx));
        if (value == start)
            return new(ReplaceAt(idx, (start + 1, end)));
        if (value == end)
            return new(ReplaceAt(idx, (start, end - 1)));
        return new(SplitAt(idx, (start, value - 1), (value + 1, end)));
    }

    /// <summary>
    /// 判断是否包含指定值 — 二分查找 O(log n)。
    /// </summary>
    /// <param name="value">要查找的 long 值。</param>
    /// <returns>包含返回 true。</returns>
    public bool Contains(long value) {
        if (_ranges.Length == 0) return false;
        var idx = LowerBound(value);
        if (idx > 0) idx--;
        if (idx >= _ranges.Length) return false;
        var (start, end) = _ranges[idx];
        return value >= start && value <= end;
    }

    /// <summary>
    /// 枚举所有元素 — 按区间逐个 yield。
    /// </summary>
    /// <returns>所有 long 值的升序枚举。</returns>
    public IEnumerable<long> Enumerate() {
        for (var i = 0; i < _ranges.Length; i++) {
            var (start, end) = _ranges[i];
            for (var v = start; v <= end; v++)
                yield return v;
        }
    }

    /// <summary>判断是否等于另一个 LongRangeSet。</summary>
    /// <param name="other">另一个集合。</param>
    public bool Equals(LongRangeSet other) {
        if (_ranges.Length != other._ranges.Length) return false;
        for (var i = 0; i < _ranges.Length; i++) {
            if (_ranges[i] != other._ranges[i]) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is LongRangeSet other && Equals(other);
    public override int GetHashCode() {
        var h = new HashCode();
        for (var i = 0; i < _ranges.Length; i++)
            h.Add(_ranges[i]);
        return h.ToHashCode();
    }
    public static bool operator ==(LongRangeSet left, LongRangeSet right) => left.Equals(right);
    public static bool operator !=(LongRangeSet left, LongRangeSet right) => !left.Equals(right);

    /// <summary>二分查找第一个 Start > value 的区间索引。</summary>
    private int LowerBound(long value) {
        var lo = 0;
        var hi = _ranges.Length;
        while (lo < hi) {
            var mid = lo + ((hi - lo) >> 1);
            if (_ranges[mid].Start <= value) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private ImmutableArray<(long, long)> ReplaceAt(int idx, (long, long) range) {
        var builder = _ranges.ToBuilder();
        builder[idx] = range;
        return builder.ToImmutable();
    }

    private ImmutableArray<(long, long)> InsertAt(int idx, (long, long) range) {
        var builder = _ranges.ToBuilder();
        builder.Insert(idx, range);
        return builder.ToImmutable();
    }

    private ImmutableArray<(long, long)> RemoveAt(int idx) {
        var builder = _ranges.ToBuilder();
        builder.RemoveAt(idx);
        return builder.ToImmutable();
    }

    private ImmutableArray<(long, long)> ReplaceTwoWithOne(int idx, (long, long) range) {
        var builder = _ranges.ToBuilder();
        builder.RemoveAt(idx + 1);
        builder[idx] = range;
        return builder.ToImmutable();
    }

    private ImmutableArray<(long, long)> SplitAt(int idx, (long, long) left, (long, long) right) {
        var builder = _ranges.ToBuilder();
        builder[idx] = left;
        builder.Insert(idx + 1, right);
        return builder.ToImmutable();
    }
}
