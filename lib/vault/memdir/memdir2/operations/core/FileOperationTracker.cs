
namespace Core.Memdir;

/// <summary>
/// 文件操作跟踪器 — 记录所有文件操作历史,用于审计和冲突检测
/// </summary>
[Register(typeof(IFileOperationTracker), ServiceLifetime.Singleton)]
public sealed partial class FileOperationTracker : ServiceEntity, IFileOperationTracker
{
    private readonly List<FileOperationEntry> _entries = [];
    private readonly ILogger<FileOperationTracker>? _logger;

    /// <summary>
    /// 构造文件操作跟踪器
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public FileOperationTracker(ILogger<FileOperationTracker>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Track(string filePath, FileOperationType operationType)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var fullPath = Path.GetFullPath(filePath);
        _entries.Add(new FileOperationEntry
        {
            FilePath = fullPath,
            OperationType = operationType
        });

        _logger?.LogDebug(L.T(StringKey.VaultLogFileOperationRecord), operationType, fullPath);
    }

    /// <inheritdoc />
    public IEnumerable<FileOperationEntry> GetAllEntries()
    {
        return _entries;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetOperatedFilePaths()
    {
        return _entries
            .Select(e => e.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _entries.Clear();
        _logger?.LogDebug(L.T(StringKey.VaultLogFileOperationCleared));
    }
}
