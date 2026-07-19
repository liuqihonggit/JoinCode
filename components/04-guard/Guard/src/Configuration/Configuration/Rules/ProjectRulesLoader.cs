
namespace Core.Configuration;

[Register]
public sealed partial class ProjectRulesLoader {
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<ProjectRulesLoader>? _logger;
    private readonly ITelemetryService? _telemetryService;

    private static string[] GetRulesFilePaths() => [
        Path.Combine(AppDataConstants.AppDataFolder, AppDataConstants.RulesFolderName, AppDataConstants.ProjectRulesFileName),
        "AGENTS.md",
        "agents.md",
        "CLAUDE.md",
        "claude.md",
        "CLAUDE.local.md",
        "claude.local.md",
        "codex.md",
        Path.Combine(".codex", "AGENTS.md")
    ];

    private static string[] GetRulesDirectoryPaths() => [
        Path.Combine(".trae", "rules"),
        Path.Combine(".claude", "rules"),
        Path.Combine(".codex", "rules"),
        Path.Combine(AppDataConstants.AppDataFolder, AppDataConstants.RulesFolderName)
    ];

    private static readonly string[] UserRulesFilePaths = [
        Path.Combine(".codex", "instructions.md"),
        Path.Combine(".codex", "AGENTS.md")
    ];

    private static readonly string[] UserRulesDirectoryPaths = [
        Path.Combine(".codex", "rules")
    ];

    public ProjectRulesLoader(
        IFileSystem fs,
        ILogger<ProjectRulesLoader>? logger = null,
        ITelemetryService? telemetryService = null) {
        _fs = fs;
        _logger = logger;
        _telemetryService = telemetryService;
    }

    public async Task<string?> LoadRulesAsync(string? workingDirectory = null, CancellationToken cancellationToken = default) {
        var basePath = workingDirectory ?? _fs.GetCurrentDirectory();
        var result = await LoadRulesFromDirectoryAsync(basePath, cancellationToken).ConfigureAwait(false);
        _telemetryService?.RecordCount("rules.loader.count", new() { ["operation"] = "load", ["found"] = (result != null).ToString() }, description: "Project rules loader count");
        return result;
    }

    private async Task<string?> LoadRulesFromDirectoryAsync(string startDirectory, CancellationToken cancellationToken) {
        var foundFiles = new List<(string Path, string Content)>();
        var currentDirPath = _fs.GetFullPath(startDirectory);

        while (currentDirPath != null) {
            // 并行读取所有规则文件
            var readTasks = new List<Task<(string Path, string? Content)?>>();
            foreach (var relativePath in GetRulesFilePaths()) {
                var fullPath = Path.Combine(currentDirPath, relativePath);
                if (_fs.FileExists(fullPath)) {
                    readTasks.Add(TryReadFileAsync(fullPath, cancellationToken));
                }
            }
            var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);
            foreach (var result in readResults) {
                if (result is not null && result.Value.Content != null && !foundFiles.Exists(f => f.Path.Equals(result.Value.Path, StringComparison.OrdinalIgnoreCase))) {
                    foundFiles.Add((result.Value.Path, result.Value.Content));
                }
            }

            // 并行扫描所有规则目录
            var dirTasks = new List<Task<List<(string Path, string Content)>>>();
            foreach (var rulesDir in GetRulesDirectoryPaths()) {
                var fullDirPath = Path.Combine(currentDirPath, rulesDir);
                if (_fs.DirectoryExists(fullDirPath)) {
                    dirTasks.Add(LoadRulesFromDirectoryRecursiveAsync(fullDirPath, fullDirPath, cancellationToken));
                }
            }
            var dirResults = await Task.WhenAll(dirTasks).ConfigureAwait(false);
            foreach (var dirFiles in dirResults) {
                foreach (var file in dirFiles) {
                    if (!foundFiles.Exists(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase))) {
                        foundFiles.Add(file);
                    }
                }
            }

            currentDirPath = _fs.GetParentPath(currentDirPath);
        }

        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 并行读取用户规则文件
        var userReadTasks = new List<Task<(string Path, string? Content)?>>();
        foreach (var relativePath in UserRulesFilePaths) {
            var fullPath = Path.Combine(appDataRoot, relativePath);
            if (_fs.FileExists(fullPath)) {
                userReadTasks.Add(TryReadFileAsync(fullPath, cancellationToken));
            }
        }
        var userReadResults = await Task.WhenAll(userReadTasks).ConfigureAwait(false);
        foreach (var result in userReadResults) {
            if (result is not null && result.Value.Content != null && !foundFiles.Exists(f => f.Path.Equals(result.Value.Path, StringComparison.OrdinalIgnoreCase))) {
                foundFiles.Add((result.Value.Path, result.Value.Content));
                _logger?.LogInformation("已加载用户规则文件: {Path}", result.Value.Path);
            }
        }

        // 并行扫描用户规则目录
        var userDirTasks = new List<Task<List<(string Path, string Content)>>>();
        foreach (var rulesDir in UserRulesDirectoryPaths) {
            var fullDirPath = Path.Combine(appDataRoot, rulesDir);
            if (_fs.DirectoryExists(fullDirPath)) {
                userDirTasks.Add(LoadRulesFromDirectoryRecursiveAsync(fullDirPath, fullDirPath, cancellationToken));
            }
        }
        var userDirResults = await Task.WhenAll(userDirTasks).ConfigureAwait(false);
        foreach (var dirFiles in userDirResults) {
            foreach (var file in dirFiles) {
                if (!foundFiles.Exists(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase))) {
                    foundFiles.Add(file);
                }
            }
        }

        if (foundFiles.Count == 0) {
            _logger?.LogDebug("未找到项目规则文件");
            return null;
        }

        if (foundFiles.Count == 1) {
            return foundFiles[0].Content;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var (path, content) in foundFiles) {
            var fileName = Path.GetFileName(path);
            sb.AppendLine($"<!-- 来源: {fileName} -->");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<(string Path, string? Content)?> TryReadFileAsync(string fullPath, CancellationToken cancellationToken) {
        try {
            if (!_fs.FileExists(fullPath)) return null;
            var content = await _fs.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return (fullPath, content);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "读取规则文件失败: {Path}", fullPath);
            return null;
        }
    }

    private async Task<List<(string Path, string Content)>> LoadRulesFromDirectoryRecursiveAsync(string baseDirPath, string currentDirPath, CancellationToken cancellationToken) {
        var foundFiles = new List<(string Path, string Content)>();
        try {
            var mdFiles = _fs.GetFiles(currentDirPath, "*.md", SearchOption.TopDirectoryOnly);

            // 并行读取所有 md 文件
            var readTasks = new List<Task<(string Path, string? Content)?>>();
            foreach (var filePath in mdFiles) {
                readTasks.Add(TryReadRuleFileAsync(filePath, cancellationToken));
            }
            var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);
            foreach (var result in readResults) {
                if (result is not null && result.Value.Content != null && !string.IsNullOrWhiteSpace(result.Value.Content)) {
                    foundFiles.Add((result.Value.Path, result.Value.Content));
                    _logger?.LogInformation("已加载规则文件: {Path}", result.Value.Path);
                }
            }

            // 并行扫描子目录
            var subDirs = _fs.GetDirectories(currentDirPath, "*", SearchOption.TopDirectoryOnly);
            if (subDirs.Length > 0) {
                var subDirTasks = new List<Task<List<(string Path, string Content)>>>();
                foreach (var subDir in subDirs) {
                    subDirTasks.Add(LoadRulesFromDirectoryRecursiveAsync(baseDirPath, subDir, cancellationToken));
                }
                var subDirResults = await Task.WhenAll(subDirTasks).ConfigureAwait(false);
                foreach (var subFiles in subDirResults) {
                    foundFiles.AddRange(subFiles);
                }
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "扫描规则目录失败: {Path}", currentDirPath);
        }

        return foundFiles;
    }

    private async Task<(string Path, string? Content)?> TryReadRuleFileAsync(string filePath, CancellationToken cancellationToken) {
        try {
            if (!_fs.FileExists(filePath)) return null;
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return (filePath, content);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "读取规则文件失败: {Path}", filePath);
            return null;
        }
    }

    public bool HasRulesFile(string? workingDirectory = null) {
        var basePath = workingDirectory ?? _fs.GetCurrentDirectory();
        var currentDirPath = _fs.GetFullPath(basePath);

        while (currentDirPath != null) {
            foreach (var relativePath in GetRulesFilePaths()) {
                var fullPath = Path.Combine(currentDirPath, relativePath);
                if (_fs.FileExists(fullPath)) {
                    return true;
                }
            }

            foreach (var rulesDir in GetRulesDirectoryPaths()) {
                var fullDirPath = Path.Combine(currentDirPath, rulesDir);
                if (_fs.DirectoryExists(fullDirPath) && _fs.GetFiles(fullDirPath, "*.md", SearchOption.AllDirectories).Length > 0) {
                    return true;
                }
            }

            currentDirPath = _fs.GetParentPath(currentDirPath);
        }

        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var relativePath in UserRulesFilePaths) {
            var fullPath = Path.Combine(appDataRoot, relativePath);
            if (_fs.FileExists(fullPath)) {
                return true;
            }
        }

        foreach (var rulesDir in UserRulesDirectoryPaths) {
            var fullDirPath = Path.Combine(appDataRoot, rulesDir);
            if (_fs.DirectoryExists(fullDirPath) && _fs.GetFiles(fullDirPath, "*.md", SearchOption.AllDirectories).Length > 0) {
                return true;
            }
        }

        return false;
    }

    public string? GetRulesFilePath(string? workingDirectory = null) {
        var basePath = workingDirectory ?? _fs.GetCurrentDirectory();
        var currentDirPath = _fs.GetFullPath(basePath);

        while (currentDirPath != null) {
            foreach (var relativePath in GetRulesFilePaths()) {
                var fullPath = Path.Combine(currentDirPath, relativePath);
                if (_fs.FileExists(fullPath)) {
                    return fullPath;
                }
            }

            foreach (var rulesDir in GetRulesDirectoryPaths()) {
                var fullDirPath = Path.Combine(currentDirPath, rulesDir);
                if (_fs.DirectoryExists(fullDirPath)) {
                    var mdFiles = _fs.GetFiles(fullDirPath, "*.md", SearchOption.AllDirectories);
                    if (mdFiles.Length > 0) {
                        return mdFiles[0];
                    }
                }
            }

            currentDirPath = _fs.GetParentPath(currentDirPath);
        }

        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var relativePath in UserRulesFilePaths) {
            var fullPath = Path.Combine(appDataRoot, relativePath);
            if (_fs.FileExists(fullPath)) {
                return fullPath;
            }
        }

        foreach (var rulesDir in UserRulesDirectoryPaths) {
            var fullDirPath = Path.Combine(appDataRoot, rulesDir);
            if (_fs.DirectoryExists(fullDirPath)) {
                    var mdFiles = _fs.GetFiles(fullDirPath, "*.md", SearchOption.AllDirectories);
                if (mdFiles.Length > 0) {
                    return mdFiles[0];
                }
            }
        }

        return null;
    }

}
