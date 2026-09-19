namespace Core.Configuration;

/// <summary>
/// 外部规则加载器 — 从项目目录(.trae/.claude/.codex/rules)和用户目录加载 markdown 规则文件
/// </summary>
public sealed partial class ExternalRulesLoader {
    private readonly IFileSystem _fs;
    private readonly ILogger<ExternalRulesLoader>? _logger;

    private static readonly string[] ProjectRulesDirs = new[] {
        Path.Combine(".trae", "rules"),
        Path.Combine(".claude", "rules"),
        Path.Combine(".codex", "rules"),
        Path.Combine(AppDataConstants.AppDataFolder, AppDataConstants.RulesFolderName)
     };

    private static readonly string[] UserRulesDirs = new[] {
        Path.Combine(".codex", "rules")
     };

    /// <summary>
    /// 初始化外部规则加载器
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="logger">可选的日志记录器</param>
    public ExternalRulesLoader(IFileSystem fs, ILogger<ExternalRulesLoader>? logger = null) {
        _fs = fs;
        _logger = logger;
    }

    /// <summary>
    /// 加载项目级规则 — 从工作目录向上逐级扫描各项目规则目录及用户规则目录
    /// </summary>
    /// <param name="workingDirectory">工作目录起点</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>去重后的规则文件列表</returns>
    public async Task<List<RuleFile>> LoadProjectRulesAsync(string workingDirectory, CancellationToken cancellationToken = default) {
        var rules = new Dictionary<string, RuleFile>(StringComparer.OrdinalIgnoreCase);
        var currentDirPath = _fs.GetFullPath(workingDirectory);

        while (currentDirPath != null) {
            // 并行扫描所有项目规则目录
            var dirTasks = new List<Task<Dictionary<string, RuleFile>>>();
            foreach (var rulesDir in ProjectRulesDirs) {
                var fullDirPath = Path.Combine(currentDirPath, rulesDir);
                if (_fs.DirectoryExists(fullDirPath)) {
                    dirTasks.Add(LoadRulesFromDirectoryAsync(fullDirPath, cancellationToken));
                }
            }
            var dirResults = await Task.WhenAll(dirTasks).ConfigureAwait(false);
            foreach (var dirRules in dirResults) {
                foreach (var kvp in dirRules) {
                    rules.TryAdd(kvp.Key, kvp.Value);
                }
            }

            currentDirPath = _fs.GetParentPath(currentDirPath);
        }

        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // 并行扫描所有用户规则目录
        var userDirTasks = new List<Task<Dictionary<string, RuleFile>>>();
        foreach (var rulesDir in UserRulesDirs) {
            var fullDirPath = Path.Combine(appDataRoot, rulesDir);
            if (_fs.DirectoryExists(fullDirPath)) {
                userDirTasks.Add(LoadRulesFromDirectoryAsync(fullDirPath, cancellationToken));
            }
        }
        var userDirResults = await Task.WhenAll(userDirTasks).ConfigureAwait(false);
        foreach (var dirRules in userDirResults) {
            foreach (var kvp in dirRules) {
                rules.TryAdd(kvp.Key, kvp.Value);
            }
        }

        return rules.Values.ToList();
    }

    /// <summary>
    /// 筛选 AlwaysApply 匹配策略的规则
    /// </summary>
    /// <param name="rules">规则列表</param>
    /// <returns>匹配策略为 Always 的规则列表</returns>
    public List<RuleFile> FilterAlwaysApply(List<RuleFile> rules) {
        return rules.Where(r => r.MatchStrategy == RuleMatchStrategy.Always).ToList();
    }

    /// <summary>
    /// 按 glob 模式筛选规则 — 匹配文件名或完整路径
    /// </summary>
    /// <param name="rules">规则列表</param>
    /// <param name="filePath">要匹配的文件路径</param>
    /// <returns>匹配的规则列表</returns>
    public List<RuleFile> FilterByGlobs(List<RuleFile> rules, string filePath) {
        if (string.IsNullOrEmpty(filePath)) return [];

        var fileName = Path.GetFileName(filePath);
        var result = new List<RuleFile>();

        foreach (var rule in rules.Where(r => r.MatchStrategy == RuleMatchStrategy.Glob)) {
            var patterns = rule.Globs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pattern in patterns) {
                if (MatchesGlobPattern(fileName, pattern) || MatchesGlobPattern(filePath, pattern)) {
                    result.Add(rule);
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 筛选 Description 匹配策略的规则
    /// </summary>
    /// <param name="rules">规则列表</param>
    /// <returns>匹配策略为 Description 的规则列表</returns>
    public List<RuleFile> FilterByDescription(List<RuleFile> rules) {
        return rules.Where(r => r.MatchStrategy == RuleMatchStrategy.Description).ToList();
    }

    private async Task<Dictionary<string, RuleFile>> LoadRulesFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken) {
        var rules = new Dictionary<string, RuleFile>(StringComparer.OrdinalIgnoreCase);

        try {
            await LoadRulesRecursiveAsync(directoryPath, directoryPath, rules, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "扫描规则目录失败: {Path}", directoryPath);
        }

        return rules;
    }

    private async Task LoadRulesRecursiveAsync(string baseDirPath, string currentDirPath, Dictionary<string, RuleFile> rules, CancellationToken cancellationToken) {
        try {
            var mdFiles = _fs.GetFiles(currentDirPath, "*.md", SearchOption.TopDirectoryOnly);

            // 并行读取所有 md 文件
            var readTasks = new List<Task<RuleFile?>>();
            foreach (var filePath in mdFiles) {
                if (rules.ContainsKey(filePath)) continue;
                readTasks.Add(TryReadRuleFileAsync(baseDirPath, filePath, cancellationToken));
            }
            var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);
            foreach (var rule in readResults) {
                if (rule is not null) {
                    rules[rule.SourcePath] = rule;
                    _logger?.LogInformation("已加载规则: {Name} ({Strategy}) [{Path}]", rule.Name, rule.MatchStrategy, rule.SourcePath);
                }
            }

            // 并行扫描子目录
            var subDirs = _fs.GetDirectories(currentDirPath, "*", SearchOption.TopDirectoryOnly);
            if (subDirs.Length > 0) {
                var subDirTasks = new List<Task>();
                foreach (var subDir in subDirs) {
                    subDirTasks.Add(LoadRulesRecursiveAsync(baseDirPath, subDir, rules, cancellationToken));
                }
                await Task.WhenAll(subDirTasks).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "扫描规则目录失败: {Path}", currentDirPath);
        }
    }

    private async Task<RuleFile?> TryReadRuleFileAsync(string baseDirPath, string filePath, CancellationToken cancellationToken) {
        try {
            if (!_fs.FileExists(filePath)) return null;
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var (body, alwaysApply, globs, description) = RuleFrontmatterParser.Parse(content ?? string.Empty);
            var relativePath = filePath.Length > baseDirPath.Length + 1
                ? filePath[(baseDirPath.Length + 1)..]
                : Path.GetFileName(filePath);
            var name = Path.GetFileNameWithoutExtension(relativePath);

            return new RuleFile {
                Name = name,
                Content = body.Trim(),
                SourcePath = filePath,
                AlwaysApply = alwaysApply,
                Globs = globs,
                Description = description
            };
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "读取规则文件失败: {Path}", filePath);
            return null;
        }
    }

    internal static bool MatchesGlobPattern(string input, string pattern)
        => GlobMatcher.IsMatch(input, pattern);

    private static List<RuleFile> Deduplicate(List<RuleFile> rules) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RuleFile>();

        foreach (var rule in rules) {
            if (seen.Add(rule.SourcePath)) {
                result.Add(rule);
            }
        }

        return result;
    }
}