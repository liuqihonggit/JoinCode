namespace Infrastructure.Persistence;

/// <summary>
/// 内存键值存储 — IKvStore 的纯内存实现,用于测试和临时数据。
/// <para>线程安全(ConcurrentDictionary + SortedDictionary for Scan)。</para>
/// <para>0 磁盘 IO,数据不持久化。</para>
/// </summary>
public sealed class InMemoryKvStore : IKvStore {
    private readonly ConcurrentDictionary<byte[], byte[]> _data = new(ByteArrayComparer.Instance);
    private volatile bool _disposed;

    /// <inheritdoc />
    public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _data[key] = value;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new(_data.TryGetValue(key, out var value) ? value : null);
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(byte[] key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        _data.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? from = null,
        byte[]? to = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var sorted = new SortedDictionary<byte[], byte[]>(ByteArrayComparer.Instance);
        foreach (var kvp in _data) {
            ct.ThrowIfCancellationRequested();
            if (from is not null && ByteArrayComparer.Instance.Compare(kvp.Key, from) < 0) continue;
            if (to is not null && ByteArrayComparer.Instance.Compare(kvp.Key, to) > 0) continue;
            sorted[kvp.Key] = kvp.Value;
        }
        foreach (var kvp in sorted) {
            ct.ThrowIfCancellationRequested();
            yield return (kvp.Key, kvp.Value);
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() {
        _disposed = true;
        _data.Clear();
        return ValueTask.CompletedTask;
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>, IEqualityComparer<byte[]> {
        public static readonly ByteArrayComparer Instance = new();
        /// <summary>比较两个 byte[] 的字典序。</summary>
        public int Compare(byte[]? x, byte[]? y) {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var len = Math.Min(x.Length, y.Length);
            for (var i = 0; i < len; i++) {
                var c = x[i].CompareTo(y[i]);
                if (c != 0) return c;
            }
            return x.Length.CompareTo(y.Length);
        }
        /// <summary>判断两个 byte[] 内容是否相等。</summary>
        public bool Equals(byte[]? x, byte[]? y) {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return x.AsSpan().SequenceEqual(y.AsSpan());
        }
        /// <summary>计算 byte[] 的哈希码。</summary>
        public int GetHashCode(byte[] obj) {
            var hash = new HashCode();
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
}
