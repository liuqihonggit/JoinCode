
namespace Core.Memdir;

/// <summary>
/// 记忆路径管理
/// 管理用户/项目/团队记忆路径
/// </summary>
public interface IMemoryPaths
{
    /// <summary>
    /// 获取基础记忆目录
    /// </summary>
    string GetBaseMemoryDirectory();

    /// <summary>
    /// 获取用户记忆目录
    /// </summary>
    string GetUserMemoryDirectory(string? userId = null);

    /// <summary>
    /// 获取项目记忆目录
    /// </summary>
    string GetProjectMemoryDirectory(string? projectId = null);

    /// <summary>
    /// 获取特定类型的记忆目录
    /// </summary>
    string GetMemoryDirectoryByType(MemoryType type, string? contextId = null);

    /// <summary>
    /// 获取记忆文件路径
    /// </summary>
    string GetMemoryFilePath(string memoryId, MemoryType type, string? contextId = null);
}

/// <summary>
/// 记忆路径管理实现
/// </summary>
[Register]
public sealed partial class MemoryPaths : IMemoryPaths
{
    private static readonly FrozenDictionary<MemoryType, string> TypeDirectoryNames =
        Enum.GetValues<MemoryType>().ToFrozenDictionary(t => t, t => t.ToString().ToLowerInvariant());

    private readonly string _baseDirectory;
    private readonly string? _currentUserId;
    private readonly string? _currentProjectId;

    public MemoryPaths(IOptions<MemdirOptions> options)
    {
        _baseDirectory = options?.Value?.StoragePath ?? GetDefaultBaseDirectory();
        _currentUserId = null;
        _currentProjectId = null;
    }

    /// <inheritdoc />
    public string GetBaseMemoryDirectory()
    {
        return _baseDirectory;
    }

    /// <inheritdoc />
    public string GetUserMemoryDirectory(string? userId = null)
    {
        var id = userId ?? _currentUserId ?? "default";
        return Path.Combine(_baseDirectory, "users", id);
    }

    /// <inheritdoc />
    public string GetProjectMemoryDirectory(string? projectId = null)
    {
        var id = projectId ?? _currentProjectId ?? "default";
        return Path.Combine(_baseDirectory, "projects", id);
    }

    /// <inheritdoc />
    public string GetMemoryDirectoryByType(MemoryType type, string? contextId = null)
    {
        var typeName = TypeDirectoryNames.GetValueOrDefault(type, type.GetName().ToLowerInvariant());

        return type switch
        {
            MemoryType.User => GetUserMemoryDirectory(contextId),
            MemoryType.Feedback => Path.Combine(GetUserMemoryDirectory(contextId), "feedback"),
            MemoryType.Project => GetProjectMemoryDirectory(contextId),
            MemoryType.Reference => Path.Combine(_baseDirectory, "references"),
            _ => Path.Combine(_baseDirectory, typeName)
        };
    }

    /// <inheritdoc />
    public string GetMemoryFilePath(string memoryId, MemoryType type, string? contextId = null)
    {
        var directory = GetMemoryDirectoryByType(type, contextId);
        return Path.Combine(directory, $"{memoryId}.json");
    }

    /// <summary>
    /// 获取默认基础目录
    /// </summary>
    private static string GetDefaultBaseDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder,
            "memories");
    }
}

/// <summary>
/// 团队记忆路径管理
/// </summary>
public interface ITeamMemoryPaths
{
    /// <summary>
    /// 获取团队记忆基础目录
    /// </summary>
    string GetTeamMemoryDirectory(string teamId);

    /// <summary>
    /// 获取团队共享记忆目录
    /// </summary>
    string GetTeamSharedDirectory(string teamId);

    /// <summary>
    /// 获取团队成员个人记忆目录
    /// </summary>
    string GetTeamMemberDirectory(string teamId, string userId);
}

/// <summary>
/// 团队记忆路径管理实现
/// </summary>
[Register]
public sealed partial class TeamMemoryPaths : ITeamMemoryPaths
{
    private readonly string _baseDirectory;

    public TeamMemoryPaths(IOptions<MemdirOptions> options)
    {
        var storagePath = options?.Value?.StoragePath;
        _baseDirectory = storagePath is not null
            ? Path.Combine(storagePath, "team-memories")
            : GetDefaultTeamBaseDirectory();
    }

    /// <inheritdoc />
    public string GetTeamMemoryDirectory(string teamId)
    {
        return Path.Combine(_baseDirectory, "teams", teamId);
    }

    /// <inheritdoc />
    public string GetTeamSharedDirectory(string teamId)
    {
        return Path.Combine(GetTeamMemoryDirectory(teamId), "shared");
    }

    /// <inheritdoc />
    public string GetTeamMemberDirectory(string teamId, string userId)
    {
        return Path.Combine(GetTeamMemoryDirectory(teamId), "members", userId);
    }

    private static string GetDefaultTeamBaseDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppDataConstants.AppDataFolder,
            "team-memories");
    }
}
