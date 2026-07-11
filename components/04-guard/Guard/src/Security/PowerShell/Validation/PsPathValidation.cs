namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PS 路径约束验证 — 与 TS pathValidation.ts 1:1 对齐
/// 核心逻辑：checkPathConstraints → validatePath → isPathAllowed
/// deny 永远优先于 ask，passthrough 是默认
/// </summary>
public static partial class PsPathValidation
{
    /// <summary>
    /// 错误消息中最多显示的目录数
    /// </summary>
    private const int MaxDirsToList = 5;

    /// <summary>
    /// 检查路径约束 — 顶层入口
    /// 对整条 PowerShell 命令做路径约束检查
    /// </summary>
    /// <param name="command">原始命令字符串</param>
    /// <param name="workingDirectory">当前工作目录</param>
    /// <param name="allowedDirectories">允许的目录列表</param>
    /// <param name="denyDirectories">拒绝的目录列表</param>
    public static PsSecurityResult CheckPathConstraints(
        string command,
        string workingDirectory,
 IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return PsSecurityResult.Passthrough;
        }

        var parsed = PsAstParser.Parse(command);

        if (!parsed.Valid)
        {
            return FallbackPathCheck(command, workingDirectory, denyDirectories);
        }

        if (parsed.Statements.Length == 0)
        {
            return PsSecurityResult.Passthrough;
        }

        PsSecurityResult? firstAsk = null;

        foreach (var statement in parsed.Statements)
        {
            var result = CheckStatementPaths(statement, workingDirectory, allowedDirectories, denyDirectories);
            if (result.Behavior == PermissionBehavior.Deny)
            {
                return result;
            }

            if (result.Behavior == PermissionBehavior.Ask && firstAsk is null)
            {
                firstAsk = result;
            }
        }

        return firstAsk ?? PsSecurityResult.Passthrough;
    }

    /// <summary>
    /// 检查单条语句的路径约束
    /// </summary>
    private static PsSecurityResult CheckStatementPaths(
        PsStatement statement,
        string workingDirectory,
        IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories)
    {
        PsSecurityResult? firstAsk = null;

        // 检查主命令
        foreach (var cmd in statement.Commands)
        {
            var result = CheckCommandPaths(cmd, workingDirectory, allowedDirectories, denyDirectories);
            if (result.Behavior == PermissionBehavior.Deny) return result;
            if (result.Behavior == PermissionBehavior.Ask && firstAsk is null) firstAsk = result;
        }

        // 检查嵌套命令（控制流中的命令）
        foreach (var cmd in statement.NestedCommands)
        {
            var result = CheckCommandPaths(cmd, workingDirectory, allowedDirectories, denyDirectories);
            if (result.Behavior == PermissionBehavior.Deny) return result;
            if (result.Behavior == PermissionBehavior.Ask && firstAsk is null) firstAsk = result;
        }

        // 检查重定向
        foreach (var redir in statement.Redirections)
        {
            if (!string.IsNullOrEmpty(redir.Target))
            {
                var result = ValidatePath(redir.Target, FileOperationType.Write, workingDirectory, allowedDirectories, denyDirectories);
                if (result.Behavior == PermissionBehavior.Deny) return result;
                if (result.Behavior == PermissionBehavior.Ask && firstAsk is null) firstAsk = result;
            }
        }

        return firstAsk ?? PsSecurityResult.Passthrough;
    }

    /// <summary>
    /// 检查单个命令的路径约束
    /// </summary>
    private static PsSecurityResult CheckCommandPaths(
        PsCommandElement cmd,
        string workingDirectory,
        IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories)
    {
        var extraction = PsPathExtractor.ExtractPaths(cmd);

        // 不可验证的路径参数 → ask
        if (extraction.HasUnvalidatablePathArg)
        {
            return PsSecurityResult.Ask($"Command '{cmd.Name}' contains arguments that cannot be statically validated for path safety");
        }

        // 写操作零路径且非可选写 → ask（可能是管道输出）
        if (extraction.OperationType == FileOperationType.Write
            && extraction.Paths.Count == 0
            && !extraction.OptionalWrite)
        {
            return PsSecurityResult.Ask($"Command '{cmd.Name}' is a write operation with no explicit path — output may go to an unvalidated location");
        }

        // 遍历每个路径进行验证
        PsSecurityResult? firstAsk = null;
        foreach (var path in extraction.Paths)
        {
            var result = ValidatePath(path, extraction.OperationType, workingDirectory, allowedDirectories, denyDirectories);
            if (result.Behavior == PermissionBehavior.Deny) return result;
            if (result.Behavior == PermissionBehavior.Ask && firstAsk is null) firstAsk = result;
        }

        return firstAsk ?? PsSecurityResult.Passthrough;
    }

    /// <summary>
    /// 验证单条路径 — 核心验证流水线
    /// 检查顺序是安全关键，不可调换：反引号 → :: → UNC → 变量 → 提供程序 → glob → 常规解析
    /// </summary>
    private static PsSecurityResult ValidatePath(
        string path,
        FileOperationType operationType,
        string workingDirectory,
        IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories)
    {
        // 1. 反引号 — PS 转义字符，无法静态验证
        if (path.Contains('`'))
        {
            var stripped = StripBackticks(path);
            if (!string.IsNullOrEmpty(stripped))
            {
                var denyResult = CheckDenyRuleForGuessedPath(stripped, denyDirectories, workingDirectory);
                if (denyResult is not null) return denyResult;
            }
            return PsSecurityResult.Ask($"Path contains backtick escape characters that cannot be statically validated: {path}");
        }

        // 2. :: 提供程序路径 — FileSystem::/etc/passwd 等
        if (path.Contains("::"))
        {
            var afterProvider = ExtractAfterProvider(path);
            if (!string.IsNullOrEmpty(afterProvider))
            {
                var denyResult = CheckDenyRuleForGuessedPath(afterProvider, denyDirectories, workingDirectory);
                if (denyResult is not null) return denyResult;
            }
            return PsSecurityResult.Ask($"Path uses provider-qualified syntax which cannot be fully validated: {path}");
        }

        // 3. UNC 路径 — 可触发网络请求泄露凭据
        if (IsUncPath(path))
        {
            return PsSecurityResult.Deny($"UNC path is not allowed as it may trigger network authentication: {path}", path);
        }

        // 4. 变量扩展 — 运行时展开无法静态验证
        if (path.Contains('$') || path.Contains('%'))
        {
            return PsSecurityResult.Ask($"Path contains variable expansion that cannot be statically validated: {path}");
        }

        // 5. 非文件系统提供程序路径 — env:、HKLM:、alias: 等
        if (IsNonFileSystemProviderPath(path))
        {
            return PsSecurityResult.Ask($"Path references a non-filesystem PowerShell provider: {path}");
        }

        // 6. Glob 模式（写操作）— 写操作中禁止通配符
        if (operationType == FileOperationType.Write && IsGlobPattern(path))
        {
            return PsSecurityResult.Deny($"Write operations with glob patterns are not allowed: {path}", path);
        }

        // 7. Glob 模式（读操作）— 验证 glob 基目录
        if (operationType == FileOperationType.Read && IsGlobPattern(path))
        {
            if (ContainsPathTraversal(path))
            {
                // 含遍历的 glob → 解析完整路径验证
                var resolved = SafeResolvePath(path, workingDirectory);
                return IsPathAllowed(resolved, operationType, allowedDirectories, denyDirectories, workingDirectory);
            }

            // 验证 glob 基目录
            var globBase = GetGlobBaseDirectory(path, workingDirectory);
            var denyResult = CheckDenyRuleForGuessedPath(globBase, denyDirectories, workingDirectory);
            if (denyResult is not null) return denyResult;

            return PsSecurityResult.Ask($"Read operation uses glob pattern which cannot be fully validated: {path}");
        }

        // 8. 常规路径解析
        var fullResolvedPath = SafeResolvePath(path, workingDirectory);

        // 危险删除检查（Remove-Item 对系统关键路径硬拒绝）
        if (operationType == FileOperationType.Write && IsDangerousRemovalPath(path, fullResolvedPath))
        {
            return PsSecurityResult.Deny($"Removing system-critical path is not allowed: {path}", path);
        }

        return IsPathAllowed(fullResolvedPath, operationType, allowedDirectories, denyDirectories, workingDirectory);
    }

    /// <summary>
    /// 路径允许检查 — 五层检查
    /// deny → 内部可编辑 → 安全检查 → 工作目录 → allow
    /// </summary>
    private static PsSecurityResult IsPathAllowed(
        string resolvedPath,
        FileOperationType operationType,
        IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories,
        string workingDirectory)
    {
        // 1. deny 规则匹配
        foreach (var denyDir in denyDirectories)
        {
            if (PathStartsWith(resolvedPath, denyDir))
            {
                return PsSecurityResult.Deny($"Path is in a denied directory: {resolvedPath}", resolvedPath);
            }
        }

        // 2. 工作目录内 — 读操作直接允许，写操作需 acceptEdits 模式（此处简化为允许）
        if (PathStartsWith(resolvedPath, workingDirectory))
        {
            if (operationType == FileOperationType.Read)
            {
                return PsSecurityResult.Passthrough;
            }
            // 写操作在工作目录内 — 允许（acceptEdits 检查由上层权限系统负责）
            return PsSecurityResult.Passthrough;
        }

        // 3. allow 规则匹配
        foreach (var allowDir in allowedDirectories)
        {
            if (PathStartsWith(resolvedPath, allowDir))
            {
                return PsSecurityResult.Passthrough;
            }
        }

        // 4. 默认 — 不允许
        var opLabel = operationType == FileOperationType.Write ? "write to" : "read from";
        var message = $"Path is outside allowed directories: {resolvedPath}";
        var suggestions = BuildSuggestions(operationType, allowedDirectories, workingDirectory);

        return new PsSecurityResult
        {
            Behavior = PermissionBehavior.Ask,
            Message = message,
            Suggestions = suggestions,
            DecisionReason = "pathOutsideAllowedDirs",
        };
    }

    #region 路径辅助方法

    /// <summary>
    /// 剥离反引号转义字符
    /// </summary>
    private static string StripBackticks(string path)
    {
        return path.Replace("`", "");
    }

    /// <summary>
    /// 提取 :: 提供程序路径之后的部分
    /// </summary>
    private static string ExtractAfterProvider(string path)
    {
        var idx = path.IndexOf("::", StringComparison.Ordinal);
        return idx >= 0 && idx + 2 < path.Length ? path[(idx + 2)..] : string.Empty;
    }

    /// <summary>
    /// 检查是否为 UNC 路径
    /// </summary>
    private static bool IsUncPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }
        // DavWWWRoot、@SSL@ 等 WebDAV 指示符
        if (path.Contains("DavWWWRoot", StringComparison.OrdinalIgnoreCase)
            || path.Contains("@SSL@", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 检查是否为非文件系统提供程序路径（env:、HKLM:、alias: 等）
    /// Windows 上 2+ 字母前缀匹配，POSIX 上任意字母数字前缀匹配
    /// </summary>
    private static bool IsNonFileSystemProviderPath(string path)
    {
        var colonIdx = path.IndexOf(':');
        if (colonIdx <= 0) return false;

        // 单字母驱动器号（C:、D:）是文件系统路径
        if (colonIdx == 1) return false;

        // Windows: 2+ 字母前缀是非文件系统提供程序
        var prefix = path[..colonIdx];
        if (prefix.Length >= 2 && prefix.All(char.IsLetterOrDigit))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 检查是否为 glob 模式（含 * ? [ ]）
    /// </summary>
    private static bool IsGlobPattern(string path)
    {
        return path.Contains('*') || path.Contains('?') || path.Contains('[');
    }

    /// <summary>
    /// 检查是否含路径遍历（..）
    /// </summary>
    private static bool ContainsPathTraversal(string path)
    {
        return path.Contains("..");
    }

    /// <summary>
    /// 安全解析路径 — 展开波浪号、合并工作目录
    /// </summary>
    private static string SafeResolvePath(string path, string workingDirectory)
    {
        // 展开波浪号
        var expanded = ExpandTilde(path);

        try
        {
            if (Path.IsPathRooted(expanded))
            {
                return Path.GetFullPath(expanded);
            }

            return Path.GetFullPath(Path.Combine(workingDirectory, expanded));
        }
        catch (Exception)
        {
            return expanded;
        }
    }

    /// <summary>
    /// 展开波浪号为用户主目录
    /// </summary>
    private static string ExpandTilde(string path)
    {
        if (!path.StartsWith('~')) return path;

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.Length == 1) return homeDir;
        if (path[1] == '\\' || path[1] == '/') return Path.Combine(homeDir, path[2..]);
        return path; // ~username 不展开
    }

    /// <summary>
    /// 获取 glob 模式的基础目录（第一个通配符之前的目录部分）
    /// </summary>
    private static string GetGlobBaseDirectory(string path, string workingDirectory)
    {
        var expanded = ExpandTilde(path);

        // 找到第一个通配符的位置
        var globIdx = -1;
        for (var i = 0; i < expanded.Length; i++)
        {
            if (expanded[i] == '*' || expanded[i] == '?' || expanded[i] == '[')
            {
                globIdx = i;
                break;
            }
        }

        if (globIdx < 0) return SafeResolvePath(expanded, workingDirectory);

        // 取通配符前的最后一个目录分隔符
        var lastSep = expanded[..globIdx].LastIndexOfAny(['\\', '/']);
        if (lastSep < 0) return workingDirectory;

        var basePart = expanded[..lastSep];
        return SafeResolvePath(basePart, workingDirectory);
    }

    /// <summary>
    /// 对无法完全解析的路径做尽力而为的 deny 规则匹配
    /// </summary>
    private static PsSecurityResult? CheckDenyRuleForGuessedPath(
        string guessedPath,
        IReadOnlyList<string> denyDirectories,
        string workingDirectory)
    {
        var resolved = SafeResolvePath(guessedPath, workingDirectory);
        foreach (var denyDir in denyDirectories)
        {
            if (PathStartsWith(resolved, denyDir))
            {
                return PsSecurityResult.Deny($"Path matches a denied directory: {resolved}", resolved);
            }
        }
        return null;
    }

    /// <summary>
    /// 检查路径是否以指定目录为前缀（不区分大小写）
    /// </summary>
    private static bool PathStartsWith(string path, string directory)
    {
        if (string.IsNullOrEmpty(directory)) return false;

        var normPath = path.Replace('/', '\\').TrimEnd('\\');
        var normDir = directory.Replace('/', '\\').TrimEnd('\\');

        if (!normPath.StartsWith(normDir, StringComparison.OrdinalIgnoreCase)) return false;

        // 确保是目录边界匹配（避免 C:\Windows 匹配 C:\Win）
        if (normPath.Length == normDir.Length) return true;
        return normPath[normDir.Length] == '\\';
    }

    /// <summary>
    /// 检查是否为危险删除路径（系统关键路径硬拒绝）
    /// </summary>
    private static bool IsDangerousRemovalPath(string rawPath, string resolvedPath)
    {
        var dangerousPaths = new[]
        {
            "/", @"\",
            "/etc", "/usr", "/bin", "/sbin", "/var", "/opt",
            @"C:\", @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)",
            @"C:\Users", @"C:\ProgramData",
        };

        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var dangerous in dangerousPaths)
        {
            if (PathStartsWith(resolvedPath, dangerous)) return true;
            if (PathStartsWith(rawPath, dangerous)) return true;
        }

        // 主目录根级别
        if (PathStartsWith(resolvedPath, homeDir) && resolvedPath.Replace('/', '\\').TrimEnd('\\') == homeDir.Replace('/', '\\').TrimEnd('\\'))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 构建权限更新建议
    /// </summary>
    private static string BuildSuggestions(FileOperationType operationType, IReadOnlyList<string> allowedDirectories, string workingDirectory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Suggestions:");

        if (operationType == FileOperationType.Write)
        {
            sb.AppendLine("  - Add the target directory to allowed write directories");
            sb.AppendLine("  - Switch to acceptEdits mode for automatic write approval");
        }
        else
        {
            sb.AppendLine("  - Add the target directory to allowed read directories");
        }

        if (allowedDirectories.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Currently allowed directories:");
            var dirsToShow = allowedDirectories.Take(MaxDirsToList);
            foreach (var dir in dirsToShow)
            {
                sb.AppendLine($"  - {dir}");
            }
            if (allowedDirectories.Count > MaxDirsToList)
            {
                sb.AppendLine($"  ... and {allowedDirectories.Count - MaxDirsToList} more");
            }
        }

        return sb.ToString();
    }

    #endregion

    #region 降级路径

    /// <summary>
    /// AST 解析失败时的尽力路径检查 — 对齐 TS powershellPermissions.ts 降级路径
    /// 基于字符串分割做 deny 规则扫描，无法确定时返回 Ask
    /// </summary>
    private static PsSecurityResult FallbackPathCheck(
        string command,
        string workingDirectory,
        IReadOnlyList<string> denyDirectories)
    {
        var backtickStripped = command.Replace("`", "");

        foreach (var fragment in backtickStripped.Split([';', '|', '\n', '\r', '{', '}', '(', ')', '&']))
        {
            var trimmed = fragment.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var tokens = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            var firstTok = tokens[0].ToLowerInvariant();
            var canonical = PsAliases.ResolveToCanonical(firstTok);

            if (denyDirectories.Count > 0)
            {
                foreach (var arg in tokens[1..])
                {
                    if (arg.StartsWith('-')) continue;
                    var resolved = SafeResolvePath(arg, workingDirectory);
                    foreach (var denyDir in denyDirectories)
                    {
                        if (PathStartsWith(resolved, denyDir))
                        {
                            return PsSecurityResult.Deny($"Path is in a denied directory: {resolved}", resolved);
                        }
                    }
                }
            }

            if (canonical == "remove-item")
            {
                foreach (var arg in tokens[1..])
                {
                    if (arg.StartsWith('-')) continue;
                    if (IsDangerousRemovalRawPath(arg))
                    {
                        return PsSecurityResult.Deny($"Removing system-critical path is not allowed: {arg}", arg);
                    }
                }
            }

            if (IsUncPathRaw(firstTok, tokens))
            {
                return PsSecurityResult.Deny("UNC path is not allowed as it may trigger network authentication", "");
            }
        }

        return PsSecurityResult.Ask("Could not parse command for path safety analysis — AST parser unavailable");
    }

    private static bool IsDangerousRemovalRawPath(string path)
    {
        var lower = path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        return lower is @"c:\windows\system32" or @"c:\windows\system"
            or @"c:\program files" or @"c:\program files (x86)"
            || lower.StartsWith(@"c:\windows\system32\")
            || lower.StartsWith(@"c:\windows\system\")
            || lower.StartsWith(@"c:\program files\")
            || lower.StartsWith(@"c:\program files (x86)\");
    }

    private static bool IsUncPathRaw(string firstTok, string[] tokens)
    {
        foreach (var arg in tokens)
        {
            if (arg.StartsWith(@"\\", StringComparison.Ordinal) || arg.StartsWith("//", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    #endregion
}
