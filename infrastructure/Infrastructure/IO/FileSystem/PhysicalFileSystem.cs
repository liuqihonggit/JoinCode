namespace IO.FileSystem;

/// <summary>
/// 物理文件系统实现 — 直接委托给 System.IO.File / System.IO.Directory
/// </summary>
[Register]
public sealed partial class PhysicalFileSystem : ServiceEntity, IFileSystem
{
    // === File 写操作 ===

    /// <inheritdoc />
    public async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(path, contents, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WriteAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(path, contents, encoding, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void WriteAllText(string path, string contents)
        => File.WriteAllText(path, contents);

    /// <inheritdoc />
    public void WriteAllText(string path, string contents, Encoding encoding)
        => File.WriteAllText(path, contents, encoding);

    /// <inheritdoc />
    public async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void WriteAllBytes(string path, byte[] bytes)
        => File.WriteAllBytes(path, bytes);

    /// <inheritdoc />
    /// <remarks>使用 FileShare.ReadWrite 允许并发读取者，避免跨进程读-写冲突。写-写互斥由调用方的 Named Mutex 保护。</remarks>
    public async Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        await AppendAllTextWithShareAsync(path, contents, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void AppendAllText(string path, string contents)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.Write(contents);
        writer.Flush();
    }

    // === File 读操作 ===

    /// <inheritdoc />
    /// <remarks>使用 FileShare.ReadWrite 允许并发写入者，避免跨进程读-写冲突。</remarks>
    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        return await ReadAllTextWithShareAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
    {
        return await ReadAllTextWithShareAsync(path, encoding, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string ReadAllText(string path)
        => File.ReadAllText(path);

    /// <inheritdoc />
    public string ReadAllText(string path, Encoding encoding)
        => File.ReadAllText(path, encoding);

    /// <inheritdoc />
    /// <remarks>使用 FileShare.ReadWrite 允许并发写入者，避免跨进程读-写冲突。</remarks>
    public async Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        return await ReadAllLinesWithShareAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string[] ReadAllLines(string path)
        => File.ReadAllLines(path);

    /// <inheritdoc />
    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path)
        => File.ReadAllBytes(path);

    // === File 存在/删除/移动/复制 ===

    /// <inheritdoc />
    public bool FileExists(string path)
        => File.Exists(path);

    /// <inheritdoc />
    public void DeleteFile(string path)
        => File.Delete(path);

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destPath, bool overwrite = false)
        => File.Move(sourcePath, destPath, overwrite);

    /// <inheritdoc />
    public void CopyFile(string sourcePath, string destPath, bool overwrite = false)
        => File.Copy(sourcePath, destPath, overwrite);

    // === File 流操作 ===

    /// <inheritdoc />
    public Stream OpenRead(string path)
        => File.OpenRead(path);

    /// <inheritdoc />
    public Stream Open(string path, FileMode mode)
        => File.Open(path, mode);

    /// <inheritdoc />
    public Stream CreateStream(string path, FileMode mode, FileAccess access, FileShare share)
        => new FileStream(path, mode, access, share);

    // === File 时间戳 ===

    /// <inheritdoc />
    public DateTime GetLastWriteTime(string path)
        => File.GetLastWriteTime(path);

    /// <inheritdoc />
    public DateTime GetLastWriteTimeUtc(string path)
        => File.GetLastWriteTimeUtc(path);

    /// <inheritdoc />
    public DateTime GetCreationTime(string path)
        => File.GetCreationTime(path);

    /// <inheritdoc />
    public DateTime GetCreationTimeUtc(string path)
        => File.GetCreationTimeUtc(path);

    /// <inheritdoc />
    public long GetFileLength(string path)
        => new FileInfo(path).Length;

    /// <inheritdoc />
    public FileAttributes GetFileAttributes(string path)
        => File.GetAttributes(path);

    // === Directory 操作 ===

    /// <inheritdoc />
    public bool DirectoryExists(string path)
        => Directory.Exists(path);

    /// <inheritdoc />
    public DirectoryInfo CreateDirectory(string path)
        => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public void DeleteDirectory(string path, bool recursive = false)
        => Directory.Delete(path, recursive);

    /// <inheritdoc />
    public string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
        => Directory.GetFiles(path, searchPattern, searchOption);

    /// <inheritdoc />
    public string[] GetDirectories(string path, string searchPattern, SearchOption searchOption)
        => Directory.GetDirectories(path, searchPattern, searchOption);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        => Directory.EnumerateFiles(path, searchPattern, searchOption);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
        => Directory.EnumerateDirectories(path, searchPattern, searchOption);

    /// <inheritdoc />
    public void MoveDirectory(string sourceDir, string destDir)
        => Directory.Move(sourceDir, destDir);

    /// <inheritdoc />
    public DateTime GetDirectoryLastWriteTimeUtc(string path)
        => Directory.GetLastWriteTimeUtc(path);

    /// <inheritdoc />
    public void SetDirectoryLastWriteTimeUtc(string path, DateTime utcTime)
        => Directory.SetLastWriteTimeUtc(path, utcTime);

    /// <inheritdoc />
    public string? GetParentPath(string path)
    {
        var dir = Directory.GetParent(path);
        return dir?.FullName;
    }

    /// <inheritdoc />
    public string GetDirectoryName(string path)
        => new DirectoryInfo(path).Name;

    // === Path / 环境 ===

    /// <inheritdoc />
    public string GetCurrentDirectory()
        => Directory.GetCurrentDirectory();

    /// <inheritdoc />
    public void SetCurrentDirectory(string path)
        => Directory.SetCurrentDirectory(path);

    /// <inheritdoc />
    public string GetFullPath(string path)
        => Path.GetFullPath(path);

    /// <inheritdoc />
    public string CombinePath(params string[] paths)
        => Path.Combine(paths);

    // === Watch ===

    /// <inheritdoc />
    public IFileSystemWatcher Watch(string path, string filter = "*.*")
        => new PhysicalFileSystemWatcher(path, filter);

    // === 跨进程安全的文件 I/O ===

    /// <summary>
    /// 追加写入 — FileShare.ReadWrite 允许并发读取者
    /// </summary>
    private static async Task AppendAllTextWithShareAsync(string path, string contents, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(contents.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取全部文本 — FileShare.ReadWrite 允许并发写入者
    /// </summary>
    private static async Task<string> ReadAllTextWithShareAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取全部文本（指定编码）— FileShare.ReadWrite 允许并发写入者
    /// </summary>
    private static async Task<string> ReadAllTextWithShareAsync(string path, Encoding encoding, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, encoding);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取所有行 — FileShare.ReadWrite 允许并发写入者
    /// </summary>
    private static async Task<string[]> ReadAllLinesWithShareAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
        }
        return lines.ToArray();
    }
}
