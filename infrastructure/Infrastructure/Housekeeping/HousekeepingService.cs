namespace Infrastructure.Housekeeping;

/// <summary>
/// 后台家政清理服务 — 对齐 TS startBackgroundHousekeeping / cleanupOldMessageFilesInBackground
/// 聚合调度所有 CleanupOld* 方法，延迟执行+循环清理
/// </summary>
[Register(typeof(IHousekeepingService), ServiceLifetime.Singleton)]
public sealed partial class HousekeepingService : ServiceEntity, IHousekeepingService
{

    public HousekeepingService(IFileSystem fs, IClockService clock, IPlanModeManager planModeManager, IAgentWorktreeService worktreeService, IEntityReaper? entityReaper = null, ILogger<HousekeepingService>? logger = null)
    {
        _fs = fs;
        _clock = clock;
        _planModeManager = planModeManager;
        _worktreeService = worktreeService;
        _entityReaper = entityReaper;
        _logger = logger;
    }
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly IPlanModeManager _planModeManager;
    private readonly IAgentWorktreeService _worktreeService;
    private readonly IEntityReaper? _entityReaper;
    private readonly ILogger<HousekeepingService>? _logger;

    private static readonly string JccDir = WorkflowConstants.Paths.JccDirectory;

    public async Task<int> RunAllCleanupAsync(string currentSessionId = "", CancellationToken cancellationToken = default)
    {
        var total = 0;

        total += CleanupOldSessionFiles();
        total += CleanupOldFileHistoryBackups();
        total += CleanupOldSessionEnvDirs();
        total += CleanupOldDebugLogs();
        total += CleanupOldMessageFiles();
        total += CleanupOldImageCaches(currentSessionId);
        total += CleanupOldPastes();
        total += CleanupOldPlanFiles();
        total += CleanupNpmCache();
        total += CleanupOldVersions();
        total += await CleanupStaleWorktreesAsync(cancellationToken).ConfigureAwait(false);

        if (_entityReaper is not null)
        {
            total += _entityReaper.ScanOnce();
        }

        if (total > 0)
        {
            _logger?.LogDebug("家政清理完成: 共清理 {Total} 项", total);
        }

        return total;
    }

    /// <summary>
    /// 清理旧会话文件 — 对齐 TS cleanupOldSessionFiles
    /// 删除 sessions/*.json + *.cast + tool-results/ 中 mtime 超过指定天数的
    /// </summary>
    public int CleanupOldSessionFiles(int maxAgeDays = 30)
    {
        var sessionsDir = Path.Combine(JccDir, AppDataConstants.SessionsFolderName);

        var total = CleanupFilesInDirectory(
            sessionsDir,
            maxAgeDays,
            ["*.json", "*.cast"],
            includeSubDirPattern: AppDataConstants.ToolResultsFolderName);

        // 清理过期的会话子目录(每会话一文件夹格式 {id}/)
        total += CleanupSessionDirectories(sessionsDir, maxAgeDays);

        return total;
    }

    /// <summary>
    /// 清理过期的会话子目录 — 每会话一文件夹格式,目录 mtime 超过 maxAgeDays 则整个删除
    /// </summary>
    private int CleanupSessionDirectories(string sessionsDir, int maxAgeDays)
    {
        if (!_fs.DirectoryExists(sessionsDir)) return 0;

        var cutoff = _clock.GetUtcNow().AddDays(-maxAgeDays);
        var deleted = 0;

        foreach (var dir in _fs.EnumerateDirectories(sessionsDir, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (_fs.GetDirectoryLastWriteTimeUtc(dir) < cutoff)
                {
                    _fs.DeleteDirectory(dir, recursive: true);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "删除过期会话目录失败: {Path}", dir);
            }
        }

        return deleted;
    }

    /// <summary>
    /// 清理旧文件历史备份 — 对齐 TS cleanupOldFileHistoryBackups
    /// </summary>
    public int CleanupOldFileHistoryBackups(int maxAgeDays = 30)
    {
        return CleanupDirectoryChildren(
            Path.Combine(JccDir, AppDataConstants.FileHistoryFolderName),
            maxAgeDays);
    }

    /// <summary>
    /// 清理旧会话环境目录 — 对齐 TS cleanupOldSessionEnvDirs
    /// </summary>
    public int CleanupOldSessionEnvDirs(int maxAgeDays = 30)
    {
        return CleanupDirectoryChildren(
            Path.Combine(JccDir, "session-env"),
            maxAgeDays);
    }

    /// <summary>
    /// 清理旧调试日志 — 对齐 TS cleanupOldDebugLogs
    /// </summary>
    public int CleanupOldDebugLogs(int maxAgeDays = 30)
    {
        return CleanupFilesInDirectory(
            Path.Combine(JccDir, "debug"),
            maxAgeDays,
            ["*.txt"]);
    }

    /// <summary>
    /// 清理旧消息/错误日志 — 对齐 TS cleanupOldMessageFiles
    /// </summary>
    public int CleanupOldMessageFiles(int maxAgeDays = 30)
    {
        var total = 0;

        total += CleanupFilesInDirectory(
            Path.Combine(JccDir, "errors"),
            maxAgeDays,
            ["*"]);

        try
        {
            if (!_fs.DirectoryExists(JccDir)) return total;

            foreach (var mcpLogDir in _fs.EnumerateDirectories(JccDir, "mcp-logs-*", SearchOption.TopDirectoryOnly))
            {
                total += CleanupFilesInDirectory(mcpLogDir, maxAgeDays, ["*"]);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理 mcp-logs 目录失败");
        }

        return total;
    }

    /// <summary>
    /// 清理旧 npm 缓存 — 对齐 TS cleanupNpmCacheForAnthropicPackages
    /// </summary>
    public int CleanupNpmCache(int maxAgeDays = 1, int retentionCount = 5)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var npmCacheDir = Path.Combine(userProfile, ".npm", "_cacache");

        if (!_fs.DirectoryExists(npmCacheDir)) return 0;

        try
        {
            var cutoffDate = _clock.GetUtcNow().AddDays(-maxAgeDays);
            var deletedCount = 0;

            foreach (var file in _fs.EnumerateFiles(npmCacheDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (_fs.GetLastWriteTimeUtc(file) < cutoffDate)
                    {
                        _fs.DeleteFile(file);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "删除 npm 缓存文件失败: {Path}", file);
                }
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理 npm 缓存失败");
            return 0;
        }
    }

    /// <summary>
    /// 清理旧版本二进制 — 对齐 TS cleanupOldVersions
    /// </summary>
    public int CleanupOldVersions(int retentionCount = 2)
    {
        var deletedCount = 0;

        try
        {
            var versionsDir = Path.Combine(JccDir, "versions");
            if (!_fs.DirectoryExists(versionsDir)) return 0;

            var stagingDir = Path.Combine(versionsDir, "staging");
            if (_fs.DirectoryExists(stagingDir))
            {
                var stagingCutoff = _clock.GetUtcNow().AddHours(-1);
                foreach (var dir in _fs.EnumerateDirectories(stagingDir, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (_fs.GetDirectoryLastWriteTimeUtc(dir) < stagingCutoff)
                        {
                            _fs.DeleteDirectory(dir, recursive: true);
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "删除 staging 目录失败: {Path}", dir);
                    }
                }
            }

            var currentExePath = Environment.ProcessPath;
            foreach (var file in _fs.EnumerateFiles(versionsDir, "*.tmp.*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (_fs.GetLastWriteTimeUtc(file) < _clock.GetUtcNow().AddHours(-1))
                    {
                        _fs.DeleteFile(file);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "删除临时版本文件失败: {Path}", file);
                }
            }

            var versionFiles = _fs.EnumerateFiles(versionsDir, "jcc-*", SearchOption.TopDirectoryOnly)
                .Where(f => !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => _fs.GetLastWriteTimeUtc(f))
                .ToList();

            foreach (var file in versionFiles.Skip(retentionCount))
            {
                try
                {
                    if (currentExePath is not null &&
                        string.Equals(Path.GetFullPath(file), Path.GetFullPath(currentExePath), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _fs.DeleteFile(file);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "删除旧版本文件失败: {Path}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理旧版本失败");
        }

        return deletedCount;
    }

    /// <summary>
    /// 通用: 清理目录中匹配模式的旧文件
    /// </summary>
    private int CleanupFilesInDirectory(
        string directory,
        int maxAgeDays,
        string[] patterns,
        string? includeSubDirPattern = null)
    {
        if (!_fs.DirectoryExists(directory)) return 0;

        try
        {
            var cutoffDate = _clock.GetUtcNow().AddDays(-maxAgeDays);
            var deletedCount = 0;

            foreach (var pattern in patterns)
            {
                foreach (var file in _fs.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (_fs.GetLastWriteTimeUtc(file) < cutoffDate)
                        {
                            _fs.DeleteFile(file);
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "删除旧文件失败: {Path}", file);
                    }
                }
            }

            if (includeSubDirPattern is not null)
            {
                foreach (var subDir in _fs.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    var toolResultsDir = Path.Combine(subDir, includeSubDirPattern);
                    if (!_fs.DirectoryExists(toolResultsDir)) continue;

                    if (_fs.GetDirectoryLastWriteTimeUtc(toolResultsDir) < cutoffDate)
                    {
                        try
                        {
                            _fs.DeleteDirectory(toolResultsDir, recursive: true);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "删除旧子目录失败: {Path}", toolResultsDir);
                        }
                    }
                }
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理目录失败: {Directory}", directory);
            return 0;
        }
    }

    /// <summary>
    /// 通用: 清理目录中 mtime 超过指定天数的子目录
    /// </summary>
    private int CleanupDirectoryChildren(string directory, int maxAgeDays)
    {
        if (!_fs.DirectoryExists(directory)) return 0;

        try
        {
            var cutoffDate = _clock.GetUtcNow().AddDays(-maxAgeDays);
            var deletedCount = 0;

            foreach (var subDir in _fs.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (_fs.GetDirectoryLastWriteTimeUtc(subDir) < cutoffDate)
                    {
                        _fs.DeleteDirectory(subDir, recursive: true);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "删除旧子目录失败: {Path}", subDir);
                }
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理子目录失败: {Directory}", directory);
            return 0;
        }
    }

    /// <summary>
    /// 清理旧图片缓存目录 — 对齐 TS cleanupOldImageCaches
    /// 删除 image-cache/ 下非当前会话的子目录，空目录也删除
    /// </summary>
    public int CleanupOldImageCaches(string currentSessionId)
    {
        var imageCacheDir = Path.Combine(JccDir, "image-cache");

        if (!_fs.DirectoryExists(imageCacheDir)) return 0;

        try
        {
            var deletedCount = 0;

            foreach (var sessionDir in _fs.EnumerateDirectories(imageCacheDir, "*", SearchOption.TopDirectoryOnly))
            {
                var dirName = Path.GetFileName(sessionDir);
                if (dirName == currentSessionId) continue;

                try
                {
                    _fs.DeleteDirectory(sessionDir, recursive: true);
                    deletedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "删除旧图片缓存目录失败: {Path}", sessionDir);
                }
            }

            try
            {
                if (!_fs.EnumerateDirectories(imageCacheDir, "*", SearchOption.TopDirectoryOnly).Any()
                    && !_fs.EnumerateFiles(imageCacheDir, "*", SearchOption.TopDirectoryOnly).Any())
                {
                    _fs.DeleteDirectory(imageCacheDir, recursive: false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "删除空图片缓存根目录失败");
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理图片缓存失败");
            return 0;
        }
    }

    /// <summary>
    /// 清理旧粘贴缓存 — 对齐 TS cleanupOldPastes
    /// 删除 paste-cache/ 中 mtime 超过指定天数的 .txt 文件
    /// </summary>
    public int CleanupOldPastes(int maxAgeDays = 30)
    {
        var pasteCacheDir = Path.Combine(JccDir, "paste-cache");

        if (!_fs.DirectoryExists(pasteCacheDir)) return 0;

        try
        {
            var cutoffDate = _clock.GetUtcNow().AddDays(-maxAgeDays);
            var deletedCount = 0;

            foreach (var file in _fs.EnumerateFiles(pasteCacheDir, "*.txt", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (_fs.GetLastWriteTimeUtc(file) < cutoffDate)
                    {
                        _fs.DeleteFile(file);
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "删除旧粘贴缓存文件失败: {Path}", file);
                }
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理粘贴缓存失败");
            return 0;
        }
    }

    /// <summary>
    /// 清理旧计划文件 — 对齐 TS cleanupOldPlanFiles
    /// 委托 IPlanModeManager.CleanupOldPlanFiles
    /// </summary>
    public int CleanupOldPlanFiles(int maxAgeDays = 30)
    {
        try
        {
            return _planModeManager.CleanupOldPlanFiles(maxAgeDays);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理计划文件失败");
            return 0;
        }
    }

    /// <summary>
    /// 清理过期 Agent Worktree — 对齐 TS cleanupStaleAgentWorktrees
    /// 委托 IAgentWorktreeService.CleanupStaleWorktreesAsync
    /// </summary>
    public async Task<int> CleanupStaleWorktreesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _worktreeService.CleanupStaleWorktreesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "清理过期 Worktree 失败");
            return 0;
        }
    }
}
