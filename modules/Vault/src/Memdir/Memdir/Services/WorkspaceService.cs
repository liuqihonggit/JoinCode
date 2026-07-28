
namespace Core.Memdir;

[Register]
public sealed partial class WorkspaceService : IWorkspaceService
{
    private readonly List<string> _directories = [];
    [Inject] private readonly ILogger<WorkspaceService>? _logger;

    public WorkspaceService(ILogger<WorkspaceService>? logger = null)
    {
        _logger = logger;
    }

    public bool AddDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fullPath = Path.GetFullPath(path);

        if (_directories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            _logger?.LogDebug(L.T(StringKey.VaultLogDirectoryExists), fullPath);
            return false;
        }

        _directories.Add(fullPath);
        _logger?.LogInformation(L.T(StringKey.VaultLogAddedWorkspace), fullPath);
        return true;
    }

    public bool RemoveDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fullPath = Path.GetFullPath(path);
        var removed = _directories.RemoveAll(d =>
            string.Equals(d, fullPath, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            _logger?.LogInformation(L.T(StringKey.VaultLogRemovedWorkspace), fullPath);
            return true;
        }

        _logger?.LogDebug(L.T(StringKey.VaultLogDirectoryNotExist), fullPath);
        return false;
    }

    public IReadOnlyList<string> GetAdditionalDirectories()
    {
        return _directories.AsReadOnly();
    }

    public void Clear()
    {
        _directories.Clear();
        _logger?.LogInformation(L.T(StringKey.VaultLogClearedWorkspaces));
    }
}
