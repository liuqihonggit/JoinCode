namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// mmap 零拷贝文件读取器 — 封装 <see cref="MemoryMappedFile"/> + <see cref="MemoryMappedViewAccessor"/>，
/// 用 <c>using var</c>（NET10 单行）释放，编译器展开为 try-finally 保证句柄回收。
/// <para>ADR: 0071 — P0 底层优化，供 PhysicalFileSystem / RgEngine / 文件编辑核心复用。</para>
/// <para>用法：</para>
/// <code>
/// using var reader = MappedFileReader.Open(path);
/// var content = reader.ReadToEnd();
/// // 或零拷贝字节访问：
/// var bytes = reader.AsSpan();
/// </code>
/// </summary>
public sealed class MappedFileReader : IDisposable
{
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly long _fileSize;
    private bool _disposed;

    private MappedFileReader(MemoryMappedFile? mmf, MemoryMappedViewAccessor? accessor, long fileSize)
    {
        _mmf = mmf;
        _accessor = accessor;
        _fileSize = fileSize;
    }

    /// <summary>
    /// 打开文件并创建 mmap 映射。文件映射到虚拟内存，按需分页，不立即分配物理内存。
    /// <para>工厂方法避免构造函数抛异常导致 mmf 泄漏：先创建 mmf，再创建 accessor，
    /// accessor 失败时 mmf.Dispose() 后重抛。</para>
    /// <para>注意：accessor.Capacity 是页对齐容量（如 4096），不是文件实际大小。
    /// 用 FileInfo.Length 获取真实大小，避免读到尾部填充的 \0。</para>
    /// <para>空文件不创建 mmap（BCL 限制），mmf/accessor 为 null，Length=0，ReadToEnd 返回空字符串。</para>
    /// </summary>
    /// <param name="path">文件绝对路径。</param>
    /// <returns>已映射的读取器，用 <c>using var</c> 释放。</returns>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="IOException">文件被独占锁定或 IO 错误。</exception>
    public static MappedFileReader Open(string path)
    {
        var fileSize = new FileInfo(path).Length;
        if (fileSize == 0)
            return new MappedFileReader(null, null, 0);

        var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        try
        {
            var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            return new MappedFileReader(mmf, accessor, fileSize);
        }
        catch
        {
            mmf.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 文件长度（字节）。
    /// </summary>
    public long Length => _fileSize;

    /// <summary>
    /// 读取全部字节到新数组。每次调用分配新数组，建议只调一次。
    /// </summary>
    public byte[] ToArray()
    {
        if (_fileSize == 0)
            return Array.Empty<byte>();
        var bytes = new byte[_fileSize];
        _accessor!.ReadArray(0, bytes, 0, (int)_fileSize);
        return bytes;
    }

    /// <summary>
    /// 读取全部内容为 UTF-8 字符串。
    /// </summary>
    public string ReadToEnd() => Encoding.UTF8.GetString(ToArray());

    /// <summary>
    /// 释放 mmap 句柄和视图访问器。NET10 单行 using 编译器展开为 try-finally 调用此方法。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _accessor?.Dispose();
        _mmf?.Dispose();
    }
}
