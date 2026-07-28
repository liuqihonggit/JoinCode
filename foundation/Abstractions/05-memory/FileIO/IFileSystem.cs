namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 文件系统抽象 — 1:1 映射 System.IO.File + System.IO.Directory
/// <para>生产环境: PhysicalFileSystem (委托给 System.IO)</para>
/// <para>测试环境: InMemoryFileSystem (纯内存, 0磁盘IO)</para>
/// </summary>
public interface IFileSystem
{
    // === File 写操作 ===

    /// <summary>异步写入文件全部文本 — 对齐 File.WriteAllTextAsync</summary>
    Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);

    /// <summary>异步写入文件全部文本(指定编码) — 对齐 File.WriteAllTextAsync</summary>
    Task WriteAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default);

    /// <summary>同步写入文件全部文本 — 对齐 File.WriteAllText</summary>
    void WriteAllText(string path, string contents);

    /// <summary>同步写入文件全部文本(指定编码) — 对齐 File.WriteAllText</summary>
    void WriteAllText(string path, string contents, Encoding encoding);

    /// <summary>异步写入文件全部字节 — 对齐 File.WriteAllBytesAsync</summary>
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default);

    /// <summary>同步写入文件全部字节 — 对齐 File.WriteAllBytes</summary>
    void WriteAllBytes(string path, byte[] bytes);

    /// <summary>异步追加文本 — 对齐 File.AppendAllTextAsync</summary>
    Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken = default);

    /// <summary>同步追加文本 — 对齐 File.AppendAllText</summary>
    void AppendAllText(string path, string contents);

    // === File 读操作 ===

    /// <summary>异步读取文件全部文本 — 对齐 File.ReadAllTextAsync</summary>
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>异步读取文件全部文本(指定编码) — 对齐 File.ReadAllTextAsync</summary>
    Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default);

    /// <summary>同步读取文件全部文本 — 对齐 File.ReadAllText</summary>
    string ReadAllText(string path);

    /// <summary>同步读取文件全部文本(指定编码) — 对齐 File.ReadAllText</summary>
    string ReadAllText(string path, Encoding encoding);

    /// <summary>异步读取文件全部行 — 对齐 File.ReadAllLinesAsync</summary>
    Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>同步读取文件全部行 — 对齐 File.ReadAllLines</summary>
    string[] ReadAllLines(string path);

    /// <summary>异步读取文件全部字节 — 对齐 File.ReadAllBytesAsync</summary>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>同步读取文件全部字节 — 对齐 File.ReadAllBytes</summary>
    byte[] ReadAllBytes(string path);

    // === File 存在/删除/移动/复制 ===

    /// <summary>检查文件是否存在 — 对齐 File.Exists</summary>
    bool FileExists(string path);

    /// <summary>删除文件 — 对齐 File.Delete</summary>
    void DeleteFile(string path);

    /// <summary>移动文件 — 对齐 File.Move</summary>
    void MoveFile(string sourcePath, string destPath, bool overwrite = false);

    /// <summary>复制文件 — 对齐 File.Copy</summary>
    void CopyFile(string sourcePath, string destPath, bool overwrite = false);

    // === File 流操作 ===

    /// <summary>打开文件读取流 — 对齐 File.OpenRead</summary>
    Stream OpenRead(string path);

    /// <summary>打开文件流 — 对齐 File.Open</summary>
    Stream Open(string path, FileMode mode);

    /// <summary>创建文件流 — 对齐 new FileStream</summary>
    Stream CreateStream(string path, FileMode mode, FileAccess access, FileShare share);

    // === File 时间戳 ===

    /// <summary>获取文件最后写入时间 — 对齐 File.GetLastWriteTime</summary>
    DateTime GetLastWriteTime(string path);

    /// <summary>获取文件最后写入时间(UTC) — 对齐 File.GetLastWriteTimeUtc</summary>
    DateTime GetLastWriteTimeUtc(string path);

    /// <summary>获取文件创建时间 — 对齐 File.GetCreationTime</summary>
    DateTime GetCreationTime(string path);

    /// <summary>获取文件创建时间(UTC) — 对齐 File.GetCreationTimeUtc</summary>
    DateTime GetCreationTimeUtc(string path);

    /// <summary>获取文件大小(字节) — 对齐 new FileInfo(path).Length</summary>
    long GetFileLength(string path);

    /// <summary>获取文件属性 — 对齐 File.GetAttributes</summary>
    FileAttributes GetFileAttributes(string path);

    // === Directory 操作 ===

    /// <summary>检查目录是否存在 — 对齐 Directory.Exists</summary>
    bool DirectoryExists(string path);

    /// <summary>创建目录 — 对齐 Directory.CreateDirectory</summary>
    DirectoryInfo CreateDirectory(string path);

    /// <summary>删除目录 — 对齐 Directory.Delete</summary>
    void DeleteDirectory(string path, bool recursive = false);

    /// <summary>获取目录中的文件 — 对齐 Directory.GetFiles</summary>
    string[] GetFiles(string path, string searchPattern, SearchOption searchOption);

    /// <summary>获取目录中的子目录 — 对齐 Directory.GetDirectories</summary>
    string[] GetDirectories(string path, string searchPattern, SearchOption searchOption);

    /// <summary>枚举目录中的文件 — 对齐 Directory.EnumerateFiles</summary>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);

    /// <summary>枚举目录中的子目录 — 对齐 Directory.EnumerateDirectories</summary>
    IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption);

    /// <summary>移动目录 — 对齐 Directory.Move</summary>
    void MoveDirectory(string sourceDir, string destDir);

    /// <summary>获取目录最后写入时间(UTC) — 对齐 Directory.GetLastWriteTimeUtc</summary>
    DateTime GetDirectoryLastWriteTimeUtc(string path);

    /// <summary>设置目录最后写入时间(UTC) — 对齐 Directory.SetLastWriteTimeUtc</summary>
    void SetDirectoryLastWriteTimeUtc(string path, DateTime utcTime);

    /// <summary>获取父目录路径 — 对齐 DirectoryInfo.Parent.FullName，根目录返回 null</summary>
    string? GetParentPath(string path);

    /// <summary>获取路径的目录名部分 — 对齐 DirectoryInfo(path).Name</summary>
    string GetDirectoryName(string path);

    // === Path / 环境 ===

    /// <summary>获取当前工作目录 — 对齐 Directory.GetCurrentDirectory</summary>
    string GetCurrentDirectory();

    /// <summary>设置当前工作目录 — 对齐 Directory.SetCurrentDirectory</summary>
    void SetCurrentDirectory(string path);

    /// <summary>获取完整路径 — 对齐 Path.GetFullPath</summary>
    string GetFullPath(string path);

    /// <summary>组合路径 — 对齐 Path.Combine</summary>
    string CombinePath(params string[] paths);

    // === Watch ===

    /// <summary>
    /// 创建文件系统监视器 — 对齐 new FileSystemWatcher(path, filter)
    /// <para>返回的 IFileSystemWatcher 需要由调用方设置 EnableRaisingEvents = true 并订阅事件</para>
    /// </summary>
    IFileSystemWatcher Watch(string path, string filter = "*.*");
}
