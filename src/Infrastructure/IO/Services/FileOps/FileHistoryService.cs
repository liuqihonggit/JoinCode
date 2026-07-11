using JoinCode.Abstractions.Attributes;

namespace IO;

/// <summary>
/// File history backup service. Mirrors TS fileHistory.
/// Backs up files before writes to {AppData}/{FileHistoryFolderName}/{sessionId}/{hash}@v{version}.
/// Uses SHA256 hash of file path as backup filename for privacy and cross-platform safety.
/// </summary>
[Register]
public sealed partial class FileHistoryService : IFileHistoryService
{
    [Inject] private readonly ILogger<FileHistoryService>? _logger;
    private readonly IFileSystem _fs;
    private readonly string _baseDir;
    private readonly string _sessionId;
    private readonly Dictionary<string, int> _versionTracker = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileHistoryService(IFileSystem fs, ILogger<FileHistoryService>? logger = null)
    {
        _fs = fs;
        _logger = logger;
        _sessionId = Environment.ProcessId.ToString();

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _baseDir = Path.Combine(homeDir, AppDataConstants.AppDataFolder, AppDataConstants.FileHistoryFolderName);
    }

    /// <inheritdoc />
    public async Task<string?> BackupBeforeWriteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = Path.GetFullPath(filePath);

        if (!_fs.FileExists(normalizedPath))
            return null;

        int version;
        string backupPath;

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_versionTracker.TryGetValue(normalizedPath, out version))
                version = 0;

            version++;
            _versionTracker[normalizedPath] = version;
            backupPath = GetBackupFilePath(normalizedPath, version);
        }
        finally
        {
            _lock.Release();
        }

        try
        {
            var backupDir = Path.GetDirectoryName(backupPath)!;
            if (!_fs.DirectoryExists(backupDir))
                _fs.CreateDirectory(backupDir);

            _fs.CopyFile(normalizedPath, backupPath, overwrite: true);

            // Note: File attribute preservation is not supported through IFileSystem abstraction.
            // InMemoryFileSystem doesn't support file attributes, so this is intentionally omitted.

            _logger?.LogDebug("File history backup created: {BackupPath} (v{Version})", backupPath, version);

            await Task.CompletedTask.ConfigureAwait(false);
            return backupPath;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to create file history backup for: {FilePath}", normalizedPath);
            return null;
        }
    }

    /// <inheritdoc />
    public string? GetBackupPath(string filePath, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = Path.GetFullPath(filePath);
        var backupPath = GetBackupFilePath(normalizedPath, version);

        return _fs.FileExists(backupPath) ? backupPath : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<int> GetBackupVersions(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = Path.GetFullPath(filePath);
        var sessionDir = GetSessionDir();
        var hash = ComputePathHash(normalizedPath);
        var prefix = $"{hash}@v";

        if (!_fs.DirectoryExists(sessionDir))
            return Array.Empty<int>();

        var versions = new List<int>();
        foreach (var file in _fs.GetFiles(sessionDir, $"{prefix}*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            var versionStr = fileName.AsSpan(prefix.Length);
            if (int.TryParse(versionStr, out var v))
                versions.Add(v);
        }

        versions.Sort();
        return versions;
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(string filePath, int version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = Path.GetFullPath(filePath);
        var backupPath = GetBackupFilePath(normalizedPath, version);

        if (!_fs.FileExists(backupPath))
            return false;

        try
        {
            var dir = Path.GetDirectoryName(normalizedPath);
            if (!string.IsNullOrEmpty(dir) && !_fs.DirectoryExists(dir))
                _fs.CreateDirectory(dir);

            _fs.CopyFile(backupPath, normalizedPath, overwrite: true);

            _logger?.LogInformation("File restored from backup: {FilePath} (v{Version})", normalizedPath, version);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to restore file from backup: {FilePath} (v{Version})", normalizedPath, version);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task CleanupAsync(string filePath, int keepVersions = 5, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var versions = GetBackupVersions(filePath);
        if (versions.Count <= keepVersions)
            return;

        var normalizedPath = Path.GetFullPath(filePath);
        var toDelete = versions.Take(versions.Count - keepVersions);

        foreach (var version in toDelete)
        {
            var backupPath = GetBackupFilePath(normalizedPath, version);
            try
            {
                if (_fs.FileExists(backupPath))
                    _fs.DeleteFile(backupPath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to delete old backup: {BackupPath}", backupPath);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private string GetBackupFilePath(string normalizedPath, int version)
    {
        var hash = ComputePathHash(normalizedPath);
        var fileName = $"{hash}@v{version}";
        return Path.Combine(GetSessionDir(), fileName);
    }

    private string GetSessionDir()
    {
        return Path.Combine(_baseDir, _sessionId);
    }

    /// <summary>
    /// Compute SHA256 hash of file path, take first 16 hex chars.
    /// Mirrors TS getBackupFileName which uses sha256(path).slice(0,16).
    /// </summary>
    private static string ComputePathHash(string path)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(path));
        return Convert.ToHexString(bytes).AsSpan(0, 16).ToString();
    }
}
