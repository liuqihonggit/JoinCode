namespace PithosDB.Core.Core;

/// <summary>
/// Byte-lexicographic comparer for <see cref="T:byte[]"/> keys. Implements both
/// <see cref="IComparer{T}"/> (for sorted collections) and
/// <see cref="IEqualityComparer{T}"/> (for hash-based collections such as
/// <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> and
/// <see cref="System.Collections.Generic.HashSet{T}"/>).
/// </summary>
public sealed class ByteArrayComparer : IComparer<byte[]>, IEqualityComparer<byte[]>
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly ByteArrayComparer Instance = new();

    /// <inheritdoc/>
    public int Compare(byte[]? x, byte[]? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return x.AsSpan().SequenceCompareTo(y.AsSpan());
    }

    /// <inheritdoc/>
    public bool Equals(byte[]? x, byte[]? y) => Compare(x, y) == 0;

    /// <inheritdoc/>
    public int GetHashCode(byte[] obj)
    {
        var h = new HashCode();
        h.AddBytes(obj);
        return h.ToHashCode();
    }
}
