namespace JoinCode.Cli;

/// <summary>
/// 信任目录管理器 — CLI 简化版，实现 ITrustFolderManager
/// </summary>
[Register]
public sealed partial class TrustFolderManager : ITrustFolderManager
{
    private readonly string _trustedFoldersPath;
    private readonly IFileSystem _fs;

    public TrustFolderManager(IFileSystem fs)
    {
        _fs = fs;
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder);
        _trustedFoldersPath = Path.Combine(appDataPath, AppDataConstants.TrustedFoldersFileName);
    }

    internal TrustFolderManager(IFileSystem fs, string trustedFoldersPath)
    {
        _fs = fs;
        _trustedFoldersPath = trustedFoldersPath;
    }

    /// <inheritdoc/>
    public bool IsTrusted(string folderPath)
    {
        var normalized = NormalizePath(folderPath);
        var folders = LoadTrustedFolders();
        return folders.Contains(normalized);
    }

    /// <inheritdoc/>
    public void Trust(string folderPath)
    {
        var normalized = NormalizePath(folderPath);
        var folders = LoadTrustedFolders();
        if (folders.Add(normalized))
        {
            SaveTrustedFolders(folders);
        }
    }

    /// <inheritdoc/>
    public void Untrust(string folderPath)
    {
        var normalized = NormalizePath(folderPath);
        var folders = LoadTrustedFolders();
        if (folders.Remove(normalized))
        {
            SaveTrustedFolders(folders);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetAllTrustedFolders()
    {
        return [.. LoadTrustedFolders()];
    }

    /// <inheritdoc/>
    public void ClearAll()
    {
        SaveTrustedFolders([]);
    }

    private HashSet<string> LoadTrustedFolders()
    {
        if (!_fs.FileExists(_trustedFoldersPath))
        {
            return [];
        }

        try
        {
            var json = _fs.ReadAllText(_trustedFoldersPath);
            var entries = System.Text.Json.JsonSerializer.Deserialize(json, TrustFoldersContext.Default.TrustFolderEntries);
            if (entries?.Folders is null)
            {
                return [];
            }

            return new HashSet<string>(entries.Folders, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return [];
        }
    }

    private void SaveTrustedFolders(HashSet<string> folders)
    {
        var dir = Path.GetDirectoryName(_trustedFoldersPath);
        DirectoryHelper.EnsureDirectoryExists(_fs, dir);

        var entries = new TrustFolderEntries { Folders = [.. folders] };
        var json = System.Text.Json.JsonSerializer.Serialize(entries, TrustFoldersContext.Default.TrustFolderEntries);
        _fs.WriteAllText(_trustedFoldersPath, json);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

/// <summary>
/// 信任目录条目 — JSON 序列化用
/// </summary>
public sealed class TrustFolderEntries
{
    public List<string> Folders { get; set; } = [];
}

/// <summary>
/// 信任目录 JSON 序列化上下文
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(TrustFolderEntries))]
public sealed partial class TrustFoldersContext : System.Text.Json.Serialization.JsonSerializerContext;
