namespace Core.Security.Auditing;

/// <summary>
/// 文件系统快照服务 — 捕获目录文件状态并对比变更
/// <para>
/// 通过 IFileSystem 抽象层枚举文件,捕获每个文件的相对路径/大小/最后修改时间
/// 对比两个快照时按相对路径匹配,检测 Created/Modified/Deleted
/// </para>
/// </summary>
[Register(typeof(IFileSystemSnapshotService), ServiceLifetime.Singleton)]
public sealed class FileSystemSnapshotService : IFileSystemSnapshotService
{
    private readonly IFileSystem _fs;
    private readonly ILogger<FileSystemSnapshotService>? _logger;

    /// <summary>
    /// 创建 FileSystemSnapshotService
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">日志器(可选)</param>
    public FileSystemSnapshotService(
        IFileSystem fs,
        ILogger<FileSystemSnapshotService>? logger = null)
    {
        _fs = fs;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<FileSystemSnapshot> CaptureAsync(string directoryPath, CancellationToken ct = default)
    {
        if (!_fs.DirectoryExists(directoryPath))
            return Task.FromResult(new FileSystemSnapshot(directoryPath, DateTimeOffset.UtcNow, FrozenDictionary<string, FileSnapshotEntry>.Empty));

        var capturedAt = DateTimeOffset.UtcNow;
        var entries = CaptureEntries(directoryPath, ct);
        return Task.FromResult(new FileSystemSnapshot(directoryPath, capturedAt, entries));
    }

    /// <inheritdoc/>
    public IReadOnlyList<FileChangeRecord> Compare(FileSystemSnapshot before, FileSystemSnapshot after)
    {
        return DetectChanges(before, after)
            .OrderBy(c => c.Path)
            .ToList();
    }

    private FrozenDictionary<string, FileSnapshotEntry> CaptureEntries(string directoryPath, CancellationToken ct)
    {
        var builder = new Dictionary<string, FileSnapshotEntry>(StringComparer.Ordinal);

        foreach (var filePath in _fs.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(directoryPath, filePath).Replace('\\', '/');
            var size = _fs.GetFileLength(filePath);
            var lastWrite = _fs.GetLastWriteTimeUtc(filePath);
            builder[relativePath] = new FileSnapshotEntry(relativePath, size, lastWrite);
        }

        return builder.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static IEnumerable<FileChangeRecord> DetectChanges(FileSystemSnapshot before, FileSystemSnapshot after)
    {
        // Deleted: in before but not in after
        foreach (var (path, oldEntry) in before.Entries)
        {
            if (!after.Entries.ContainsKey(path))
                yield return new FileChangeRecord(path, FileChangeType.Deleted, oldEntry.Size, null);
        }

        // Created + Modified: in after
        foreach (var (path, newEntry) in after.Entries)
        {
            if (!before.Entries.TryGetValue(path, out var oldEntry))
            {
                yield return new FileChangeRecord(path, FileChangeType.Created, null, newEntry.Size);
            }
            else if (oldEntry.Size != newEntry.Size || oldEntry.LastWriteTimeUtc != newEntry.LastWriteTimeUtc)
            {
                yield return new FileChangeRecord(path, FileChangeType.Modified, oldEntry.Size, newEntry.Size);
            }
        }
    }
}
