namespace JoinCode.Cli;

/// <summary>
/// 信任目录管理器 — CLI 简化版，实现 ITrustFolderManager
/// </summary>
[Register(typeof(ITrustFolderManager), ServiceLifetime.Singleton)]
public sealed partial class TrustFolderManager : ServiceEntity, ITrustFolderManager
{
    private readonly string _trustedFoldersPath;
    private readonly IFileSystem _fs;

    public TrustFolderManager(IFileSystem fs)
    {
        _fs = fs;
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
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
            var entries = RelaxedJsonSerializer.Deserialize(json, TrustFoldersContext.Default.TrustFolderEntries);
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
        var json = RelaxedJsonSerializer.Serialize(entries, TrustFoldersContext.Default);
        try
        {
            _fs.WriteAllText(_trustedFoldersPath, json);
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrustFolderManager] 无法写入信任目录文件（沙箱环境）: {ex.Message}");
        }
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
[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(TrustFolderEntries))]
public sealed partial class TrustFoldersContext : System.Text.Json.Serialization.JsonSerializerContext;
