namespace Core.Configuration;

/// <summary>
/// 配置变更通知器 Actor — 基于 FileWatcherActorBase,单 watcher 监控配置文件变更。
/// <para>向上遍历找到最顶层有配置文件的目录,一个 watcher + IncludeSubdirectories=true 覆盖所有配置文件。</para>
/// <para>死循环防护: MarkInternalWrite 继承基类,通过 Actor 邮箱串行化,无窗口竞态。</para>
/// </summary>
[Register(typeof(IConfigChangeNotifier), ServiceLifetime.Singleton)]
public sealed partial class ConfigChangeNotifier : FileWatcherActorBase, IConfigChangeNotifier
{
    private readonly ILogger<ConfigChangeNotifier>? _logger;
    private readonly ITelemetryService? _telemetryService;

    public ConfigChangeNotifier(IFileSystem fs, ILogger<ConfigChangeNotifier>? logger = null, ITelemetryService? telemetryService = null)
        : base(fs, 1000)
    {
        _logger = logger;
        _telemetryService = telemetryService;
    }

    private static readonly string[] RootConfigFiles =
    new[] {
        "AGENTS.md", "agents.md",
        ClaudeCompatConstants.ProjectRulesFileName, ClaudeCompatConstants.ProjectRulesFileNameLower,
        ClaudeCompatConstants.ProjectRulesLocalFileName, ClaudeCompatConstants.ProjectRulesLocalFileNameLower,
        "codex.md"
     };

    private static readonly string[] RulesSubDirs =
    new[] {
        Path.Combine(".trae", "rules"),
        Path.Combine(ClaudeCompatConstants.ConfigDirectory, "rules"),
        Path.Combine(".codex", "rules"),
        Path.Combine(AppDataConstants.AppDataFolder, AppDataConstants.RulesFolderName)
     };

    private static readonly string[] CommandsSubDirs =
    new[] {
        Path.Combine(".trae", "commands"),
        Path.Combine(".claude", "commands"),
        Path.Combine(".codex", "commands"),
        Path.Combine(AppDataConstants.AppDataFolder, AppDataConstants.CommandsFolderName)
     };

    private static readonly string[] AppDataConfigFiles =
    new[] {
        AppDataConstants.SettingsFileName,
        AppDataConstants.AuthFileName
     };

    public event EventHandler<ConfigChangeEventArgs>? ConfigChanged;

    private sealed record StartMonitoringCmd(string WorkingDirectory) : FileWatcherCommand;

    /// <summary>启动配置文件监控 — 投递命令到 Actor 邮箱,Consumer 串行处理</summary>
    public void StartMonitoring(string workingDirectory)
        => TrySend(new StartMonitoringCmd(workingDirectory));

    /// <summary>停止配置文件监控 — 投递停止命令到 Actor 邮箱</summary>
    public void StopMonitoring()
        => TrySend(new FileWatcherStopCmd());

    /// <summary>文件变更处理 — IsConfigFile 过滤后触发 ConfigChanged 事件</summary>
    protected override ValueTask HandleFileChangedAsync(string filePath, WatcherChangeTypes kind, DateTimeOffset timestamp, CancellationToken ct)
    {
        if (!IsConfigFile(filePath)) return ValueTask.CompletedTask;
        RaiseConfigChanged(filePath, kind.ToString());
        return ValueTask.CompletedTask;
    }

    /// <summary>文件重命名处理 — IsConfigFile 过滤后触发 ConfigChanged 事件</summary>
    protected override ValueTask HandleFileRenamedAsync(string oldPath, string newPath, DateTimeOffset timestamp, CancellationToken ct)
    {
        if (!IsConfigFile(newPath)) return ValueTask.CompletedTask;
        RaiseConfigChanged(newPath, "Renamed");
        return ValueTask.CompletedTask;
    }

    /// <summary>自定义命令处理 — StartMonitoringCmd 启动监控</summary>
    protected override async ValueTask HandleCustomCommandAsync(FileWatcherCommand cmd, CancellationToken ct)
    {
        if (cmd is not StartMonitoringCmd start) return;

        TrySend(new FileWatcherStopCmd());
        var watchRoot = FindWatchRoot(start.WorkingDirectory);
        await SendAsync(new FileWatcherStartCmd(
            watchRoot, "*.*", TimeSpan.FromMilliseconds(500),
            IncludeSubdirectories: true,
            NotifyFilter: NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime
        ), ct).ConfigureAwait(false);
        _logger?.LogInformation("[ConfigChangeNotifier] 已启动配置文件监控,监控根: {Root}", watchRoot);
    }

    /// <summary>向上遍历找到最顶层有配置文件的目录 — 从该目录向下监控覆盖所有配置文件</summary>
    private string FindWatchRoot(string workingDirectory)
    {
        var current = FileSystem.GetFullPath(workingDirectory);
        var topMost = current;
        while (current != null)
        {
            if (HasConfigFiles(current))
                topMost = current;
            current = FileSystem.GetParentPath(current);
        }
        return topMost;
    }

    private bool HasConfigFiles(string dir)
    {
        foreach (var fileName in RootConfigFiles)
            if (FileSystem.FileExists(Path.Combine(dir, fileName))) return true;

        var codexAgentsPath = Path.Combine(dir, ".codex", "AGENTS.md");
        if (FileSystem.FileExists(codexAgentsPath)) return true;

        foreach (var subDir in RulesSubDirs)
            if (FileSystem.DirectoryExists(Path.Combine(dir, subDir))) return true;

        foreach (var subDir in CommandsSubDirs)
            if (FileSystem.DirectoryExists(Path.Combine(dir, subDir))) return true;

        var appDataDir = Path.Combine(dir, AppDataConstants.AppDataFolder);
        if (FileSystem.DirectoryExists(appDataDir))
            foreach (var configFile in AppDataConfigFiles)
                if (FileSystem.FileExists(Path.Combine(appDataDir, configFile))) return true;

        return false;
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

            var parentName = FileSystem.GetDirectoryName(dirName);
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
