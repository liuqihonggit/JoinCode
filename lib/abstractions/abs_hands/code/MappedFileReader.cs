namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// mmap 零拷贝文件读取器 — 封装 <see cref="MemoryMappedFile"/> + <see cref="MemoryMappedViewAccessor"/>，
/// 调用方用 <c>using var</c>（NET10 单行）释放，编译器自动展开为 try-finally 保证句柄回收。
/// <para>ADR: 0071 — P0 底层优化，供 PhysicalFileSystem / RgEngine / 文件编辑核心复用。</para>
/// <para>用法：</para>
/// <code>
/// using var reader = new MappedFileReader(path);
/// var content = reader.ReadToEnd();
/// </code>
/// </summary>
public sealed class MappedFileReader : IDisposable {
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly long _fileSize;
    private bool _disposed;

    /// <summary>
    /// 打开文件并创建 mmap 映射。文件映射到虚拟内存，按需分页，不立即分配物理内存。
    /// <para>用 FileStream(FileShare.ReadWrite) 打开文件，允许多读并发（独占打开会导致并发读 IOException）。</para>
    /// <para>mmap 从 FileStream 创建（leaveOpen=false），MemoryMappedFile.Dispose 自动关闭 FileStream。</para>
    /// <para>空文件不创建 mmap（BCL 限制），_mmf/_accessor 为 null，Length=0，ReadToEnd 返回空字符串。</para>
    /// <para>调用方用 <c>using var</c> 释放，编译器展开为 try-finally 调用 Dispose。</para>
    /// </summary>
    /// <param name="path">文件绝对路径。</param>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="IOException">文件被独占锁定或 IO 错误。</exception>
    public MappedFileReader(string path) {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try {
            _fileSize = fs.Length;
            if (_fileSize == 0) {
                fs.Dispose();
                return;
            }
            _mmf = MemoryMappedFile.CreateFromFile(fs, null, _fileSize, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        } catch {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 文件长度（字节）。
    /// </summary>
    public long Length => _fileSize;

    /// <summary>
    /// 读取全部字节到新数组。
    /// </summary>
    public byte[] ToArray() {
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
    /// 释放 mmap 句柄和视图访问器。由 <c>using var</c> 编译器展开的 try-finally 调用。
    /// </summary>
    public void Dispose() {
        if (_disposed)
            return;
        _disposed = true;
        _accessor?.Dispose();
        _mmf?.Dispose();
    }
}