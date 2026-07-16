namespace IO.FileSystem;

/// <summary>
/// 内存文件系统实现 — 纯内存, 0磁盘IO, 用于测试
/// </summary>
public sealed class InMemoryFileSystem : IFileSystem
{
    private readonly ConcurrentDictionary<string, InMemoryFileEntry> _files = new();
    private readonly ConcurrentDictionary<string, InMemoryDirectoryEntry> _directories = new();
    private readonly List<InMemoryFileSystemWatcher> _watchers = [];
    private string _currentDirectory = "/test";

    public InMemoryFileSystem()
    {
        _directories[string.Empty] = new InMemoryDirectoryEntry { FullPath = string.Empty };
        _directories[NormalizePath(_currentDirectory)] = new InMemoryDirectoryEntry { FullPath = NormalizePath(_currentDirectory) };
    }

    // === File 写操作 ===

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        WriteAllText(path, contents);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken = default)
    {
        WriteAllText(path, contents, encoding);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void WriteAllText(string path, string contents)
    {
        var normalizedPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(normalizedPath) ?? string.Empty;
        EnsureDirectoryExists(directory);

        var isNew = !_files.ContainsKey(normalizedPath);
        var file = _files.GetOrAdd(normalizedPath, _ => new InMemoryFileEntry { FullPath = normalizedPath });
        file.TextContent = contents;
        file.LastWriteTime = DateTime.Now;
        file.CreationTime = file.CreationTime == default ? DateTime.Now : file.CreationTime;
        NotifyWatchers(path, isNew ? WatcherChangeTypes.Created : WatcherChangeTypes.Changed);
    }

    /// <inheritdoc />
    public void WriteAllText(string path, string contents, Encoding encoding)
    {
        var normalizedPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(normalizedPath) ?? string.Empty;
        EnsureDirectoryExists(directory);

        var isNew = !_files.ContainsKey(normalizedPath);
        var file = _files.GetOrAdd(normalizedPath, _ => new InMemoryFileEntry { FullPath = normalizedPath });

        // 对齐 File.WriteAllText: 编码写入时包含 preamble (BOM)
        var preamble = encoding.GetPreamble();
        var contentBytes = encoding.GetBytes(contents);
        if (preamble.Length == 0)
        {
            file.ByteContent = contentBytes;
        }
        else
        {
            var bytes = new byte[preamble.Length + contentBytes.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(contentBytes, 0, bytes, preamble.Length, contentBytes.Length);
            file.ByteContent = bytes;
        }

        file.TextContent = null; // 编码写入清除文本缓存，强制通过 ByteContent 解码
        file.LastWriteTime = DateTime.Now;
        file.CreationTime = file.CreationTime == default ? DateTime.Now : file.CreationTime;
        NotifyWatchers(path, isNew ? WatcherChangeTypes.Created : WatcherChangeTypes.Changed);
    }

    /// <inheritdoc />
    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        WriteAllBytes(path, bytes);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void WriteAllBytes(string path, byte[] bytes)
    {
        var normalizedPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(normalizedPath) ?? string.Empty;
        EnsureDirectoryExists(directory);

        var isNew = !_files.ContainsKey(normalizedPath);
        var file = _files.GetOrAdd(normalizedPath, _ => new InMemoryFileEntry { FullPath = normalizedPath });
        file.ByteContent = bytes;
        file.TextContent = null; // 二进制文件清除文本缓存
        file.LastWriteTime = DateTime.Now;
        file.CreationTime = file.CreationTime == default ? DateTime.Now : file.CreationTime;
        NotifyWatchers(path, isNew ? WatcherChangeTypes.Created : WatcherChangeTypes.Changed);
    }

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
        // 如果 ByteContent 存在，需要先解码为 TextContent 再追加，然后清除 ByteContent
        // 否则 ReadAllBytes 会优先返回过时的 ByteContent
        if (file.ByteContent is not null)
        {
            file.TextContent = (System.Text.Encoding.UTF8.GetString(file.ByteContent) + contents);
            file.ByteContent = null;
        }
        else
        {
            file.TextContent = (file.TextContent ?? string.Empty) + contents;
        }
        file.LastWriteTime = DateTime.Now;
        NotifyWatchers(path, WatcherChangeTypes.Changed);
    }

    // === File 读操作 ===

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadAllText(path));

    /// <inheritdoc />
    public Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadAllText(path, encoding));

    /// <inheritdoc />
    public string ReadAllText(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
        {
            // 优先返回 TextContent，若为 null 则从 ByteContent 解码
            if (file.TextContent is not null)
                return file.TextContent;
            if (file.ByteContent is not null)
            {
                // 对齐 File.ReadAllText: 使用 StreamReader 自动检测编码并跳过 BOM
                using var ms = new MemoryStream(file.ByteContent);
                using var reader = new StreamReader(ms, System.Text.Encoding.UTF8);
                return reader.ReadToEnd();
            }
            return string.Empty;
        }
        throw new FileNotFoundException($"文件未找到: {path}");
    }

    /// <inheritdoc />
    public string ReadAllText(string path, Encoding encoding)
    {
        var normalizedPath = NormalizePath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
        {
            if (file.ByteContent is not null)
            {
                // 对齐 File.ReadAllText(path, encoding): 使用指定编码解码，自动跳过 BOM
                using var ms = new MemoryStream(file.ByteContent);
                using var reader = new StreamReader(ms, encoding);
                return reader.ReadToEnd();
            }
            return file.TextContent ?? string.Empty;
        }
        throw new FileNotFoundException($"文件未找到: {path}");
    }

    /// <inheritdoc />
    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadAllLines(path));

    /// <inheritdoc />
    public string[] ReadAllLines(string path)
    {
        var content = ReadAllText(path);
        // 对齐 System.IO.File.ReadAllLines: 按行分割，去除行终止符，忽略末尾空行
        if (string.IsNullOrEmpty(content))
            return [];

        var lines = content.Split('\n');
        // Split('\n') 在 "line1\n" 时产生 ["line1", ""]，需移除尾部空字符串
        var result = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            // 跳过末尾的空行（由末尾 \n 产生的空字符串）
            if (i == lines.Length - 1 && line.Length == 0)
                continue;
            result.Add(line);
        }
        return result.ToArray();
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadAllBytes(path));

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
        {
            return file.ByteContent ?? System.Text.Encoding.UTF8.GetBytes(file.TextContent ?? string.Empty);
        }
        throw new FileNotFoundException($"文件未找到: {path}");
    }

    // === File 存在/删除/移动/复制 ===

    /// <inheritdoc />
    public bool FileExists(string path)
    {
        var normalizedPath = NormalizePath(path);
        return _files.ContainsKey(normalizedPath);
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        var normalizedPath = NormalizePath(path);
        _files.TryRemove(normalizedPath, out _);
        NotifyWatchers(path, WatcherChangeTypes.Deleted);
    }

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
        NotifyWatchersRenamed(sourcePath, destPath);
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
            TextContent = sourceFile.TextContent,
            ByteContent = sourceFile.ByteContent,
            LastWriteTime = DateTime.Now,
            CreationTime = DateTime.Now
        };
    }

    // === File 流操作 ===

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

        return mode switch
        {
            FileMode.Create or FileMode.CreateNew or FileMode.OpenOrCreate => new InMemoryFileStream(this, normalizedPath, mode, exists),
            FileMode.Open when exists => new MemoryStream(ReadAllBytes(path), writable: true),
            FileMode.Open => throw new FileNotFoundException($"文件未找到: {path}"),
            FileMode.Append => new InMemoryFileStream(this, normalizedPath, mode, exists),
            FileMode.Truncate when exists => new InMemoryFileStream(this, normalizedPath, mode, exists),
            FileMode.Truncate => throw new FileNotFoundException($"文件未找到: {path}"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <inheritdoc />
    public Stream CreateStream(string path, FileMode mode, FileAccess access, FileShare share)
        => Open(path, mode);

    // === File 时间戳 ===

    /// <inheritdoc />
    public DateTime GetLastWriteTime(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (_files.TryGetValue(normalizedPath, out var file))
            return file.LastWriteTime;
        return DateTime.MinValue;
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
        {
            if (file.ByteContent is not null)
                return file.ByteContent.Length;
            return System.Text.Encoding.UTF8.GetByteCount(file.TextContent ?? string.Empty);
        }
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

    // === Directory 操作 ===

    /// <inheritdoc />
    public bool DirectoryExists(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalizedPath = NormalizePath(path);
        return _directories.ContainsKey(normalizedPath);
    }

    /// <inheritdoc />
    public DirectoryInfo CreateDirectory(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (!string.IsNullOrEmpty(normalizedPath))
            EnsureDirectoryExists(normalizedPath);
        return new DirectoryInfo(path);
    }

    /// <inheritdoc />
    public void DeleteDirectory(string path, bool recursive = false)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(normalizedPath)) return;

        if (recursive)
        {
            var filesToDelete = _files.Keys.Where(f => f.StartsWith(normalizedPath + "/", StringComparison.Ordinal)).ToList();
            foreach (var file in filesToDelete)
                _files.TryRemove(file, out _);

            var dirsToDelete = _directories.Keys.Where(d => d.StartsWith(normalizedPath + "/", StringComparison.Ordinal)).OrderByDescending(d => d.Length).ToList();
            foreach (var dir in dirsToDelete)
                _directories.TryRemove(dir, out _);
        }

        _directories.TryRemove(normalizedPath, out _);
    }

    /// <inheritdoc />
    public string[] GetFiles(string path, string searchPattern, SearchOption searchOption)
        => EnumerateFiles(path, searchPattern, searchOption).ToArray();

    /// <inheritdoc />
    public string[] GetDirectories(string path, string searchPattern, SearchOption searchOption)
        => EnumerateDirectories(path, searchPattern, searchOption).ToArray();

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var normalizedPath = NormalizePath(path);
        var pattern = GlobToRegex(searchPattern);
        var regex = new System.Text.RegularExpressions.Regex("^" + pattern + "$");

        return _files.Values
            .Where(f => MatchDirectory(f.FullPath, normalizedPath, searchOption))
            .Where(f => regex.IsMatch(Path.GetFileName(f.FullPath)))
            .Select(f => f.FullPath.Replace('/', '\\'));
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalizedPath = NormalizePath(path);
        var pattern = GlobToRegex(searchPattern);
        var regex = new System.Text.RegularExpressions.Regex("^" + pattern + "$");

        return _directories.Keys
            .Where(d => !string.IsNullOrEmpty(d))
            .Where(d => MatchParentDirectory(d, normalizedPath, searchOption))
            .Where(d => regex.IsMatch(Path.GetFileName(d)))
            .Select(d => d.Replace('/', '\\'));
    }

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

    // === Path / 环境 ===

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

    // === Watch ===

    /// <inheritdoc />
    public IFileSystemWatcher Watch(string path, string filter = "*.*")
    {
        var watcher = new InMemoryFileSystemWatcher(this, path, filter);
        RegisterWatcher(watcher);
        return watcher;
    }

    // === 辅助方法 ===

    /// <summary>
    /// 注册 watcher — 由 InMemoryFileSystemWatcher 内部调用
    /// </summary>
    internal void RegisterWatcher(InMemoryFileSystemWatcher watcher)
    {
        lock (_watchers) _watchers.Add(watcher);
    }

    /// <summary>
    /// 注销 watcher — 由 InMemoryFileSystemWatcher.Dispose 内部调用
    /// </summary>
    internal void UnregisterWatcher(InMemoryFileSystemWatcher watcher)
    {
        lock (_watchers) _watchers.Remove(watcher);
    }

    private void NotifyWatchers(string fullPath, WatcherChangeTypes changeType)
    {
        List<InMemoryFileSystemWatcher> snapshot;
        lock (_watchers) snapshot = [.. _watchers];
        foreach (var watcher in snapshot)
            watcher.OnFileChanged(fullPath, changeType);
    }

    private void NotifyWatchersRenamed(string oldFullPath, string newFullPath)
    {
        List<InMemoryFileSystemWatcher> snapshot;
        lock (_watchers) snapshot = [.. _watchers];
        foreach (var watcher in snapshot)
            watcher.OnFileRenamed(oldFullPath, newFullPath);
    }

    /// <summary>
    /// 清空整个文件系统
    /// </summary>
    public void Clear()
    {
        _files.Clear();
        _directories.Clear();
        _directories[string.Empty] = new InMemoryDirectoryEntry { FullPath = string.Empty };
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

    internal static string NormalizePath(string path)
        => path?.Replace('\\', '/').Trim('/') ?? string.Empty;

    private static string GlobToRegex(string pattern)
        => pattern.Replace("*", ".*").Replace("?", ".");

    private static bool MatchDirectory(string filePath, string targetDir, SearchOption searchOption)
    {
        var dir = Path.GetDirectoryName(filePath)?.Replace('\\', '/') ?? string.Empty;
        if (searchOption == SearchOption.TopDirectoryOnly)
        {
            if (string.IsNullOrEmpty(targetDir))
                return string.IsNullOrEmpty(dir) || dir == ".";
            return dir == targetDir;
        }
        if (string.IsNullOrEmpty(targetDir))
            return true;
        return dir.StartsWith(targetDir + "/", StringComparison.Ordinal) || dir == targetDir;
    }

    private static bool MatchParentDirectory(string dirPath, string targetDir, SearchOption searchOption)
    {
        var parent = Path.GetDirectoryName(dirPath)?.Replace('\\', '/') ?? string.Empty;
        if (searchOption == SearchOption.TopDirectoryOnly)
        {
            if (string.IsNullOrEmpty(targetDir))
                return string.IsNullOrEmpty(parent) || parent == ".";
            return parent == targetDir;
        }
        if (string.IsNullOrEmpty(targetDir))
            return true;
        return parent.StartsWith(targetDir + "/", StringComparison.Ordinal) || parent == targetDir;
    }

    /// <summary>
    /// 内存文件条目
    /// </summary>
    private sealed class InMemoryFileEntry
    {
        public string FullPath { get; set; } = string.Empty;
        public string? TextContent { get; set; }
        public byte[]? ByteContent { get; set; }
        public DateTime LastWriteTime { get; set; }
        public DateTime CreationTime { get; set; }
    }

    /// <summary>
    /// 内存目录条目
    /// </summary>
    private sealed class InMemoryDirectoryEntry
    {
        public string FullPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// 内存文件流 — 写入时更新 InMemoryFileSystem
    /// </summary>
#pragma warning disable JCC9103 // Stream 基类要求同时实现 IDisposable 和 IAsyncDisposable
    private sealed class InMemoryFileStream : Stream
    {
        private readonly InMemoryFileSystem _fs;
        private readonly string _path;
        private readonly MemoryStream _inner;
        private bool _disposed;

        public InMemoryFileStream(InMemoryFileSystem fs, string path, FileMode mode, bool exists)
        {
            _fs = fs;
            _path = path;

            var initialData = exists && mode != FileMode.CreateNew
                ? (fs._files.TryGetValue(path, out var entry)
                    ? entry.ByteContent ?? System.Text.Encoding.UTF8.GetBytes(entry.TextContent ?? string.Empty)
                    : [])
                : [];

            // 使用可扩展的 MemoryStream — new MemoryStream(byte[], writable) 创建固定大小流，
            // 写入超过初始大小时抛 NotSupportedException，导致 JsonSerializer.SerializeAsync 失败
            _inner = new MemoryStream();
            if (initialData.Length > 0)
            {
                _inner.Write(initialData, 0, initialData.Length);
                if (mode != FileMode.Append)
                    _inner.Position = 0;
            }

            if (mode == FileMode.Append && initialData.Length > 0)
                _inner.Seek(0, SeekOrigin.End);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin)
            => _inner.Seek(offset, origin);

        public override void SetLength(long value)
            => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
            => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
            var data = _inner.ToArray();
            _fs.WriteAllBytes(_path.Replace('/', '\\'), data);
            _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
