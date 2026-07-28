
namespace Core.Permission;

/// <summary>
/// 权限检查上下文 — 在中间件管道中传递权限检查所需的所有数据
/// </summary>
public sealed class PermissionCheckContext
{
    /// <summary>
    /// 要检查权限的工具名称
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// 工具调用参数
    /// </summary>
    public Dictionary<string, JsonElement>? Arguments { get; init; }

    /// <summary>
    /// 当前权限模式
    /// </summary>
    public required PermissionMode CurrentMode { get; init; }

    /// <summary>
    /// 权限配置 — 中间件从中读取规则和模式定义
    /// </summary>
    public required PermissionConfig Config { get; init; }

    /// <summary>
    /// 自动批准的工具名集合 — 由 PermissionChecker 维护的可变状态
    /// </summary>
    public required HashSet<string> AutoApprovedTools { get; init; }

    /// <summary>
    /// 自动拒绝的工具名集合 — 由 PermissionChecker 维护的可变状态
    /// </summary>
    public required HashSet<string> AutoRejectedTools { get; init; }

    /// <summary>
    /// 权限检查结果 — 由中间件设置，非 null 时表示已做出决策
    /// </summary>
    public ToolPermissionCheckResult? Result { get; set; }

    /// <summary>
    /// 从工具参数中提取路径
    /// </summary>
    public static string? ExtractPathFromArguments(Dictionary<string, JsonElement> arguments)
    {
        if (arguments.TryGetValue("file_path", out var filePathEl) && filePathEl.ValueKind == JsonValueKind.String)
            return filePathEl.GetString();

        if (arguments.TryGetValue("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
            return pathEl.GetString();

        return null;
    }

    /// <summary>
    /// 检查是否为文件读取工具（含搜索工具，对齐 TS GrepTool/GlobTool 也走 checkReadPermissionForTool）
    /// </summary>
    public static bool IsFileReadTool(string toolName)
    {
        return string.Equals(toolName, FileToolNameConstants.FileRead, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, SearchToolNameConstants.Grep, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, SearchToolNameConstants.Glob, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查是否为文件写入/编辑工具
    /// </summary>
    public static bool IsFileWriteTool(string toolName)
    {
        return string.Equals(toolName, FileToolNameConstants.FileWrite, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, FileToolNameConstants.FileEdit, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(toolName, FileToolNameConstants.FileEditRegex, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查是否为 WebFetch 工具
    /// </summary>
    public static bool IsWebFetchTool(string toolName)
        => string.Equals(toolName, WebToolNameConstants.WebFetch, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 检查是否为 Config GET 操作 — 对齐 TS 版 ConfigTool.checkPermissions
    /// </summary>
    public static bool IsConfigGetOperation(string toolName, Dictionary<string, JsonElement>? arguments)
    {
        if (string.Equals(toolName, InteractionToolNameConstants.ConfigGet, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, InteractionToolNameConstants.ConfigList, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(toolName, InteractionToolNameConstants.Config, StringComparison.OrdinalIgnoreCase))
        {
            if (arguments == null || !arguments.TryGetValue("value", out var valueEl))
                return true;
            if (valueEl.ValueKind == JsonValueKind.Null || valueEl.ValueKind == JsonValueKind.Undefined)
                return true;
            return false;
        }

        return false;
    }

    /// <summary>
    /// 使用配置中的模式定义检查工具是否匹配某个操作类型
    /// </summary>
    public bool MatchesOperationType(string toolName, List<OperationPattern> patterns)
    {
        for (var i = 0; i < patterns.Count; i++)
        {
            if (MatchesPattern(toolName, patterns[i].Pattern, patterns[i].PatternType))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 检查是否为写入操作
    /// </summary>
    public bool IsWriteOperation(string toolName)
        => MatchesOperationType(toolName, Config.WriteOperationPatterns);

    /// <summary>
    /// 检查是否为读取操作
    /// </summary>
    public bool IsReadOperation(string toolName)
        => MatchesOperationType(toolName, Config.ReadOperationPatterns);

    /// <summary>
    /// 检查是否为 Shell 操作
    /// </summary>
    public bool IsShellOperation(string toolName)
        => MatchesOperationType(toolName, Config.ShellOperationPatterns);

    /// <summary>
    /// 检查路径是否为敏感路径
    /// </summary>
    public static bool IsSensitivePath(string path, List<SensitivePathPattern> patterns)
    {
        var fullPath = Path.GetFullPath(path);

        for (var i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];
            string resolvedPath = pattern.PathType == PathType.SpecialFolder
                ? ResolveSpecialFolder(pattern.Path)
                : pattern.Path;

            if (string.IsNullOrEmpty(resolvedPath))
                continue;

            switch (pattern.PathType)
            {
                case PathType.Contains:
                    if (fullPath.Contains(resolvedPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                    break;
                case PathType.StartsWith:
                    if (fullPath.StartsWith(resolvedPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                    break;
                case PathType.SpecialFolder:
                    if (fullPath.StartsWith(resolvedPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查命令是否为危险命令
    /// </summary>
    public static bool IsDangerousCommand(string command, List<DangerousCommandPattern> patterns)
    {
        var commandSpan = command.AsSpan();

        for (var i = 0; i < patterns.Count; i++)
        {
            if (ContainsOrdinalIgnoreCase(commandSpan, patterns[i].Pattern.AsSpan()))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 模式匹配
    /// </summary>
    public static bool MatchesPattern(string input, string pattern, PatternType patternType)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        return patternType switch
        {
            PatternType.Contains => input.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            PatternType.StartsWith => input.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            PatternType.EndsWith => input.EndsWith(pattern, StringComparison.OrdinalIgnoreCase),
            PatternType.Exact => input.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            PatternType.Regex => Regex.IsMatch(input, pattern),
            _ => false
        };
    }

    /// <summary>
    /// 将路径权限检查结果映射为工具权限检查结果
    /// </summary>
    public static ToolPermissionCheckResult MapPathResult(PathPermissionCheckResult pathResult)
    {
        return pathResult.Decision switch
        {
            PermissionBehavior.Allow => ToolPermissionCheckResult.Approved(),
            PermissionBehavior.Deny => ToolPermissionCheckResult.Rejected(pathResult.Reason ?? "路径权限被拒绝"),
            PermissionBehavior.Ask => ToolPermissionCheckResult.PendingConfirmation(pathResult.Reason ?? "路径需要用户确认"),
            _ => ToolPermissionCheckResult.PendingConfirmation(pathResult.Reason ?? "未知路径权限状态")
        };
    }

    /// <summary>
    /// 解析特殊文件夹占位符
    /// </summary>
    private static string ResolveSpecialFolder(string placeholder)
    {
        return placeholder switch
        {
            "{Windows}" => Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)),
            "{System}" => Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.System)),
            "{SystemX86}" => Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)),
            _ => placeholder
        };
    }

    /// <summary>
    /// 使用 OrdinalIgnoreCase 在 Span 中查找子串，避免创建新字符串
    /// </summary>
    private static bool ContainsOrdinalIgnoreCase(ReadOnlySpan<char> source, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return true;
        if (source.IsEmpty)
            return false;
        if (value.Length > source.Length)
            return false;

        for (int i = 0; i <= source.Length - value.Length; i++)
        {
            if (MatchesOrdinalIgnoreCase(source.Slice(i, value.Length), value))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 比较两个 Span 是否相等（OrdinalIgnoreCase）
    /// </summary>
    private static bool MatchesOrdinalIgnoreCase(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (char.ToUpperInvariant(a[i]) != char.ToUpperInvariant(b[i]))
                return false;
        }

        return true;
    }
}
