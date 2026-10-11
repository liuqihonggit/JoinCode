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

    /// <summary>
    /// 打开文件并映射物理内存只读视图 — 工厂方法（ADR 0129）。
    /// 三段式资源（mmf→accessor→AcquirePointer）逐步获取，任一失败释放已分配资源。
    /// </summary>
    public static unsafe PhysicalMemoryMappedRead Open(string path) {
        var length = new FileInfo(path).Length;
        var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        try {
            var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            try {
                byte* ptr = null;
                accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                return new PhysicalMemoryMappedRead(mmf, accessor, length, ptr);
            }
            catch {
                accessor.Dispose();
                throw;
            }
        }
        catch {
            mmf.Dispose();
            throw;
        }
    }

    /// <summary>私有构造函数 — 仅字段赋值（ADR 0129）。</summary>
    private PhysicalMemoryMappedRead(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, long length, byte* ptr) {
        _mmf = mmf;
        _accessor = accessor;
        _length = length;
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
