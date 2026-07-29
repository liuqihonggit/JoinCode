namespace Core.Configuration;

[Register]
public sealed partial class ConfigChangeNotifier : IConfigChangeNotifier, IDisposable
{
    [Inject] private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<ConfigChangeNotifier>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;
    private readonly List<IFileSystemWatcher> _watchers = [];
    private readonly HashSet<string> _monitoredDirs = [];
    private bool _disposed;

    private static readonly string[] RootConfigFiles =
    [
        "AGENTS.md", "agents.md",
        "CLAUDE.md", "claude.md",
        "CLAUDE.local.md", "claude.local.md",
        "codex.md"
    ];

    private static readonly string[] RulesSubDirs =
    [
        Path.Combine(".trae", "rules"),
        Path.Combine(".claude", "rules"),
        Path.Combine(".codex", "rules"),
        Path.Combine(AppDataConstants.AppDataFolder, AppDataConstants.RulesFolderName)
    ];

    private static readonly string[] CommandsSubDirs =
    [
        Path.Combine(".trae", "commands"),
        Path.Combine(".claude", "commands"),
        Path.Combine(".codex", "commands"),
        Path.Combine(AppDataConstants.AppDataFolder, AppDataConstants.CommandsFolderName)
    ];

    private static readonly string[] AppDataConfigFiles =
    [
        AppDataConstants.SettingsFileName,
        AppDataConstants.AuthFileName
    ];

    public event EventHandler<ConfigChangeEventArgs>? ConfigChanged;

    public void StartMonitoring(string workingDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StopMonitoring();

        var absWorkingDir = Path.GetFullPath(workingDirectory);

        WatchRootConfigFiles(absWorkingDir);
        WatchSubDirectories(absWorkingDir, RulesSubDirs, "*.md");
        WatchSubDirectories(absWorkingDir, CommandsSubDirs, "*.md");
        WatchAppDataConfigFiles(absWorkingDir);

        _logger?.LogInformation("[ConfigChangeNotifier] 已启动配置文件监控，工作目录: {Dir}，监控器数量: {Count}",
            absWorkingDir, _watchers.Count);
    }

    public void StopMonitoring()
    {
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[ConfigChangeNotifier] 停止监控器时出错");
            }
        }
        _watchers.Clear();

        _monitoredDirs.Clear();
    }

    /// <summary>
    /// 标记内部写入 — 对齐 TS markInternalWrite
    /// 在自身写入配置文件前调用，5s 窗口内该文件变更不触发 ConfigChanged 事件
    /// </summary>
    public void MarkInternalWrite(string filePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var watcher in _watchers)
        {
            watcher.MarkInternalWrite(filePath);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }

    private void WatchRootConfigFiles(string workingDir)
    {
        var currentDirPath = _fs.GetFullPath(workingDir);
        while (currentDirPath != null)
        {
            foreach (var fileName in RootConfigFiles)
            {
                var fullPath = Path.Combine(currentDirPath, fileName);
                if (_fs.FileExists(fullPath))
                {
                    TryCreateFileWatcher(currentDirPath, fileName);
                }
            }

            var codexAgentsPath = Path.Combine(currentDirPath, ".codex", "AGENTS.md");
            if (_fs.FileExists(codexAgentsPath))
            {
                var codexDir = Path.Combine(currentDirPath, ".codex");
                TryCreateFileWatcher(codexDir, "AGENTS.md");
            }

            currentDirPath = _fs.GetParentPath(currentDirPath);
        }
    }

    private void WatchSubDirectories(string workingDir, string[] subDirs, string filter)
    {
        var currentDirPath = _fs.GetFullPath(workingDir);
        while (currentDirPath != null)
        {
            foreach (var subDir in subDirs)
            {
                var fullPath = Path.Combine(currentDirPath, subDir);
                if (_fs.DirectoryExists(fullPath))
                {
                    TryCreateDirectoryWatcher(fullPath, filter);
                }
            }
            currentDirPath = _fs.GetParentPath(currentDirPath);
        }
    }

    private void WatchAppDataConfigFiles(string workingDir)
    {
        var currentDirPath = _fs.GetFullPath(workingDir);
        while (currentDirPath != null)
        {
            var appDataDir = Path.Combine(currentDirPath, AppDataConstants.AppDataFolder);
            if (_fs.DirectoryExists(appDataDir))
            {
                foreach (var configFile in AppDataConfigFiles)
                {
                    var fullPath = Path.Combine(appDataDir, configFile);
                    if (_fs.FileExists(fullPath))
                    {
                        TryCreateFileWatcher(appDataDir, configFile);
                    }
                }
            }
            currentDirPath = _fs.GetParentPath(currentDirPath);
        }
    }

    private void TryCreateFileWatcher(string directory, string fileName)
    {
        if (!_monitoredDirs.Add($"{directory}|{fileName}")) return;

        try
        {
            var watcher = _fs.Watch(directory, fileName);
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
            watcher.DebounceInterval = TimeSpan.FromMilliseconds(500);
            watcher.DebouncedChanged += OnFileChanged;
            watcher.DebouncedCreated += OnFileChanged;
            watcher.DebouncedDeleted += OnFileChanged;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ConfigChangeNotifier] 创建文件监控器失败: {Dir}/{File}", directory, fileName);
        }
    }

    private void TryCreateDirectoryWatcher(string directory, string filter)
    {
        if (!_monitoredDirs.Add($"{directory}|{filter}")) return;

        try
        {
            var watcher = _fs.Watch(directory, filter);
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime;
            watcher.DebounceInterval = TimeSpan.FromMilliseconds(500);
            watcher.DebouncedChanged += OnFileChanged;
            watcher.DebouncedCreated += OnFileChanged;
            watcher.DebouncedDeleted += OnFileChanged;
            watcher.DebouncedRenamed += OnFileRenamed;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ConfigChangeNotifier] 创建目录监控器失败: {Dir}/{Filter}", directory, filter);
        }
    }

    private void OnFileChanged(object? sender, FileChangedEventArgs e)
    {
        if (!IsConfigFile(e.FullPath)) return;
        RaiseConfigChanged(e.FullPath, e.ChangeType.ToString());
    }

    private void OnFileRenamed(object? sender, FileRenamedEventArgs e)
    {
        if (!IsConfigFile(e.FullPath)) return;
        RaiseConfigChanged(e.FullPath, "Renamed");
    }

    private void RaiseConfigChanged(string filePath, string changeType)
    {
        try
        {
            var args = new ConfigChangeEventArgs
            {
                FilePath = filePath,
                ChangeType = changeType,
                Timestamp = DateTimeOffset.Now
            };
            ConfigChanged?.Invoke(this, args);
            _logger?.LogDebug("[ConfigChangeNotifier] 配置文件变更通知: {Path} ({ChangeType})", filePath, changeType);
            RecordConfigChangeMetrics(changeType, true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ConfigChangeNotifier] 触发配置变更事件时出错: {Path}", filePath);
            RecordConfigChangeMetrics(changeType, false);
        }
    }

    private void RecordConfigChangeMetrics(string changeType, bool isSuccess)
        => _telemetryService?.RecordCount("config.change.count", new() { ["change_type"] = changeType, ["success"] = isSuccess.ToString() }, description: "Config change notification count");

    private bool IsConfigFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var ext = Path.GetExtension(filePath);

        if (string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase))
        {
            var dirName = Path.GetDirectoryName(filePath);
            if (dirName == null) return false;

            var parentName = _fs.GetDirectoryName(dirName);
            if (parentName.Equals("rules", StringComparison.OrdinalIgnoreCase)
                || parentName.Equals("commands", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var rootFile in RootConfigFiles)
            {
                if (string.Equals(fileName, rootFile, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var configFile in AppDataConfigFiles)
            {
                if (string.Equals(fileName, configFile, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
