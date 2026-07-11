
namespace Core.Memdir;

[Register]
public sealed partial class FileOperationTracker : IFileOperationTracker
{
    private readonly List<FileOperationEntry> _entries = [];
    [Inject] private readonly ILogger<FileOperationTracker>? _logger;

    public FileOperationTracker(ILogger<FileOperationTracker>? logger = null)
    {
        _logger = logger;
    }

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

    public IReadOnlyList<FileOperationEntry> GetAllEntries()
    {
        return _entries.AsReadOnly();
    }

    public IReadOnlyList<string> GetOperatedFilePaths()
    {
        return _entries
            .Select(e => e.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public void Clear()
    {
        _entries.Clear();
        _logger?.LogDebug(L.T(StringKey.VaultLogFileOperationCleared));
    }
}
