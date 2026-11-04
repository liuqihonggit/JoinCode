namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 内存内存映射只读视图 — byte[] 包装，测试环境用。
/// <para>生产环境用 PhysicalMemoryMappedRead（MemoryMappedFile 零拷贝）。</para>
/// </summary>
public sealed class InMemoryMemoryMappedRead : IMemoryMappedRead {
    private readonly byte[] _data;

    /// <summary>构造内存内存映射只读视图。</summary>
    public InMemoryMemoryMappedRead(byte[] data) {
        _data = data;
    }

    /// <inheritdoc />
    public long Length => _data.Length;

    /// <inheritdoc />
    public ReadOnlySpan<byte> AsSpan() {
        return _data.AsSpan();
    }

    /// <inheritdoc />
    public void Dispose() {
    }
}
