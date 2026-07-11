namespace IO.FileSystem;

/// <summary>
/// 物理文件系统实现 — 直接委托给 System.IO.File / System.IO.Directory
/// </summary>
[Register]
public sealed class PhysicalFileSystem : IFileSystem
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
    public async Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        await File.AppendAllTextAsync(path, contents, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void AppendAllText(string path, string contents)
        => File.AppendAllText(path, contents);

    // === File 读操作 ===

    /// <inheritdoc />
    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
    {
        return await File.ReadAllTextAsync(path, encoding, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string ReadAllText(string path)
        => File.ReadAllText(path);

    /// <inheritdoc />
    public string ReadAllText(string path, Encoding encoding)
        => File.ReadAllText(path, encoding);

    /// <inheritdoc />
    public async Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        return await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
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
}
