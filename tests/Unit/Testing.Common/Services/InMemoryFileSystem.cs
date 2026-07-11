namespace Testing.Common.Services;

/// <summary>
/// 内存文件系统条目基类
/// </summary>
public abstract class InMemoryFileSystemEntry
{
    public string FullPath { get; set; } = string.Empty;
    public string Name => Path.GetFileName(FullPath);
    public DateTime LastWriteTime { get; set; } = DateTime.Now;
    public DateTime CreationTime { get; set; } = DateTime.Now;
}

/// <summary>
/// 内存文件条目
/// </summary>
public sealed class InMemoryFileEntry : InMemoryFileSystemEntry
{
    public string Content { get; set; } = string.Empty;
    public byte[] Bytes => System.Text.Encoding.UTF8.GetBytes(Content);
    public long Length => Bytes.Length;
    public int LineCount => Content.Split('\n').Length;
}

/// <summary>
/// 内存目录条目
/// </summary>
public sealed class InMemoryDirectoryEntry : InMemoryFileSystemEntry
{
    public ConcurrentDictionary<string, InMemoryFileSystemEntry> Entries { get; } = new();
}

/// <summary>
/// 内存文件系统 - 用于测试的高速内存存储
/// 同时实现 IFileSystem 接口，方便测试项目注册为 DI 服务
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly InMemoryDirectoryEntry _root = new() { FullPath = "" };
    private readonly ConcurrentDictionary<string, InMemoryFileEntry> _files = new();
    private readonly ConcurrentDictionary<string, InMemoryDirectoryEntry> _directories = new();
    private string _currentDirectory = "/test";

    public InMemoryFileSystem()
    {
        _directories[string.Empty] = _root;
        _directories[NormalizePath(_currentDirectory)] = new InMemoryDirectoryEntry { FullPath = NormalizePath(_currentDirectory) };
    }

    // === IFileSystem: File 写操作 ===

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        WriteAllText(path, contents);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default)
    {
        WriteAllText(path, contents);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 写入文件内容
    /// </summary>
    public void WriteAllText(string path, string content)
    {
        var normalizedPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(normalizedPath) ?? string.Empty;

        EnsureDirectoryExists(directory);

        var file = _files.GetOrAdd(normalizedPath, _ => new InMemoryFileEntry { FullPath = normalizedPath });
        file.Content = content;
        file.LastWriteTime = DateTime.Now;
    }

    /// <inheritdoc />
    public void WriteAllText(string path, string contents, Encoding encoding)
        => WriteAllText(path, contents);

    /// <inheritdoc />
    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        // 内存文件系统以文本为主，二进制转 Base64 存储
        WriteAllText(path, Convert.ToBase64String(bytes));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void WriteAllBytes(string path, byte[] bytes)
        => WriteAllText(path, Convert.ToBase64String(bytes));

    /// <inheritdoc />
    public Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        AppendAllText(path, contents);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void AppendAllText(string path, string contents)
    {
        var normalizedPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(normalizedPath) ?? string.Empty;
        EnsureDirectoryExists(directory);

        var file = _files.GetOrAdd(normalizedPath, _ => new InMemoryFileEntry { FullPath = normalizedPath });
        file.Content += contents;
        file.LastWriteTime = DateTime.Now;
    }

    // === IFileSystem: File 读操作 ===

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadAllText(path));

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadAllText(path));

    /// <summary>
    /// 读取文件内容
    /// </summary>
    public string ReadAllText(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
        {
            return file.Content;
        }
        throw new FileNotFoundException($"文件未找到: {path}");
    }

    /// <inheritdoc />
    public string ReadAllText(string path, Encoding encoding)
        => ReadAllText(path);

    /// <inheritdoc />
    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadAllLines(path));

    /// <summary>
    /// 读取文件所有行
    /// </summary>
    public string[] ReadAllLines(string path)
    {
        var content = ReadAllText(path);
        return content.Split('\n');
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadAllBytes(path));

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path)
    {
        var content = ReadAllText(path);
        try
        {
            return Convert.FromBase64String(content);
        }
        catch (FormatException)
        {
            return System.Text.Encoding.UTF8.GetBytes(content);
        }
    }

    // === IFileSystem: File 存在/删除/移动/复制 ===

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    public bool FileExists(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _files.ContainsKey(normalizedPath);
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    public bool DeleteFile(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _files.TryRemove(normalizedPath, out _);
    }

    /// <inheritdoc />
    void IFileSystem.DeleteFile(string path)
        => DeleteFile(path);

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destPath, bool overwrite = false)
    {
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDest = NormalizePath(destPath);

        if (!_files.TryGetValue(normalizedSource, out var file))
            throw new FileNotFoundException($"源文件未找到: {sourcePath}");

        if (_files.ContainsKey(normalizedDest) && !overwrite)
            throw new IOException($"目标文件已存在: {destPath}");

        var destDir = Path.GetDirectoryName(normalizedDest) ?? string.Empty;
        EnsureDirectoryExists(destDir);

        _files.TryRemove(normalizedSource, out _);
        file.FullPath = normalizedDest;
        _files[normalizedDest] = file;
    }

    /// <inheritdoc />
    public void CopyFile(string sourcePath, string destPath, bool overwrite = false)
    {
        var normalizedSource = NormalizePath(sourcePath);
        var normalizedDest = NormalizePath(destPath);

        if (!_files.TryGetValue(normalizedSource, out var sourceFile))
            throw new FileNotFoundException($"源文件未找到: {sourcePath}");

        if (_files.ContainsKey(normalizedDest) && !overwrite)
            throw new IOException($"目标文件已存在: {destPath}");

        var destDir = Path.GetDirectoryName(normalizedDest) ?? string.Empty;
        EnsureDirectoryExists(destDir);

        _files[normalizedDest] = new InMemoryFileEntry
        {
            FullPath = normalizedDest,
            Content = sourceFile.Content,
            LastWriteTime = DateTime.Now,
            CreationTime = DateTime.Now
        };
    }

    // === IFileSystem: File 流操作 ===

    /// <inheritdoc />
    public Stream OpenRead(string path)
    {
        var bytes = ReadAllBytes(path);
        return new MemoryStream(bytes, writable: false);
    }

    /// <inheritdoc />
    public Stream Open(string path, FileMode mode)
    {
        var normalizedPath = NormalizePath(path);
        var exists = _files.ContainsKey(normalizedPath);

        if (mode == FileMode.Create || mode == FileMode.CreateNew || mode == FileMode.OpenOrCreate)
        {
            if (!exists)
                WriteAllText(path, string.Empty);
            var content = ReadAllText(path);
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content), writable: true);
        }

        if (mode == FileMode.Open && !exists)
            throw new FileNotFoundException($"文件未找到: {path}");

        if (mode == FileMode.Append)
        {
            var content = exists ? ReadAllText(path) : string.Empty;
            var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content), writable: true);
            ms.Seek(0, SeekOrigin.End);
            return ms;
        }

        if (mode == FileMode.Truncate && !exists)
            throw new FileNotFoundException($"文件未找到: {path}");

        return new MemoryStream([]);
    }

    /// <inheritdoc />
    public Stream CreateStream(string path, FileMode mode, FileAccess access, FileShare share)
        => Open(path, mode);

    // === IFileSystem: File 时间戳 ===

    /// <summary>
    /// 获取文件最后写入时间
    /// </summary>
    public DateTime GetLastWriteTime(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
        {
            return file.LastWriteTime;
        }
        throw new FileNotFoundException($"文件未找到: {path}");
    }

    /// <inheritdoc />
    public DateTime GetLastWriteTimeUtc(string path)
        => GetLastWriteTime(path).ToUniversalTime();

    /// <inheritdoc />
    public DateTime GetCreationTime(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
            return file.CreationTime;
        return DateTime.MinValue;
    }

    /// <inheritdoc />
    public DateTime GetCreationTimeUtc(string path)
        => GetCreationTime(path).ToUniversalTime();

    /// <inheritdoc />
    public long GetFileLength(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
            return file.Length;
        throw new FileNotFoundException($"文件未找到: {path}");
    }

    /// <inheritdoc />
    public FileAttributes GetFileAttributes(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_files.ContainsKey(normalizedPath))
            return FileAttributes.Normal;
        if (_directories.ContainsKey(normalizedPath))
            return FileAttributes.Directory;
        throw new FileNotFoundException($"文件未找到: {path}");
    }

    // === IFileSystem: Directory 操作 ===

    /// <summary>
    /// 检查目录是否存在
    /// </summary>
    public bool DirectoryExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalizedPath = NormalizePath(path);
        return _directories.ContainsKey(normalizedPath);
    }

    /// <summary>
    /// 创建目录
    /// </summary>
    public DirectoryInfo CreateDirectory(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(normalizedPath)) return new DirectoryInfo(path);

        EnsureDirectoryExists(normalizedPath);
        return new DirectoryInfo(path);
    }

    /// <summary>
    /// 删除目录（递归）
    /// </summary>
    public bool DeleteDirectory(string path, bool recursive = true)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(normalizedPath)) return false;

        if (recursive)
        {
            // 删除所有子文件
            var filesToDelete = _files.Keys.Where(f => f.StartsWith(normalizedPath + "/", StringComparison.Ordinal)).ToList();
            foreach (var file in filesToDelete)
            {
                _files.TryRemove(file, out _);
            }

            // 删除所有子目录
            var dirsToDelete = _directories.Keys.Where(d => d.StartsWith(normalizedPath + "/", StringComparison.Ordinal)).OrderByDescending(d => d.Length).ToList();
            foreach (var dir in dirsToDelete)
            {
                _directories.TryRemove(dir, out _);
            }
        }

        return _directories.TryRemove(normalizedPath, out _);
    }

    /// <inheritdoc />
    void IFileSystem.DeleteDirectory(string path, bool recursive)
        => DeleteDirectory(path, recursive);

    /// <inheritdoc />
    public string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
        => EnumerateFiles(path, searchPattern, searchOption).ToArray();

    /// <inheritdoc />
    public string[] GetDirectories(string path, string searchPattern, SearchOption searchOption)
        => EnumerateDirectories(path, searchPattern, searchOption).ToArray();

    /// <summary>
    /// 枚举文件
    /// </summary>
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        var normalizedPath = NormalizePath(path);
        var targetDir = normalizedPath;

        var pattern = searchPattern.Replace("*", ".*").Replace("?", ".");
        var regex = new System.Text.RegularExpressions.Regex("^" + pattern + "$");

        var files = _files.Values.Where(f =>
        {
            var dir = Path.GetDirectoryName(f.FullPath)?.Replace('\\', '/') ?? string.Empty;
            if (searchOption == SearchOption.TopDirectoryOnly)
            {
                if (string.IsNullOrEmpty(targetDir))
                {
                    return string.IsNullOrEmpty(dir) || dir == ".";
                }
                return dir == targetDir;
            }
            if (string.IsNullOrEmpty(targetDir))
            {
                return true;
            }
            return dir.StartsWith(targetDir + "/", StringComparison.Ordinal) || dir == targetDir;
        });

        if (searchPattern != "*")
        {
            files = files.Where(f => regex.IsMatch(Path.GetFileName(f.FullPath)));
        }

        return files.Select(f => f.FullPath.Replace('/', '\\'));
    }

    /// <inheritdoc />
    IEnumerable<string> IFileSystem.EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        => EnumerateFiles(path, searchPattern, searchOption);

    /// <summary>
    /// 枚举目录
    /// </summary>
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalizedPath = NormalizePath(path);
        var targetDir = normalizedPath;

        var dirs = _directories.Keys.Where(d =>
        {
            if (string.IsNullOrEmpty(d)) return false;
            var parent = Path.GetDirectoryName(d)?.Replace('\\', '/') ?? string.Empty;
            if (searchOption == SearchOption.TopDirectoryOnly)
            {
                if (string.IsNullOrEmpty(targetDir))
                {
                    return string.IsNullOrEmpty(parent) || parent == ".";
                }
                return parent == targetDir;
            }
            if (string.IsNullOrEmpty(targetDir))
            {
                return true;
            }
            return parent.StartsWith(targetDir + "/", StringComparison.Ordinal) || parent == targetDir;
        });

        return dirs.Select(d => d.Replace('/', '\\'));
    }

    /// <inheritdoc />
    IEnumerable<string> IFileSystem.EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
        => EnumerateDirectories(path, searchPattern, searchOption);

    /// <inheritdoc />
    public void MoveDirectory(string sourceDir, string destDir)
    {
        var normalizedSource = NormalizePath(sourceDir);
        var normalizedDest = NormalizePath(destDir);

        if (!_directories.ContainsKey(normalizedSource))
            throw new DirectoryNotFoundException($"源目录未找到: {sourceDir}");

        EnsureDirectoryExists(normalizedDest);

        // 移动所有子文件
        var filesToMove = _files.Keys.Where(f => f.StartsWith(normalizedSource + "/", StringComparison.Ordinal)).ToList();
        foreach (var file in filesToMove)
        {
            if (_files.TryRemove(file, out var entry))
            {
                var newPath = normalizedDest + file.Substring(normalizedSource.Length);
                entry.FullPath = newPath;
                _files[newPath] = entry;
            }
        }

        // 移动所有子目录
        var dirsToMove = _directories.Keys.Where(d => d.StartsWith(normalizedSource + "/", StringComparison.Ordinal)).OrderBy(d => d.Length).ToList();
        foreach (var dir in dirsToMove)
        {
            if (_directories.TryRemove(dir, out var entry))
            {
                var newPath = normalizedDest + dir.Substring(normalizedSource.Length);
                entry.FullPath = newPath;
                _directories[newPath] = entry;
            }
        }

        _directories.TryRemove(normalizedSource, out _);
        _directories[normalizedDest] = new InMemoryDirectoryEntry { FullPath = normalizedDest };
    }

    /// <inheritdoc />
    public DateTime GetDirectoryLastWriteTimeUtc(string path)
        => DateTime.UtcNow;

    /// <inheritdoc />
    public string? GetParentPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        var parent = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
        return string.IsNullOrEmpty(parent) ? null : parent.Replace('/', '\\');
    }

    /// <inheritdoc />
    public string GetDirectoryName(string path)
    {
        var normalizedPath = NormalizePath(path);
        var name = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return name ?? path;
    }

    // === IFileSystem: Path / 环境 ===

    /// <inheritdoc />
    public string GetCurrentDirectory()
        => _currentDirectory;

    /// <inheritdoc />
    public void SetCurrentDirectory(string path)
        => _currentDirectory = path;

    /// <inheritdoc />
    public string GetFullPath(string path)
    {
        if (Path.IsPathFullyQualified(path))
            return path;
        return Path.Combine(_currentDirectory, path).Replace('\\', '/');
    }

    /// <inheritdoc />
    public string CombinePath(params string[] paths)
        => Path.Combine(paths);

    // === 辅助方法 ===

    /// <summary>
    /// 获取文件信息
    /// </summary>
    public InMemoryFileEntry? GetFileInfo(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalizedPath = NormalizePath(path);
        _files.TryGetValue(normalizedPath, out var file);
        return file;
    }

    /// <summary>
    /// 清空整个文件系统
    /// </summary>
    public void Clear()
    {
        _files.Clear();
        _directories.Clear();
        _directories[string.Empty] = _root;
    }

    private void EnsureDirectoryExists(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(normalizedPath)) return;

        var parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentPath = string.Empty;

        foreach (var part in parts)
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
            _directories.GetOrAdd(currentPath, _ => new InMemoryDirectoryEntry { FullPath = currentPath });
        }
    }

    private static string NormalizePath(string path)
        => path?.Replace('\\', '/').Trim('/') ?? string.Empty;

    // === IFileSystem: Watch ===

    public IFileSystemWatcher Watch(string path, string filter = "*.*")
        => new NullFileSystemWatcher { Path = path, Filter = filter };

    private sealed class NullFileSystemWatcher : IFileSystemWatcher
    {
        public string Path { get; set; } = string.Empty;
        public string Filter { get; set; } = "*.*";
        public ICollection<string> Filters { get; } = new List<string>();
        public bool IncludeSubdirectories { get; set; }
        public NotifyFilters NotifyFilter { get; set; }
        public bool EnableRaisingEvents { get; set; }
        public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(500);
        public int InternalWriteWindowMs { get; set; } = 5000;
        public event EventHandler<FileChangedEventArgs>? Changed { add { } remove { } }
        public event EventHandler<FileChangedEventArgs>? Created { add { } remove { } }
        public event EventHandler<FileChangedEventArgs>? Deleted { add { } remove { } }
        public event EventHandler<FileRenamedEventArgs>? Renamed { add { } remove { } }
        public event EventHandler<FileChangedEventArgs>? DebouncedChanged { add { } remove { } }
        public event EventHandler<FileChangedEventArgs>? DebouncedCreated { add { } remove { } }
        public event EventHandler<FileChangedEventArgs>? DebouncedDeleted { add { } remove { } }
        public event EventHandler<FileRenamedEventArgs>? DebouncedRenamed { add { } remove { } }
        public void MarkInternalWrite(string filePath) { }
        public void Dispose() { }
    }
}
