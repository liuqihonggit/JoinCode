namespace Core.Configuration;

public sealed partial class ExternalRulesLoader
{
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<ExternalRulesLoader>? _logger;

    private static readonly string[] ProjectRulesDirs = [
        Path.Combine(".trae", "rules"),
        Path.Combine(".claude", "rules"),
        Path.Combine(".codex", "rules"),
        Path.Combine(AppDataConstants.AppDataFolder, AppDataConstants.RulesFolderName)
    ];

    private static readonly string[] UserRulesDirs = [
        Path.Combine(".codex", "rules")
    ];

    public ExternalRulesLoader(IFileSystem fs, ILogger<ExternalRulesLoader>? logger = null)
    {
        _fs = fs;
        _logger = logger;
    }

    public async Task<List<RuleFile>> LoadProjectRulesAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        var rules = new List<RuleFile>();
        var currentDirPath = _fs.GetFullPath(workingDirectory);

        while (currentDirPath != null)
        {
            // 并行扫描所有项目规则目录
            var dirTasks = new List<Task<List<RuleFile>>>();
            foreach (var rulesDir in ProjectRulesDirs)
            {
                var fullDirPath = Path.Combine(currentDirPath, rulesDir);
                if (_fs.DirectoryExists(fullDirPath))
                {
                    dirTasks.Add(LoadRulesFromDirectoryAsync(fullDirPath, cancellationToken));
                }
            }
            var dirResults = await Task.WhenAll(dirTasks).ConfigureAwait(false);
            foreach (var dirRules in dirResults)
            {
                rules.AddRange(dirRules);
            }

            currentDirPath = _fs.GetParentPath(currentDirPath);
        }

        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        // 并行扫描所有用户规则目录
        var userDirTasks = new List<Task<List<RuleFile>>>();
        foreach (var rulesDir in UserRulesDirs)
        {
            var fullDirPath = Path.Combine(appDataRoot, rulesDir);
            if (_fs.DirectoryExists(fullDirPath))
            {
                userDirTasks.Add(LoadRulesFromDirectoryAsync(fullDirPath, cancellationToken));
            }
        }
        var userDirResults = await Task.WhenAll(userDirTasks).ConfigureAwait(false);
        foreach (var dirRules in userDirResults)
        {
            rules.AddRange(dirRules);
        }

        return Deduplicate(rules);
    }

    public List<RuleFile> FilterAlwaysApply(List<RuleFile> rules)
    {
        return rules.Where(r => r.MatchStrategy == RuleMatchStrategy.Always).ToList();
    }

    public List<RuleFile> FilterByGlobs(List<RuleFile> rules, string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return [];

        var fileName = Path.GetFileName(filePath);
        var result = new List<RuleFile>();

        foreach (var rule in rules.Where(r => r.MatchStrategy == RuleMatchStrategy.Glob))
        {
            var patterns = rule.Globs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pattern in patterns)
            {
                if (MatchesGlobPattern(fileName, pattern) || MatchesGlobPattern(filePath, pattern))
                {
                    result.Add(rule);
                    break;
                }
            }
        }

        return result;
    }

    public List<RuleFile> FilterByDescription(List<RuleFile> rules)
    {
        return rules.Where(r => r.MatchStrategy == RuleMatchStrategy.Description).ToList();
    }

    private async Task<List<RuleFile>> LoadRulesFromDirectoryAsync(string directoryPath, CancellationToken cancellationToken)
    {
        var rules = new List<RuleFile>();

        try
        {
            await LoadRulesRecursiveAsync(directoryPath, directoryPath, rules, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "扫描规则目录失败: {Path}", directoryPath);
        }

        return rules;
    }

    private async Task LoadRulesRecursiveAsync(string baseDirPath, string currentDirPath, List<RuleFile> rules, CancellationToken cancellationToken)
    {
        try
        {
            var mdFiles = _fs.GetFiles(currentDirPath, "*.md", SearchOption.TopDirectoryOnly);

            // 并行读取所有 md 文件
            var readTasks = new List<Task<RuleFile?>>();
            foreach (var filePath in mdFiles)
            {
                if (rules.Exists(r => r.SourcePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))) continue;
                readTasks.Add(TryReadRuleFileAsync(baseDirPath, filePath, cancellationToken));
            }
            var readResults = await Task.WhenAll(readTasks).ConfigureAwait(false);
            foreach (var rule in readResults)
            {
                if (rule is not null)
                {
                    rules.Add(rule);
                    _logger?.LogInformation("已加载规则: {Name} ({Strategy}) [{Path}]", rule.Name, rule.MatchStrategy, rule.SourcePath);
                }
            }

            // 并行扫描子目录
            var subDirs = _fs.GetDirectories(currentDirPath, "*", SearchOption.TopDirectoryOnly);
            if (subDirs.Length > 0)
            {
                var subDirTasks = new List<Task>();
                foreach (var subDir in subDirs)
                {
                    subDirTasks.Add(LoadRulesRecursiveAsync(baseDirPath, subDir, rules, cancellationToken));
                }
                await Task.WhenAll(subDirTasks).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "扫描规则目录失败: {Path}", currentDirPath);
        }
    }

    private async Task<RuleFile?> TryReadRuleFileAsync(string baseDirPath, string filePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!_fs.FileExists(filePath)) return null;
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content)) return null;

            var (body, alwaysApply, globs, description) = RuleFrontmatterParser.Parse(content!);
            var relativePath = filePath.Length > baseDirPath.Length + 1
                ? filePath[(baseDirPath.Length + 1)..]
                : Path.GetFileName(filePath);
            var name = Path.GetFileNameWithoutExtension(relativePath);

            return new RuleFile
            {
                Name = name,
                Content = body.Trim(),
                SourcePath = filePath,
                AlwaysApply = alwaysApply,
                Globs = globs,
                Description = description
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取规则文件失败: {Path}", filePath);
            return null;
        }
    }

    internal static bool MatchesGlobPattern(string input, string pattern)
        => GlobMatcher.IsMatch(input, pattern);

    private static List<RuleFile> Deduplicate(List<RuleFile> rules)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RuleFile>();

        foreach (var rule in rules)
        {
            if (seen.Add(rule.SourcePath))
            {
                result.Add(rule);
            }
        }

        return result;
    }
}
