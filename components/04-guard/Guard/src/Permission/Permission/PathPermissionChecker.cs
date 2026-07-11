using JoinCode.Abstractions.Attributes;

namespace Core.Permission;

/// <summary>
/// 路径级权限检查器 — 对齐 TS checkReadPermissionForTool 9步决策链
/// 检查文件读写操作的路径级权限（工作目录、规则匹配、内部路径白名单等）
/// </summary>
[Register]
public sealed partial class PathPermissionChecker : IPathPermissionChecker
{
    private readonly string _workingDirectory;
    private readonly string[] _resolvedAdditionalDirectories;
    private readonly IReadOnlyList<PathPermissionRule> _rules;
    private readonly IFileSystem _fs;
    [Inject] private readonly ILogger<PathPermissionChecker>? _logger;
    private readonly string _appDataRoot;

    /// <summary>
    /// 测试用构造函数 — 直接传入 PathPermissionRule 列表
    /// </summary>
    internal PathPermissionChecker(
        IFileSystem fs,
        string workingDirectory,
        IReadOnlyList<string>? additionalDirectories = null,
        IReadOnlyList<PathPermissionRule>? rules = null,
        ILogger<PathPermissionChecker>? logger = null)
    {
        _fs = fs;
        _workingDirectory = NormalizePath(Path.GetFullPath(workingDirectory ?? _fs.GetCurrentDirectory()));
        _rules = rules ?? [];
        _logger = logger;

        _resolvedAdditionalDirectories = additionalDirectories is { Count: > 0 }
            ? additionalDirectories
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => NormalizePath(Path.GetFullPath(d)))
                .ToArray()
            : [];

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _appDataRoot = Path.Combine(homeDir, AppDataConstants.AppDataFolder);
    }

    /// <summary>
    /// DI 构造函数 — 从 IOptions&lt;PermissionConfig&gt; 推导 workingDirectory、additionalDirectories、rules
    /// </summary>
    public PathPermissionChecker(
        IFileSystem fs,
        IOptions<PermissionConfig> configOptions,
        ILogger<PathPermissionChecker>? logger = null)
    {
        _fs = fs;
        _workingDirectory = NormalizePath(Path.GetFullPath(fs.GetCurrentDirectory()));
        var config = configOptions.Value;
        _rules = BuildPathPermissionRules(config);
        _logger = logger;

        _resolvedAdditionalDirectories = config.AdditionalDirectories is { Count: > 0 }
            ? config.AdditionalDirectories
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => NormalizePath(Path.GetFullPath(d)))
                .ToArray()
            : [];

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _appDataRoot = Path.Combine(homeDir, AppDataConstants.AppDataFolder);
    }

    /// <summary>
    /// 从 PermissionConfig 构建 PathPermissionRule 列表
    /// 将 ToolPermissionRule 中的 Read/Edit 规则转换为 PathPermissionRule
    /// </summary>
    internal static List<PathPermissionRule> BuildPathPermissionRules(PermissionConfig config)
    {
        var rules = new List<PathPermissionRule>();

        // 从 AutoRejectedTools 提取路径级 deny 规则
        BuildPathRulesFromToolRules(config.AutoRejectedTools, PermissionBehavior.Deny, rules);

        // 从 AskRules 提取路径级 ask 规则
        BuildPathRulesFromToolRules(config.AskRules, PermissionBehavior.Ask, rules);

        // 从 AutoApprovedTools 提取路径级 allow 规则
        BuildPathRulesFromToolRules(config.AutoApprovedTools, PermissionBehavior.Allow, rules);

        return rules;
    }

    /// <summary>
    /// 从 ToolPermissionRule 列表提取路径级规则
    /// 格式: "Read(/path/**)" → PathPermissionRule { ToolType=Read, Pattern="/path/**", Behavior=Deny }
    /// </summary>
    private static void BuildPathRulesFromToolRules(
        List<ToolPermissionRule> toolRules,
        PermissionBehavior behavior,
        List<PathPermissionRule> pathRules)
    {
        for (var i = 0; i < toolRules.Count; i++)
        {
            var rule = toolRules[i];
            if (string.IsNullOrEmpty(rule.RuleContent))
                continue;

            // 判断工具类型
            var toolType = GetToolTypeFromName(rule.ToolName);
            if (toolType is null)
                continue;

            // RuleContent 格式: "/path/**" 或 "domain:example.com"
            // 跳过 WebFetch 的 domain: 格式（已有专门处理）
            if (rule.RuleContent.StartsWith("domain:", StringComparison.OrdinalIgnoreCase))
                continue;

            pathRules.Add(new PathPermissionRule
            {
                ToolType = toolType.Value,
                Behavior = behavior,
                Pattern = rule.RuleContent,
                Source = PathPermissionRuleSource.UserSettings
            });
        }
    }

    /// <summary>
    /// 从工具名推断路径权限工具类型
    /// </summary>
    private static PathPermissionToolType? GetToolTypeFromName(string toolName)
    {
        if (string.Equals(toolName, FileToolNameConstants.FileRead, StringComparison.OrdinalIgnoreCase))
            return PathPermissionToolType.Read;

        if (string.Equals(toolName, FileToolNameConstants.FileWrite, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, FileToolNameConstants.FileEdit, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, FileToolNameConstants.FileEditRegex, StringComparison.OrdinalIgnoreCase))
            return PathPermissionToolType.Edit;

        return null;
    }

    /// <summary>
    /// 检查读取权限 — 对齐 TS checkReadPermissionForTool 9步决策链
    /// </summary>
    public PathPermissionCheckResult CheckReadPermission(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var normalizedPath = NormalizePath(fullPath);

        // 步骤1: UNC 路径防御 — 对齐 TS: UNC path detected
        if (IsUncPath(normalizedPath))
        {
            return PathPermissionCheckResult.Ask(
                $"读取路径 {path} 是 UNC 路径，可能访问网络资源，需要手动批准。");
        }

        // 步骤2: 可疑 Windows 路径模式 — 对齐 TS: suspicious Windows path pattern
        if (SecurityPatterns.HasSuspiciousWindowsPathPattern(normalizedPath))
        {
            return PathPermissionCheckResult.Ask(
                $"读取路径 {path} 包含可疑的 Windows 路径模式，需要手动验证。");
        }

        // 步骤3: Read deny 规则 — 必须在 allow 之前，防止显式 deny 被隐式 allow 绕过
        var denyRule = MatchRule(normalizedPath, PathPermissionToolType.Read, PermissionBehavior.Deny);
        if (denyRule is not null)
        {
            _logger?.LogDebug("读取权限被 deny 规则拒绝: {Pattern}", denyRule.Pattern);
            return PathPermissionCheckResult.Deny(
                $"读取 {path} 的权限已被拒绝。", denyRule);
        }

        // 步骤4: Read ask 规则 — 必须在隐式 allow 之前
        var askRule = MatchRule(normalizedPath, PathPermissionToolType.Read, PermissionBehavior.Ask);
        if (askRule is not null)
        {
            return PathPermissionCheckResult.Ask(
                $"读取路径 {path} 需要用户确认。", askRule);
        }

        // 步骤5: 编辑权限隐含读取权限 — 对齐 TS: edit access implies read access
        // 调用完整 CheckWritePermission 决策链，而非仅检查 Edit allow 规则
        var writeResult = CheckWritePermissionInternal(normalizedPath);
        if (writeResult.Decision == PermissionBehavior.Allow)
        {
            _logger?.LogDebug("读取权限由编辑权限隐含允许");
            return PathPermissionCheckResult.Allow("编辑权限隐含读取权限");
        }

        // 步骤6: 工作目录内读取 — 对齐 TS: pathInAllowedWorkingPath
        if (IsInWorkingDirectory(normalizedPath))
        {
            return PathPermissionCheckResult.Allow("路径在工作目录内");
        }

        // 步骤7: 内部可读路径白名单 — 对齐 TS: checkReadableInternalPath
        var internalResult = CheckInternalReadablePath(normalizedPath);
        if (internalResult is not null)
            return internalResult;

        // 步骤8: Read allow 规则
        var allowRule = MatchRule(normalizedPath, PathPermissionToolType.Read, PermissionBehavior.Allow);
        if (allowRule is not null)
        {
            return PathPermissionCheckResult.Allow($"匹配允许规则: {allowRule.Pattern}", allowRule);
        }

        // 步骤9: 默认 — 工作目录外需要确认
        return PathPermissionCheckResult.Ask(
            $"读取路径 {path} 在允许的工作目录之外，需要用户确认。");
    }

    /// <summary>
    /// 检查写入权限 — 对齐 TS checkWritePermissionForTool
    /// </summary>
    public PathPermissionCheckResult CheckWritePermission(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var normalizedPath = NormalizePath(fullPath);

        return CheckWritePermissionInternal(normalizedPath);
    }

    /// <summary>
    /// 写入权限检查内部实现 — 接受已规范化的路径
    /// </summary>
    private PathPermissionCheckResult CheckWritePermissionInternal(string normalizedPath)
    {
        // 步骤1: UNC 路径防御
        if (IsUncPath(normalizedPath))
        {
            return PathPermissionCheckResult.Ask(
                "写入路径是 UNC 路径，可能访问网络资源，需要手动批准。");
        }

        // 步骤2: 可疑 Windows 路径模式
        if (SecurityPatterns.HasSuspiciousWindowsPathPattern(normalizedPath))
        {
            return PathPermissionCheckResult.Ask(
                "写入路径包含可疑的 Windows 路径模式，需要手动验证。");
        }

        // 步骤3: Edit deny 规则
        var denyRule = MatchRule(normalizedPath, PathPermissionToolType.Edit, PermissionBehavior.Deny);
        if (denyRule is not null)
        {
            return PathPermissionCheckResult.Deny(
                "写入权限已被拒绝。", denyRule);
        }

        // 步骤4: Edit ask 规则
        var askRule = MatchRule(normalizedPath, PathPermissionToolType.Edit, PermissionBehavior.Ask);
        if (askRule is not null)
        {
            return PathPermissionCheckResult.Ask(
                "写入路径需要用户确认。", askRule);
        }

        // 步骤5: 工作目录内写入
        if (IsInWorkingDirectory(normalizedPath))
        {
            return PathPermissionCheckResult.Allow("路径在工作目录内");
        }

        // 步骤6: Edit allow 规则
        var allowRule = MatchRule(normalizedPath, PathPermissionToolType.Edit, PermissionBehavior.Allow);
        if (allowRule is not null)
        {
            return PathPermissionCheckResult.Allow($"匹配允许规则: {allowRule.Pattern}", allowRule);
        }

        // 步骤7: 默认 — 工作目录外需要确认
        return PathPermissionCheckResult.Ask(
            "写入路径在允许的工作目录之外，需要用户确认。");
    }

    /// <summary>
    /// 获取 Read deny 规则的排除模式 — 对齐 TS getFileReadIgnorePatterns
    /// 将 deny 规则的 Pattern 规范化为相对于 workingDirectory 的路径
    /// 用于搜索工具将 deny 规则转化为 glob 排除模式
    /// </summary>
    public IReadOnlyList<string> GetReadDenyPatterns(string? workingDirectory = null)
    {
        if (_rules.Count == 0)
            return [];

        var workDir = workingDirectory ?? _workingDirectory;
        var normalizedWorkDir = NormalizePath(Path.GetFullPath(workDir));

        var patterns = new List<string>();
        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            if (rule.ToolType != PathPermissionToolType.Read || rule.Behavior != PermissionBehavior.Deny)
                continue;

            if (string.IsNullOrEmpty(rule.Pattern))
                continue;

            var normalizedPattern = NormalizeDenyPattern(rule.Pattern, normalizedWorkDir);
            if (normalizedPattern is not null)
            {
                patterns.Add(normalizedPattern);
            }
        }

        return patterns;
    }

    /// <summary>
    /// 规范化 deny 模式为搜索排除模式 — 对齐 TS normalizePatternsToPath
    /// 将绝对路径模式转为相对路径，超出工作目录的模式跳过
    /// </summary>
    private static string? NormalizeDenyPattern(string pattern, string normalizedWorkDir)
    {
        var posixPattern = ToPosixPath(pattern);

        // 去除 /** 后缀（匹配目录及其内容的模式）
        var cleanPattern = posixPattern.EndsWith("/**", StringComparison.Ordinal)
            ? posixPattern[..^3]
            : posixPattern;

        // 绝对路径模式：转为相对路径
        if (Path.IsPathFullyQualified(pattern.Replace('/', '\\')))
        {
            var posixWorkDir = ToPosixPath(normalizedWorkDir);
            // 模式在工作目录内：转为相对路径
            if (cleanPattern.StartsWith(posixWorkDir + "/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = cleanPattern[(posixWorkDir.Length + 1)..];
                return string.IsNullOrEmpty(relative) ? null : relative;
            }

            // 模式是工作目录本身
            if (string.Equals(cleanPattern, posixWorkDir, StringComparison.OrdinalIgnoreCase))
            {
                return null; // 整个工作目录被 deny，搜索工具应直接拒绝
            }

            // 模式在工作目录外：跳过（对齐 TS: 模式在 cwd 外则不加入结果）
            return null;
        }

        // 相对路径模式：直接使用（对齐 TS: null root 的模式直接加入结果）
        // 以 / 开头的非驱动器路径：作为路径段匹配
        if (cleanPattern.StartsWith('/') && !IsPosixDrivePath(cleanPattern))
        {
            return cleanPattern.TrimStart('/');
        }

        return cleanPattern;
    }

    /// <summary>
    /// UNC 路径检测 — 对齐 TS: startsWith('\\\\') || startsWith('//')
    /// </summary>
    private static bool IsUncPath(string path)
    {
        return path.StartsWith(@"\\", StringComparison.Ordinal) ||
               path.StartsWith("//", StringComparison.Ordinal);
    }

    /// <summary>
    /// 路径规范化 — 统一分隔符，去除尾部分隔符
    /// </summary>
    private static string NormalizePath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// 检查路径是否在工作目录或额外目录内 — 对齐 TS pathInAllowedWorkingPath
    /// 使用路径段边界检查，防止 C:\Projects\MyAppSecret 绕过 C:\Projects\MyApp
    /// </summary>
    private bool IsInWorkingDirectory(string normalizedPath)
    {
        // 检查主工作目录 — 必须验证路径段边界
        if (IsPathUnderDirectory(normalizedPath, _workingDirectory))
            return true;

        // 检查额外工作目录（已预解析）
        for (var i = 0; i < _resolvedAdditionalDirectories.Length; i++)
        {
            if (IsPathUnderDirectory(normalizedPath, _resolvedAdditionalDirectories[i]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 检查路径是否在指定目录下 — 验证路径段边界
    /// 对齐 TS: normalizedPath === dir || normalizedPath.startsWith(dir + sep)
    /// </summary>
    private static bool IsPathUnderDirectory(string normalizedPath, string directory)
    {
        // 精确匹配目录本身
        if (string.Equals(normalizedPath, directory, StringComparison.OrdinalIgnoreCase))
            return true;

        // 路径必须以 directory + 分隔符 开头，确保路径段边界正确
        // 防止 C:\Projects\MyAppSecret 绕过 C:\Projects\MyApp
        if (normalizedPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// 内部可读路径白名单 — 对齐 TS checkReadableInternalPath
    /// 返回 null 表示 passthrough（不匹配任何内部路径）
    /// 所有路径检查使用 IsPathUnderDirectory 确保路径段边界
    /// </summary>
    private PathPermissionCheckResult? CheckInternalReadablePath(string normalizedPath)
    {
        // Session memory: ~/.jcc/projects/{cwd}/
        var projectsDir = Path.Combine(_appDataRoot, "projects");
        if (IsPathUnderDirectory(normalizedPath, projectsDir))
        {
            return PathPermissionCheckResult.Allow("项目目录文件允许读取");
        }

        // Tasks: ~/.jcc/tasks/
        var tasksDir = Path.Combine(_appDataRoot, AppDataConstants.TasksFolderName);
        if (IsPathUnderDirectory(normalizedPath, tasksDir))
        {
            return PathPermissionCheckResult.Allow("任务文件允许读取");
        }

        // Teams: ~/.jcc/teams/
        var teamsDir = Path.Combine(_appDataRoot, AppDataConstants.TeamsFolderName);
        if (IsPathUnderDirectory(normalizedPath, teamsDir))
        {
            return PathPermissionCheckResult.Allow("团队文件允许读取");
        }

        // Plans: ~/.jcc/plans/
        var plansDir = Path.Combine(_appDataRoot, AppDataConstants.PlansFolderName);
        if (IsPathUnderDirectory(normalizedPath, plansDir))
        {
            return PathPermissionCheckResult.Allow("计划文件允许读取");
        }

        // Tool results: ~/.jcc/tool-results/ — 对齐 TS: Read tool auto-allows tool-results dir
        var toolResultsDir = Path.Combine(_appDataRoot, AppDataConstants.ToolResultsFolderName);
        if (IsPathUnderDirectory(normalizedPath, toolResultsDir))
        {
            return PathPermissionCheckResult.Allow("工具结果文件允许读取");
        }

        // Sessions: ~/.jcc/sessions/
        var sessionsDir = Path.Combine(_appDataRoot, AppDataConstants.SessionsFolderName);
        if (IsPathUnderDirectory(normalizedPath, sessionsDir))
        {
            return PathPermissionCheckResult.Allow("会话文件允许读取");
        }

        // Memdir: ~/.jcc/memdir/
        var memdirPath = Path.Combine(_appDataRoot, "memdir");
        if (IsPathUnderDirectory(normalizedPath, memdirPath))
        {
            return PathPermissionCheckResult.Allow("记忆文件允许读取");
        }

        return null; // passthrough
    }

    /// <summary>
    /// 匹配路径权限规则 — 对齐 TS matchingRuleForInput
    /// </summary>
    private PathPermissionRule? MatchRule(string normalizedPath, PathPermissionToolType toolType, PermissionBehavior behavior)
    {
        for (var i = 0; i < _rules.Count; i++)
        {
            var rule = _rules[i];
            if (rule.ToolType != toolType || rule.Behavior != behavior)
                continue;

            if (string.IsNullOrEmpty(rule.Pattern))
                continue;

            if (MatchesPattern(normalizedPath, rule.Pattern))
                return rule;
        }

        return null;
    }

    /// <summary>
    /// 通配符模式匹配 — 对齐 TS matchingRuleForInput + ignore 库的 glob 匹配
    /// 支持 ** (递归匹配) 和 *.ext (扩展名匹配)
    /// 在 Windows 上将路径和模式统一为 POSIX 格式后匹配
    /// </summary>
    private static bool MatchesPattern(string path, string pattern)
    {
        var posixPath = ToPosixPath(path);
        var posixPattern = ToPosixPath(pattern);

        // 精确匹配
        if (string.Equals(posixPath, posixPattern, StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedPattern = posixPattern.TrimEnd('/');

        // 去除 /** 后缀 — 对齐 TS: "path/**" 匹配 path 及其所有子内容
        if (normalizedPattern.EndsWith("/**", StringComparison.Ordinal))
        {
            var prefix = normalizedPattern[..^3];
            // 前缀匹配（含驱动器路径），确保路径段边界
            if (posixPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                return true;
            // 精确匹配目录本身
            if (string.Equals(posixPath, prefix, StringComparison.OrdinalIgnoreCase))
                return true;
            // 路径段匹配: 模式 /secrets/** 匹配 /c/secrets/file.txt
            if (MatchesPathSegment(posixPath, prefix))
                return true;
        }

        // 前缀匹配: pattern 是路径的前缀，确保路径段边界
        if (posixPath.StartsWith(normalizedPattern + "/", StringComparison.OrdinalIgnoreCase))
            return true;

        // 精确目录匹配
        if (string.Equals(posixPath, normalizedPattern, StringComparison.OrdinalIgnoreCase))
            return true;

        // 路径段匹配: 模式不含驱动器前缀时，匹配路径中的对应段
        if (MatchesPathSegment(posixPath, normalizedPattern))
            return true;

        // 文件名通配符匹配: *.ext — 只匹配文件名部分
        if (normalizedPattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var extension = normalizedPattern[1..]; // 包含点号，如 ".env"
            // 提取文件名，只匹配文件名部分
            var lastSlash = posixPath.LastIndexOf('/');
            var fileName = lastSlash >= 0 ? posixPath[(lastSlash + 1)..] : posixPath;
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 纯文件名匹配: 模式不含 / 且不以 * 开头 — 对齐 TS ignore 库的文件名匹配
        // 例: ".env" 匹配 "/c/projects/myapp/.env"
        if (!normalizedPattern.Contains('/') && !normalizedPattern.StartsWith('*'))
        {
            var lastSlash = posixPath.LastIndexOf('/');
            var fileName = lastSlash >= 0 ? posixPath[(lastSlash + 1)..] : posixPath;
            if (string.Equals(fileName, normalizedPattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 路径段匹配 — 对齐 TS patternWithRoot 的 root 解析逻辑
    /// 当模式不以驱动器号开头时，匹配路径中的对应段
    /// 例: 模式 "/secrets" 匹配 "/c/secrets/file.txt"（因为 /secrets 是路径的子段）
    /// </summary>
    private static bool MatchesPathSegment(string posixPath, string pattern)
    {
        // 模式以 / 开头但不是驱动器路径（/c/...），则作为路径段匹配
        if (pattern.StartsWith('/') && !IsPosixDrivePath(pattern))
        {
            // 检查路径中是否包含该模式作为路径段（确保段边界）
            // /secrets 匹配 /c/secrets/file.txt → 路径包含 /secrets/
            if (posixPath.Contains(pattern + "/", StringComparison.OrdinalIgnoreCase))
                return true;
            // 路径以该模式结尾（确保段边界）
            if (posixPath.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 检查 POSIX 路径是否为 Windows 驱动器路径 — 如 /c/...
    /// </summary>
    private static bool IsPosixDrivePath(string posixPath)
    {
        return posixPath.Length >= 3 &&
               posixPath[0] == '/' &&
               char.IsLetter(posixPath[1]) &&
               posixPath[2] == '/';
    }

    /// <summary>
    /// 将路径转换为 POSIX 格式 — 对齐 TS windowsPathToPosixPath
    /// C:\Users\test → /c/Users/test
    /// </summary>
    private static string ToPosixPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        // Windows 绝对路径: C:\... → /c/...
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            var driveLetter = char.ToLowerInvariant(path[0]);
            var rest = path[2..].Replace('\\', '/');
            return $"/{driveLetter}{rest}";
        }

        // UNC 路径和相对路径：只替换分隔符
        return path.Replace('\\', '/');
    }
}
