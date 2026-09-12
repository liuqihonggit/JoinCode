
namespace Core.Utils;

/// <summary>
/// 危险级别
/// </summary>
public enum DangerLevel
{
    /// <summary>
    /// 低风险 - 一般性警告
    /// </summary>
    Low,

    /// <summary>
    /// 中风险 - 需要确认
    /// </summary>
    Medium,

    /// <summary>
    /// 高风险 - 强烈警告
    /// </summary>
    High,

    /// <summary>
    /// 严重风险 - 可能破坏数据
    /// </summary>
    Critical
}

/// <summary>
/// 危险命令分析结果
/// </summary>
public sealed record DangerousCommandAnalysis
{
    /// <summary>
    /// 是否包含危险命令
    /// </summary>
    public bool IsDangerous { get; init; }

    /// <summary>
    /// 危险级别
    /// </summary>
    public DangerLevel Level { get; init; }

    /// <summary>
    /// 检测到的危险命令
    /// </summary>
    public List<string> DetectedCommands { get; init; } = new();

    /// <summary>
    /// 警告消息
    /// </summary>
    public string? WarningMessage { get; init; }

    /// <summary>
    /// 建议操作
    /// </summary>
    public string? Suggestion { get; init; }
}

/// <summary>
/// 危险命令定义
/// </summary>
public sealed record DangerousCommandDefinition
{
    public required string Pattern { get; init; }
    public required DangerLevel Level { get; init; }
    public required string Description { get; init; }
    public required string WarningMessage { get; init; }
    public string? Suggestion { get; init; }
    public bool IsRegex { get; init; }
}

/// <summary>
/// 破坏性命令分析器
/// </summary>
public static class DestructiveCommandAnalyzer
{
    /// <summary>
    /// 内部使用的命令定义，包含预编译的正则表达式
    /// </summary>
    private sealed record CompiledCommandDefinition
    {
        public required string Pattern { get; init; }
        public required DangerLevel Level { get; init; }
        public required string Description { get; init; }
        public required string WarningMessage { get; init; }
        public string? Suggestion { get; init; }
        public Regex? CompiledRegex { get; init; }
    }

    private static readonly List<CompiledCommandDefinition> DangerousCommands;

    static DestructiveCommandAnalyzer()
    {
        // 定义原始命令模式
        var definitions = new[]
        {
            // Critical 级别 - 数据破坏
            (Pattern: @"rm\s+-rf\s+/", Level: DangerLevel.Critical, Description: "Delete root directory",
             WarningMessage: $"{"WARNING:"} CRITICAL: This command will delete all files under the system root directory!",
             Suggestion: "Please confirm you really want to delete the entire system. This is usually a mistake."),
            (Pattern: @"rm\s+-rf\s+~", Level: DangerLevel.Critical, Description: "Delete user home directory",
             WarningMessage: $"{"WARNING:"} CRITICAL: This command will delete your entire user directory!",
             Suggestion: "This will delete all personal files, configurations, and documents."),
            (Pattern: @"rm\s+-rf\s+\*", Level: DangerLevel.Critical, Description: "Delete all contents in current directory",
             WarningMessage: $"{"WARNING:"} CRITICAL: This command will delete all files and folders in the current directory!",
             Suggestion: "Please confirm the current directory location and ensure no important files exist."),
            (Pattern: @"dd\s+if=.*\s+of=/dev/", Level: DangerLevel.Critical, Description: "Direct write to device",
             WarningMessage: $"{"WARNING:"} CRITICAL: This command writes directly to a disk device, potentially destroying the entire filesystem!",
             Suggestion: "Please confirm the target device is correct. Wrong device name will cause data loss."),
            (Pattern: @"mkfs\.", Level: DangerLevel.Critical, Description: "Format filesystem",
             WarningMessage: $"{"WARNING:"} CRITICAL: This command will format a disk partition, all data will be lost!",
             Suggestion: "Please confirm the target partition is correct."),
            (Pattern: @">\s*/dev/", Level: DangerLevel.Critical, Description: "Truncate device",
             WarningMessage: $"{"WARNING:"} CRITICAL: This command will truncate device contents!",
             Suggestion: "Please confirm the target device is correct."),

            // High 级别 - Git/GitHub 危险操作
            (Pattern: @"git\s+commit", Level: DangerLevel.High, Description: "Git commit via Shell (bypasses security scan)",
             WarningMessage: $"{"WARNING:"} HIGH RISK: Running git commit via Shell bypasses security scanning!",
             Suggestion: "Please use the git_commit tool instead, which automatically scans staged files for sensitive data and secrets."),
            (Pattern: @"git\s+reset\s+--hard", Level: DangerLevel.High, Description: "Force reset Git working tree",
             WarningMessage: $"{"WARNING:"} HIGH RISK: This command will discard all uncommitted changes!",
             Suggestion: "Please use 'git stash' to save changes first, or 'git status' to review current state."),
            (Pattern: @"git\s+clean\s+-f", Level: DangerLevel.High, Description: "Force delete untracked files",
             WarningMessage: $"{"WARNING:"} HIGH RISK: This command will permanently delete untracked files!",
             Suggestion: "Please use 'git clean -n' first to preview which files will be deleted."),
            (Pattern: @"git\s+push\s+.*--force", Level: DangerLevel.High, Description: "Force push",
             WarningMessage: $"{"WARNING:"} HIGH RISK: Force push may overwrite remote repository history!",
             Suggestion: "Consider using 'git push --force-with-lease' as a safer alternative."),
            (Pattern: @"git\s+branch\s+-D", Level: DangerLevel.High, Description: "Force delete branch",
             WarningMessage: $"{"WARNING:"} HIGH RISK: This command will force-delete a branch, potentially losing unmerged changes!",
             Suggestion: "Please confirm changes on the branch have been merged or backed up."),
            (Pattern: @"git\s+rebase", Level: DangerLevel.High, Description: "Rebase operation",
             WarningMessage: $"{"WARNING:"} HIGH RISK: Rebase rewrites commit history!",
             Suggestion: "If the branch has been pushed to remote, proceed with caution."),

            // High 级别 - 文件系统危险操作
            (Pattern: @"rm\s+-r", Level: DangerLevel.High, Description: "Recursive delete",
             WarningMessage: $"{"WARNING:"} HIGH RISK: This command will recursively delete a directory and its contents!",
             Suggestion: "Please confirm the target path is correct."),
            (Pattern: @"rmdir\s+/s", Level: DangerLevel.High, Description: "Recursive delete directory (Windows)",
             WarningMessage: $"{"WARNING:"} HIGH RISK: This command will recursively delete a directory and all its contents!",
             Suggestion: "Please confirm the target path is correct."),
            (Pattern: @"del\s+/[fq]", Level: DangerLevel.High, Description: "Force delete files (Windows)",
             WarningMessage: $"{"WARNING:"} HIGH RISK: This command will force-delete files without confirmation!",
             Suggestion: "Please confirm the target files are correct."),
            (Pattern: @"move\s+/y", Level: DangerLevel.High, Description: "Force move overwrite (Windows)",
             WarningMessage: $"{"WARNING:"} HIGH RISK: This command will overwrite existing files without prompting!",
             Suggestion: "Please confirm no important files will be overwritten."),

            // Medium 级别 - 需要注意的操作
            (Pattern: @"chmod\s+-R\s+777", Level: DangerLevel.Medium, Description: "Recursive full permissions",
             WarningMessage: $"{"WARNING:"} WARNING: Recursively setting 777 permissions may pose security risks!",
             Suggestion: "Consider using more restrictive permissions, such as 755."),
            (Pattern: @"chown\s+-R", Level: DangerLevel.Medium, Description: "Recursive change owner",
             WarningMessage: $"{"WARNING:"} WARNING: Recursively changing file ownership may affect system operation!",
             Suggestion: "Please confirm the target path is correct."),
            (Pattern: @"sudo", Level: DangerLevel.Medium, Description: "Run with admin privileges",
             WarningMessage: $"{"WARNING:"} WARNING: This command will run with administrator privileges!",
             Suggestion: "Please ensure the command source is trusted."),
            (Pattern: @"reg\s+delete", Level: DangerLevel.Medium, Description: "Delete registry key (Windows)",
             WarningMessage: $"{"WARNING:"} WARNING: Deleting registry keys may affect system stability!",
             Suggestion: "Please confirm the registry path is correct, and consider backing up first."),

            // Low 级别 - 一般性警告
            (Pattern: @"curl\s+.*\s*\|\s*sh", Level: DangerLevel.Low, Description: "Pipe remote script to shell",
             WarningMessage: $"{"WARNING:"} CAUTION: Executing scripts from the internet poses security risks!",
             Suggestion: "Please review the script content first and ensure the source is trusted."),
            (Pattern: @"wget\s+.*\s*\|\s*sh", Level: DangerLevel.Low, Description: "Pipe remote script to shell",
             WarningMessage: $"{"WARNING:"} CAUTION: Executing scripts from the internet poses security risks!",
             Suggestion: "Please review the script content first and ensure the source is trusted."),
            (Pattern: @"Invoke-Expression", Level: DangerLevel.Low, Description: "PowerShell dynamic execution",
             WarningMessage: $"{"WARNING:"} CAUTION: Invoke-Expression may execute malicious code!",
             Suggestion: "Please ensure the input content is trusted.")
        };

        // 预编译所有正则表达式
        DangerousCommands = definitions.Select(d => new CompiledCommandDefinition
        {
            Pattern = d.Pattern,
            Level = d.Level,
            Description = d.Description,
            WarningMessage = d.WarningMessage,
            Suggestion = d.Suggestion,
            CompiledRegex = new Regex(d.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase)
        }).ToList();
    }

    /// <summary>
    /// 分析命令是否包含破坏性操作
    /// </summary>
    public static DangerousCommandAnalysis Analyze(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new DangerousCommandAnalysis { IsDangerous = false };
        }

        var detectedCommands = new List<string>();
        var maxLevel = DangerLevel.Low;
        var descriptions = new List<string>();
        var suggestions = new List<string>();

        foreach (var definition in DangerousCommands)
        {
            // 使用预编译的正则表达式，避免每次重新编译
            bool isMatch = definition.CompiledRegex?.IsMatch(command) ?? false;

            if (isMatch)
            {
                detectedCommands.Add(definition.Pattern);

                if (definition.Level > maxLevel)
                {
                    maxLevel = definition.Level;
                }

                descriptions.Add(definition.WarningMessage);

                if (!string.IsNullOrEmpty(definition.Suggestion))
                {
                    suggestions.Add(definition.Suggestion);
                }
            }
        }

        if (detectedCommands.Count == 0)
        {
            return new DangerousCommandAnalysis { IsDangerous = false };
        }

        var warningMessage = string.Join("\n", descriptions.Distinct());
        var suggestion = suggestions.Count > 0
            ? string.Join("\n", suggestions.Distinct())
            : null;

        return new DangerousCommandAnalysis
        {
            IsDangerous = true,
            Level = maxLevel,
            DetectedCommands = detectedCommands,
            WarningMessage = warningMessage,
            Suggestion = suggestion
        };
    }

    /// <summary>
    /// 快速检查命令是否危险
    /// </summary>
    public static bool IsDangerous(string command)
    {
        return Analyze(command).IsDangerous;
    }

    /// <summary>
    /// 获取命令的安全建议
    /// </summary>
    public static string? GetSafetySuggestion(string command)
    {
        var analysis = Analyze(command);
        return analysis.IsDangerous ? analysis.Suggestion : null;
    }
}
