namespace IO.FileSystem;

/// <summary>
/// 物理内存映射只读视图 — MemoryMappedFile + unsafe pointer 零拷贝。
/// <para>构造时 AcquirePointer，Dispose 时 ReleasePointer。AsSpan 返回的 span 在 Dispose 前有效。</para>
/// </summary>
internal sealed unsafe class PhysicalMemoryMappedRead : IMemoryMappedRead {
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private readonly long _length;
    private readonly byte* _ptr;
    private bool _disposed;

    /// <summary>构造物理内存映射只读视图 — 打开文件并映射。</summary>
    public PhysicalMemoryMappedRead(string path) {
        _length = new FileInfo(path).Length;
        _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _ptr = ptr;
    }

    /// <inheritdoc />
    public long Length => _length;

    /// <inheritdoc />
    public ReadOnlySpan<byte> AsSpan() {
        return new ReadOnlySpan<byte>(_ptr, (int)_length);
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
        _accessor.Dispose();
        _mmf.Dispose();
    }
}
