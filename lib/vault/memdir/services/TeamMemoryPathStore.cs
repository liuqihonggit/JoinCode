namespace Core.Memdir;

/// <summary>
/// 团队内存路径存储 — 管理 TeamMemoryPath 的 CRUD 与持久化（.jcc/memory/team-paths.json）
/// </summary>
internal sealed class TeamMemoryPathStore {
    private readonly Dictionary<(string TeamId, string Path), TeamMemoryPath> _teamMemoryPaths = new();
    private readonly IPersistencePipeline? _persistencePipeline;
    private readonly IFileSystem? _fs;
    private readonly ILogger? _logger;
    private int _teamPathsLoaded;
    private static readonly string TeamPathsSubDir = Path.Combine(AppDataConstants.AppDataFolder, "memory");
    private const string TeamPathsFileName = "team-paths.json";

    /// <summary>
    /// 构造团队内存路径存储实例
    /// </summary>
    /// <param name="persistencePipeline">可选的持久化管道</param>
    /// <param name="fs">可选的文件系统抽象</param>
    /// <param name="logger">可选日志记录器</param>
    public TeamMemoryPathStore(IPersistencePipeline? persistencePipeline, IFileSystem? fs, ILogger? logger) {
        _persistencePipeline = persistencePipeline;
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// 添加团队内存路径 — 移除已存在的相同路径后重新插入并持久化
    /// </summary>
    public async Task AddTeamMemoryPathCoreAsync(string teamId, string path, bool isShared, List<string>? allowedAgents, CancellationToken ct) {
        await EnsureTeamPathsLoadedAsync(ct).ConfigureAwait(false);
        // 移除已存在的相同路径
        _teamMemoryPaths.Remove((teamId, path));

        _teamMemoryPaths[(teamId, path)] = new TeamMemoryPath {
            TeamId = teamId,
            Path = path,
            IsShared = isShared,
            AllowedAgents = allowedAgents ?? new List<string>()
        };

        _logger?.LogInformation(L.T(StringKey.VaultLogAddTeamPath), teamId, path);
        await SaveTeamPathsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取团队内存路径列表 — 按 teamId 过滤（null 或空则返回全部）
    /// </summary>
    public async Task<List<TeamMemoryPath>> GetTeamMemoryPathsCoreAsync(string? teamId, CancellationToken ct) {
        await EnsureTeamPathsLoadedAsync(ct).ConfigureAwait(false);
        return GetTeamMemoryPathsCore(teamId);
    }

    /// <summary>
    /// 同步获取团队内存路径列表（无加载检查，调用方需先 EnsureTeamPathsLoadedAsync）
    /// </summary>
    public List<TeamMemoryPath> GetTeamMemoryPathsCore(string? teamId) {
        var paths = _teamMemoryPaths.Values.AsEnumerable();

        if (!string.IsNullOrEmpty(teamId)) {
            paths = paths.Where(p => p.TeamId == teamId);
        }

        return paths.ToList();
    }

    /// <summary>
    /// 移除团队内存路径 — 成功移除后持久化
    /// </summary>
    public async Task<bool> RemoveTeamMemoryPathCoreAsync(string teamId, string path, CancellationToken ct) {
        await EnsureTeamPathsLoadedAsync(ct).ConfigureAwait(false);
        var removed = _teamMemoryPaths.Remove((teamId, path));
        if (removed) {
            _logger?.LogInformation(L.T(StringKey.VaultLogRemoveTeamPath), teamId, path);
            await SaveTeamPathsAsync(ct).ConfigureAwait(false);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 持久化团队路径到 .jcc/memory/team-paths.json(通过统一持久化管道)。
    /// </summary>
    private async Task SaveTeamPathsAsync(CancellationToken ct) {
        if (_persistencePipeline is null) return;

        var snapshot = _teamMemoryPaths.Values.ToList();
        var json = RelaxedJsonSerializer.Serialize(snapshot, MemdirJsonContext.Default);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new PersistRequest {
            Category = "memory",
            Directory = TeamPathsSubDir,
            FileName = TeamPathsFileName,
            Content = json,
            Completion = tcs,
        };
        await _persistencePipeline.EnqueueAsync(request, ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// 从 .jcc/memory/team-paths.json 加载团队路径(若存在且尚未加载)。Interlocked 保证只执行一次。
    /// </summary>
    private async Task EnsureTeamPathsLoadedAsync(CancellationToken ct) {
        if (_fs is null || Interlocked.CompareExchange(ref _teamPathsLoaded, 1, 0) != 0) return;

        try {
            var root = GitWorkspaceResolver.FindGitWorkspaceDir(null, _fs!);
            if (root is null) return;
            var path = _fs.CombinePath(_fs.CombinePath(root, TeamPathsSubDir), TeamPathsFileName);
            if (!_fs.FileExists(path)) return;
            var json = await _fs.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var list = RelaxedJsonSerializer.Deserialize<List<TeamMemoryPath>>(json, MemdirJsonContext.Default);
            if (list is null) return;
            foreach (var tp in list) {
                _teamMemoryPaths[(tp.TeamId, tp.Path)] = tp;
            }
            _logger?.LogDebug("已加载 {Count} 条团队内存路径 from {Path}", list.Count, path);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "加载团队内存路径失败");
        }
    }
}