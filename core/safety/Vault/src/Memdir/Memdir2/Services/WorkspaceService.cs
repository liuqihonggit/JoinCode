
namespace Core.Memdir;

[Register]
public sealed partial class WorkspaceService : ServiceEntity, IWorkspaceService
{
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    [Inject] private readonly ILogger<WorkspaceService>? _logger;

    public WorkspaceService(ILogger<WorkspaceService>? logger = null)
    {
        _logger = logger;
    }

    public bool AddDirectory(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var fullPath = Path.GetFullPath(path);

        if (_directories.Contains(fullPath))
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
        var removed = _directories.Remove(fullPath);

        if (removed)
        {
            _logger?.LogInformation(L.T(StringKey.VaultLogRemovedWorkspace), fullPath);
            return true;
        }

        _logger?.LogDebug(L.T(StringKey.VaultLogDirectoryNotExist), fullPath);
        return false;
    }

    public IEnumerable<string> GetAdditionalDirectories()
    {
        return _directories;
    }

    public void Clear()
    {
        _directories.Clear();
        _logger?.LogInformation(L.T(StringKey.VaultLogClearedWorkspaces));
    }
}
