using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 路径约束验证器实现 — 对齐 TS pathValidation.ts
/// 核心功能: 34个命令的路径提取 + 操作类型映射 + 危险路径检查 + 重定向验证
/// </summary>
[Register]
public sealed partial class PathConstraintValidator : IPathConstraintValidator
{
    /// <summary>
    /// 命令操作类型映射 — 对齐 TS COMMAND_OPERATION_TYPE
    /// </summary>
    private static readonly FrozenDictionary<PathCommand, FileOperationType> CommandOperationTypeMap = new Dictionary<PathCommand, FileOperationType>
    {
        // 读取操作
        [PathCommand.Cd] = FileOperationType.Read,
        [PathCommand.Ls] = FileOperationType.Read,
        [PathCommand.Find] = FileOperationType.Read,
        [PathCommand.Cat] = FileOperationType.Read,
        [PathCommand.Head] = FileOperationType.Read,
        [PathCommand.Tail] = FileOperationType.Read,
        [PathCommand.Sort] = FileOperationType.Read,
        [PathCommand.Uniq] = FileOperationType.Read,
        [PathCommand.Wc] = FileOperationType.Read,
        [PathCommand.Cut] = FileOperationType.Read,
        [PathCommand.Paste] = FileOperationType.Read,
        [PathCommand.Column] = FileOperationType.Read,
        [PathCommand.Tr] = FileOperationType.Read,
        [PathCommand.File] = FileOperationType.Read,
        [PathCommand.Stat] = FileOperationType.Read,
        [PathCommand.Diff] = FileOperationType.Read,
        [PathCommand.Awk] = FileOperationType.Read,
        [PathCommand.Strings] = FileOperationType.Read,
        [PathCommand.Hexdump] = FileOperationType.Read,
        [PathCommand.Od] = FileOperationType.Read,
        [PathCommand.Base64] = FileOperationType.Read,
        [PathCommand.Nl] = FileOperationType.Read,
        [PathCommand.Grep] = FileOperationType.Read,
        [PathCommand.Rg] = FileOperationType.Read,
        [PathCommand.Git] = FileOperationType.Read,
        [PathCommand.Jq] = FileOperationType.Read,
        [PathCommand.Sha256sum] = FileOperationType.Read,
        [PathCommand.Sha1sum] = FileOperationType.Read,
        [PathCommand.Md5sum] = FileOperationType.Read,

        // 写入操作
        [PathCommand.Rm] = FileOperationType.Write,
        [PathCommand.Rmdir] = FileOperationType.Write,
        [PathCommand.Mv] = FileOperationType.Write,
        [PathCommand.Cp] = FileOperationType.Write,
        [PathCommand.Sed] = FileOperationType.Write,

        // 创建操作
        [PathCommand.Mkdir] = FileOperationType.Create,
        [PathCommand.Touch] = FileOperationType.Create,
    }.ToFrozenDictionary();

    /// <summary>
    /// 命令动作描述映射 — 对齐 TS ACTION_VERBS
    /// </summary>
    private static readonly FrozenDictionary<PathCommand, string> ActionVerbs = new Dictionary<PathCommand, string>
    {
        [PathCommand.Cd] = "change directory to",
        [PathCommand.Ls] = "list files in",
        [PathCommand.Find] = "search for files in",
        [PathCommand.Mkdir] = "create directory in",
        [PathCommand.Touch] = "create file in",
        [PathCommand.Rm] = "remove files from",
        [PathCommand.Rmdir] = "remove directory from",
        [PathCommand.Mv] = "move files in",
        [PathCommand.Cp] = "copy files in",
        [PathCommand.Cat] = "read files from",
        [PathCommand.Head] = "read beginning of files from",
        [PathCommand.Tail] = "read end of files from",
        [PathCommand.Sort] = "sort files from",
        [PathCommand.Uniq] = "filter duplicates in files from",
        [PathCommand.Wc] = "count lines in files from",
        [PathCommand.Cut] = "extract columns from files in",
        [PathCommand.Paste] = "merge files in",
        [PathCommand.Column] = "format columns in files from",
        [PathCommand.Tr] = "translate characters in files from",
        [PathCommand.File] = "determine file type in",
        [PathCommand.Stat] = "get file status in",
        [PathCommand.Diff] = "compare files in",
        [PathCommand.Awk] = "process files in",
        [PathCommand.Strings] = "extract strings from files in",
        [PathCommand.Hexdump] = "hex dump files from",
        [PathCommand.Od] = "octal dump files from",
        [PathCommand.Base64] = "encode/decode files in",
        [PathCommand.Nl] = "number lines in files from",
        [PathCommand.Grep] = "search for patterns in files from",
        [PathCommand.Rg] = "search for patterns in files from",
        [PathCommand.Sed] = "edit files in",
        [PathCommand.Git] = "run git commands in",
        [PathCommand.Jq] = "process JSON in files from",
        [PathCommand.Sha256sum] = "compute SHA-256 of files in",
        [PathCommand.Sha1sum] = "compute SHA-1 of files in",
        [PathCommand.Md5sum] = "compute MD5 of files in",
    }.ToFrozenDictionary();

    /// <summary>
    /// 危险删除路径集合 — 对齐 TS isDangerousRemovalPath
    /// </summary>
    private static readonly FrozenSet<string> DangerousRemovalPaths = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "/", "/tmp", "/etc", "/usr", "/bin", "/sbin", "/var", "/root",
        "/home", "/opt", "/sys", "/proc", "/dev", "/lib",
        @"C:\", @"C:\Windows", @"C:\Program Files", @"C:\Users",
        @"D:\", @"E:\");

    /// <summary>
    /// 安全包装命令集合 — 对齐 TS stripSafeWrappers
    /// </summary>
    private static readonly FrozenSet<string> SafeWrapperCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "timeout", "nice", "nohup", "time", "stdbuf", "env");

    /// <summary>
    /// 进程替换模式 — 对齐 TS checkPathConstraints 中的进程替换检测
    /// </summary>
    private static readonly Regex ProcessSubstitutionPattern = new(
        @">>\s*>\s*\(|>\s*>\s*\(|<\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// Shell 展开模式 — 检测重定向目标中的变量引用
    /// </summary>
    private static readonly Regex ShellExpansionPattern = new(
        @"\$[A-Za-z_]|%[A-Za-z_]%|\$\{", RegexOptions.Compiled);

    [Inject] private readonly IPathValidator _pathValidator;

    /// <summary>
    /// 检查命令的路径约束 — 主入口，对齐 TS checkPathConstraints
    /// </summary>
    public PathConstraintResult CheckPathConstraints(
        string command,
        string workingDirectory,
        bool compoundCommandHasCd = false)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new PathConstraintResult(PermissionBehavior.Passthrough);
        }

        // 1. 进程替换检测 — 对齐 TS: >>(cmd) 或 <(...) 要求手动审批
        if (ProcessSubstitutionPattern.IsMatch(command))
        {
            return new PathConstraintResult(
                PermissionBehavior.Ask,
                "Process substitution detected — requires manual approval");
        }

        // 2. 提取输出重定向
        var redirections = ExtractOutputRedirections(command);
        if (redirections.Count > 0)
        {
            // 危险重定向检测: 重定向目标含 shell 展开语法
            foreach (var redirect in redirections)
            {
                if (ShellExpansionPattern.IsMatch(redirect.Target))
                {
                    return new PathConstraintResult(
                        PermissionBehavior.Ask,
                        $"Shell expansion in redirection target: {redirect.Target}");
                }
            }

            // 验证输出重定向路径
            var redirectResult = ValidateOutputRedirections(
                redirections, workingDirectory, compoundCommandHasCd);
            if (redirectResult.Behavior != PermissionBehavior.Passthrough)
            {
                return redirectResult;
            }
        }

        // 3. 解析命令并验证路径
        var (cmdName, args) = ParseCommandParts(command);
        if (string.IsNullOrEmpty(cmdName))
        {
            return new PathConstraintResult(PermissionBehavior.Passthrough);
        }

        // 剥离安全包装命令
        var (innerCmd, innerArgs) = StripSafeWrappers(cmdName, args);

        // 查找匹配的 PathCommand
        var pathCommand = PathCommandExtensions.FromValue(innerCmd);
        if (pathCommand is null)
        {
            return new PathConstraintResult(PermissionBehavior.Passthrough);
        }

        return ValidateCommandPaths(
            pathCommand.Value, innerArgs, workingDirectory, compoundCommandHasCd);
    }

    /// <summary>
    /// 验证指定命令的路径 — 对齐 TS validateCommandPaths
    /// </summary>
    public PathConstraintResult ValidateCommandPaths(
        PathCommand command,
        IReadOnlyList<string> args,
        string workingDirectory,
        bool compoundCommandHasCd = false,
        FileOperationType? operationTypeOverride = null)
    {
        var operationType = operationTypeOverride
            ?? CommandOperationTypeMap.GetValueOrDefault(command);

        // 1. 命令特定验证器: mv/cp 带标志时拒绝（--target-directory 可绕过路径提取）
        if ((command == PathCommand.Mv || command == PathCommand.Cp)
            && args.Any(a => a.StartsWith('-')))
        {
            return new PathConstraintResult(
                PermissionBehavior.Ask,
                $"{command.ToValue()} with flags may bypass path extraction — requires manual approval",
                OperationType: operationType,
                Command: command);
        }

        // 2. cd + 写操作拦截 — 防止 cd .claude/ && mv test.txt settings.json 绕过
        if (compoundCommandHasCd && operationType != FileOperationType.Read)
        {
            return new PathConstraintResult(
                PermissionBehavior.Ask,
                $"cd + write operation ({command.ToValue()}) requires manual approval",
                OperationType: operationType,
                Command: command);
        }

        // 3. 提取路径
        var paths = ExtractPaths(command, args, workingDirectory);

        // 4. 验证每个路径
        foreach (var path in paths)
        {
            if (!_pathValidator.IsPathWithinWorkspace(path, workingDirectory))
            {
                return new PathConstraintResult(
                    PermissionBehavior.Ask,
                    $"Cannot {ActionVerbs.GetValueOrDefault(command, "access")} '{path}' — outside working directory",
                    BlockedPath: path,
                    OperationType: operationType,
                    Command: command);
            }
        }

        // 5. 危险删除路径检查（rm/rmdir）
        if (command == PathCommand.Rm || command == PathCommand.Rmdir)
        {
            var removalResult = CheckDangerousRemovalPaths(command, args, workingDirectory);
            if (removalResult.Behavior != PermissionBehavior.Passthrough)
            {
                return removalResult;
            }
        }

        return new PathConstraintResult(PermissionBehavior.Passthrough);
    }

    /// <summary>
    /// 检查危险删除路径 — 对齐 TS checkDangerousRemovalPaths
    /// </summary>
    public PathConstraintResult CheckDangerousRemovalPaths(
        PathCommand command,
        IReadOnlyList<string> args,
        string workingDirectory)
    {
        var paths = FilterOutFlags(args);

        foreach (var rawPath in paths)
        {
            var expandedPath = ExpandTilde(rawPath);
            var absolutePath = ResolvePath(expandedPath, workingDirectory);

            if (IsDangerousRemovalPath(absolutePath))
            {
                return new PathConstraintResult(
                    PermissionBehavior.Ask,
                    $"Dangerous {command.ToValue()} operation detected — removing from system path: {absolutePath}",
                    BlockedPath: absolutePath,
                    OperationType: FileOperationType.Write,
                    Command: command);
            }
        }

        return new PathConstraintResult(
            PermissionBehavior.Passthrough,
            "No dangerous removals detected");
    }

    #region 路径提取器 — 对齐 TS PATH_EXTRACTORS

    /// <summary>
    /// 提取命令中的路径 — 对齐 TS PATH_EXTRACTORS[command]
    /// </summary>
    private static IReadOnlyList<string> ExtractPaths(
        PathCommand command, IReadOnlyList<string> args, string workingDirectory)
    {
        return command switch
        {
            PathCommand.Cd => ExtractCdPaths(args),
            PathCommand.Ls => FilterOutFlags(args, defaultPaths: [ "." ]),
            PathCommand.Find => ExtractFindPaths(args),
            PathCommand.Grep => ExtractGrepPaths(args),
            PathCommand.Rg => ExtractRgPaths(args),
            PathCommand.Sed => ExtractSedPaths(args),
            PathCommand.Jq => ExtractJqPaths(args),
            PathCommand.Git => ExtractGitPaths(args),
            PathCommand.Tr => ExtractTrPaths(args),
            // 大多数命令直接使用 FilterOutFlags
            _ => FilterOutFlags(args),
        };
    }

    /// <summary>
    /// cd 路径提取 — 对齐 TS: 所有参数拼接为单个路径，无参数则返回 home
    /// </summary>
    private static IReadOnlyList<string> ExtractCdPaths(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)];
        }

        // cd 的所有参数拼接为单个路径
        return [string.Join(" ", args)];
    }

    /// <summary>
    /// find 路径提取 — 对齐 TS: 收集路径直到遇到非全局标志
    /// </summary>
    private static IReadOnlyList<string> ExtractFindPaths(IReadOnlyList<string> args)
    {
        var paths = new List<string>();
        var i = 0;

        while (i < args.Count)
        {
            var arg = args[i];

            // -- 定界符后全是路径
            if (arg == "--")
            {
                i++;
                while (i < args.Count)
                {
                    paths.Add(args[i]);
                    i++;
                }

                break;
            }

            // 以 - 开头的是标志（find 的标志如 -name, -type 等）
            if (arg.StartsWith('-'))
            {
                break;
            }

            paths.Add(arg);
            i++;
        }

        return paths.Count > 0 ? paths : [ "." ];
    }

    /// <summary>
    /// grep 路径提取 — 对齐 TS parsePatternCommand
    /// </summary>
    private static IReadOnlyList<string> ExtractGrepPaths(IReadOnlyList<string> args)
    {
        var grepFlagsWithArgs = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "-e", "--regexp", "-f", "--file", "--include", "--exclude",
            "--exclude-from", "--exclude-dir", "--color");

        var defaults = args.Any(a => a is "-r" or "-R" or "--recursive") ? (IReadOnlyList<string>)["."] : [];

        return ParsePatternCommand(args, grepFlagsWithArgs, defaults);
    }

    /// <summary>
    /// rg 路径提取 — 对齐 TS parsePatternCommand
    /// </summary>
    private static IReadOnlyList<string> ExtractRgPaths(IReadOnlyList<string> args)
    {
        var rgFlagsWithArgs = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "-e", "--regexp", "-f", "--file", "-g", "--glob",
            "--iglob", "--type-add", "--type-not", "--color",
            "--max-columns", "--max-count", "--max-depth",
            "--max-filesize", "--mmap", "--sort", "--sort-path");

        return ParsePatternCommand(args, rgFlagsWithArgs, [ "." ]);
    }

    /// <summary>
    /// sed 路径提取 — 对齐 TS: 处理 -e/-f 标志，支持 -- 定界符
    /// </summary>
    private static IReadOnlyList<string> ExtractSedPaths(IReadOnlyList<string> args)
    {
        var paths = new List<string>();
        var pastFlags = false;
        var i = 0;

        while (i < args.Count)
        {
            var arg = args[i];

            if (arg == "--")
            {
                pastFlags = true;
                i++;
                continue;
            }

            if (!pastFlags)
            {
                if (arg is "-e" or "--expression")
                {
                    i += 2; // 跳过标志和值
                    continue;
                }

                if (arg is "-f" or "--file")
                {
                    i += 2; // 跳过标志和值
                    continue;
                }

                if (arg.StartsWith('-'))
                {
                    i++;
                    continue;
                }

                // 第一个非标志参数是脚本，跳过
                pastFlags = true;
                i++;
                continue;
            }

            paths.Add(arg);
            i++;
        }

        return paths;
    }

    /// <summary>
    /// jq 路径提取 — 对齐 TS: filter 后跟文件路径
    /// </summary>
    private static IReadOnlyList<string> ExtractJqPaths(IReadOnlyList<string> args)
    {
        var jqFlagsWithArgs = FrozenSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "-f", "--from-file", "-L", "--arg", "--argjson",
            "--slurpfile", "--rawfile", "--args", "--jsonargs");

        return ParsePatternCommand(args, jqFlagsWithArgs, []);
    }

    /// <summary>
    /// git 路径提取 — 对齐 TS: 仅处理 git diff --no-index
    /// </summary>
    private static IReadOnlyList<string> ExtractGitPaths(IReadOnlyList<string> args)
    {
        if (args.Count > 0
            && args[0].Equals("diff", StringComparison.OrdinalIgnoreCase)
            && args.Any(a => a.Equals("--no-index", StringComparison.OrdinalIgnoreCase)))
        {
            // git diff --no-index: 提取前2个非标志路径
            var paths = FilterOutFlags(args.Skip(1).ToList());
            return paths.Take(2).ToList();
        }

        // 其他 git 命令不做路径约束
        return [];
    }

    /// <summary>
    /// tr 路径提取 — 对齐 TS: 跳过字符集
    /// </summary>
    private static IReadOnlyList<string> ExtractTrPaths(IReadOnlyList<string> args)
    {
        // tr 命令: tr [选项] 字符集1 [字符集2] — 通常从 stdin 读取，无文件路径
        // 仅当有 -d 标志时跳1个字符集，否则跳2个
        var hasDelete = args.Any(a => a is "-d" or "--delete");
        var skipCount = hasDelete ? 1 : 2;

        var nonFlagArgs = args.Where(a => !a.StartsWith('-')).ToList();
        return nonFlagArgs.Skip(skipCount).ToList();
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 过滤标志参数，保留位置参数 — 对齐 TS filterOutFlags
    /// 正确处理 POSIX -- 端标志定界符
    /// </summary>
    private static IReadOnlyList<string> FilterOutFlags(
        IReadOnlyList<string> args, IReadOnlyList<string>? defaultPaths = null)
    {
        var positional = new List<string>();
        var pastDelimiter = false;

        foreach (var arg in args)
        {
            if (pastDelimiter)
            {
                positional.Add(arg);
                continue;
            }

            if (arg == "--")
            {
                pastDelimiter = true;
                continue;
            }

            if (!arg.StartsWith('-'))
            {
                positional.Add(arg);
            }
        }

        return positional.Count > 0 ? positional : defaultPaths ?? [];
    }

    /// <summary>
    /// 解析 grep/rg 风格命令 — 对齐 TS parsePatternCommand
    /// </summary>
    private static IReadOnlyList<string> ParsePatternCommand(
        IReadOnlyList<string> args,
        FrozenSet<string> flagsWithArgs,
        IReadOnlyList<string> defaults)
    {
        var paths = new List<string>();
        var pastDelimiter = false;
        var pastPattern = false;
        var i = 0;

        while (i < args.Count)
        {
            var arg = args[i];

            if (pastDelimiter)
            {
                paths.Add(arg);
                i++;
                continue;
            }

            if (arg == "--")
            {
                pastDelimiter = true;
                i++;
                continue;
            }

            // 跳过带参数的标志
            if (flagsWithArgs.Contains(arg) && i + 1 < args.Count)
            {
                i += 2;
                continue;
            }

            // 跳过标志
            if (arg.StartsWith('-'))
            {
                i++;
                continue;
            }

            // 第一个非标志参数是 pattern，跳过
            if (!pastPattern)
            {
                pastPattern = true;
                i++;
                continue;
            }

            paths.Add(arg);
            i++;
        }

        return paths.Count > 0 ? paths : defaults;
    }

    /// <summary>
    /// 验证输出重定向 — 对齐 TS validateOutputRedirections
    /// </summary>
    private static PathConstraintResult ValidateOutputRedirections(
        IReadOnlyList<OutputRedirection> redirections,
        string workingDirectory,
        bool compoundCommandHasCd)
    {
        // cd + 重定向 → 要求手动审批
        if (compoundCommandHasCd && redirections.Count > 0)
        {
            return new PathConstraintResult(
                PermissionBehavior.Ask,
                "cd + output redirection requires manual approval");
        }

        foreach (var redirect in redirections)
        {
            // /dev/null 始终安全
            if (redirect.Target.Equals("/dev/null", StringComparison.OrdinalIgnoreCase)
                || redirect.Target.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 检查路径是否在工作区内
            if (!IsPathWithinWorkspaceSimple(redirect.Target, workingDirectory))
            {
                return new PathConstraintResult(
                    PermissionBehavior.Ask,
                    $"Cannot write to '{redirect.Target}' — outside working directory",
                    BlockedPath: redirect.Target,
                    OperationType: FileOperationType.Create);
            }
        }

        return new PathConstraintResult(PermissionBehavior.Passthrough);
    }

    /// <summary>
    /// 提取输出重定向 — 对齐 TS extractOutputRedirections
    /// </summary>
    private static IReadOnlyList<OutputRedirection> ExtractOutputRedirections(string command)
    {
        var results = new List<OutputRedirection>();
        var i = 0;

        while (i < command.Length)
        {
            // 跳过引号内容
            if (command[i] is '"' or '\'')
            {
                var quote = command[i];
                i++;
                while (i < command.Length && command[i] != quote)
                {
                    i++;
                }

                i++;
                continue;
            }

            // 检测 >> 或 >
            if (command[i] == '>')
            {
                var isAppend = i + 1 < command.Length && command[i + 1] == '>';
                var start = isAppend ? i + 2 : i + 1;

                // 跳过空格
                while (start < command.Length && char.IsWhiteSpace(command[start]))
                {
                    start++;
                }

                // 提取目标路径
                var end = start;
                while (end < command.Length && !char.IsWhiteSpace(command[end])
                       && command[end] != '|' && command[end] != ';'
                       && command[end] != '&' && command[end] != '>')
                {
                    end++;
                }

                if (end > start)
                {
                    var target = command[start..end].Trim('"', '\'');
                    results.Add(new OutputRedirection(
                        target,
                        isAppend ? ">>" : ">"));
                }

                i = end;
                continue;
            }

            i++;
        }

        return results;
    }

    /// <summary>
    /// 剥离安全包装命令 — 对齐 TS stripSafeWrappers / stripWrappersFromArgv
    /// </summary>
    private static (string Command, IReadOnlyList<string> Args) StripSafeWrappers(
        string command, IReadOnlyList<string> args)
    {
        var currentCmd = command;
        var currentArgs = args.ToList();

        // 循环剥离包装命令
        while (SafeWrapperCommands.Contains(currentCmd) && currentArgs.Count > 0)
        {
            switch (currentCmd.ToLowerInvariant())
            {
                case "time":
                case "nohup":
                    // 直接剥离，支持 -- 定界符
                    if (currentArgs.Count > 0 && currentArgs[0] == "--")
                    {
                        currentArgs = currentArgs.Skip(1).ToList();
                    }

                    if (currentArgs.Count > 0)
                    {
                        currentCmd = currentArgs[0];
                        currentArgs = currentArgs.Skip(1).ToList();
                    }

                    break;

                case "timeout":
                    // 跳过 timeout 的 GNU 标志，找到 duration 参数后的命令
                    var timeoutIdx = SkipTimeoutFlags(currentArgs);
                    if (timeoutIdx >= 0 && timeoutIdx + 1 < currentArgs.Count)
                    {
                        currentCmd = currentArgs[timeoutIdx + 1];
                        currentArgs = currentArgs.Skip(timeoutIdx + 2).ToList();
                    }
                    else
                    {
                        // 无法解析，返回原始
                        return (currentCmd, currentArgs);
                    }

                    break;

                case "nice":
                    // nice cmd / nice -N cmd / nice -n N cmd
                    var niceIdx = 0;
                    if (currentArgs.Count > 0 && currentArgs[0].StartsWith("-")
                        && !currentArgs[0].Equals("--", StringComparison.Ordinal))
                    {
                        if (currentArgs[0] == "-n" && currentArgs.Count > 1)
                        {
                            niceIdx = 2;
                        }
                        else
                        {
                            niceIdx = 1;
                        }
                    }

                    if (niceIdx + 1 <= currentArgs.Count && niceIdx < currentArgs.Count)
                    {
                        currentCmd = currentArgs[niceIdx];
                        currentArgs = currentArgs.Skip(niceIdx + 1).ToList();
                    }
                    else
                    {
                        return (currentCmd, currentArgs);
                    }

                    break;

                case "stdbuf":
                    // 跳过 -i/-o/-e 标志
                    var stdbufIdx = SkipStdbufFlags(currentArgs);
                    if (stdbufIdx < currentArgs.Count)
                    {
                        currentCmd = currentArgs[stdbufIdx];
                        currentArgs = currentArgs.Skip(stdbufIdx + 1).ToList();
                    }
                    else
                    {
                        return (currentCmd, currentArgs);
                    }

                    break;

                case "env":
                    // 跳过 VAR=val 和安全标志
                    var envIdx = SkipEnvFlags(currentArgs);
                    if (envIdx < currentArgs.Count)
                    {
                        currentCmd = currentArgs[envIdx];
                        currentArgs = currentArgs.Skip(envIdx + 1).ToList();
                    }
                    else
                    {
                        return (currentCmd, currentArgs);
                    }

                    break;

                default:
                    return (currentCmd, currentArgs);
            }
        }

        return (currentCmd, currentArgs);
    }

    /// <summary>
    /// 跳过 timeout 的 GNU 标志 — 对齐 TS skipTimeoutFlags
    /// </summary>
    private static int SkipTimeoutFlags(IReadOnlyList<string> args)
    {
        var i = 0;
        while (i < args.Count)
        {
            var arg = args[i];

            if (arg == "--foreground")
            {
                i++;
                continue;
            }

            if (arg is "--kill-after" or "-k" or "--signal" or "-s" or "-v")
            {
                i += 2; // 标志 + 值
                continue;
            }

            // duration 参数: 数字+[smhd]?
            if (Regex.IsMatch(arg, @"^\d+(?:\.\d+)?[smhd]?$"))
            {
                return i;
            }

            // 未知标志，无法解析
            return -1;
        }

        return -1;
    }

    /// <summary>
    /// 跳过 stdbuf 的 -i/-o/-e 标志 — 对齐 TS skipStdbufFlags
    /// </summary>
    private static int SkipStdbufFlags(IReadOnlyList<string> args)
    {
        var i = 0;
        while (i < args.Count)
        {
            var arg = args[i];

            // -iVAL, -oVAL, -eVAL (融合选项)
            if (arg.Length >= 3 && arg[0] == '-'
                && (arg[1] is 'i' or 'o' or 'e'))
            {
                i++;
                continue;
            }

            // --input=VAL, --output=VAL, --error=VAL (长选项)
            if (arg.StartsWith("--input=", StringComparison.Ordinal)
                || arg.StartsWith("--output=", StringComparison.Ordinal)
                || arg.StartsWith("--error=", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            // -i VAL, -o VAL, -e VAL (短选项+空格)
            if (arg is "-i" or "-o" or "-e" && i + 1 < args.Count)
            {
                i += 2;
                continue;
            }

            // 非标志，这是命令开始
            break;
        }

        return i;
    }

    /// <summary>
    /// 跳过 env 的 VAR=val 和安全标志 — 对齐 TS skipEnvFlags
    /// </summary>
    private static int SkipEnvFlags(IReadOnlyList<string> args)
    {
        var i = 0;
        while (i < args.Count)
        {
            var arg = args[i];

            // VAR=val 形式
            if (arg.Contains('=') && !arg.StartsWith('-'))
            {
                i++;
                continue;
            }

            // 安全标志
            if (arg is "-i" or "-0" or "-v" or "-u")
            {
                i += arg is "-u" ? 2 : 1;
                continue;
            }

            // 拒绝危险标志
            if (arg is "-S" or "-C" or "-P")
            {
                return args.Count; // fail-closed
            }

            // 非标志，命令开始
            break;
        }

        return i;
    }

    /// <summary>
    /// 展开 tilde — 对齐 TS expandTilde
    /// </summary>
    private static string ExpandTilde(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        if (path.StartsWith("~/", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home + path[1..];
        }

        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return path;
    }

    /// <summary>
    /// 解析为绝对路径（不解析符号链接）— 对齐 TS resolve(path, cwd)
    /// </summary>
    private static string ResolvePath(string path, string workingDirectory)
    {
        if (string.IsNullOrEmpty(path))
        {
            return workingDirectory;
        }

        // 去除引号
        path = path.Trim('"', '\'');

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        try
        {
            return Path.GetFullPath(Path.Combine(workingDirectory, path));
        }
        catch
        {
            return path;
        }
    }

    /// <summary>
    /// 检查是否为危险删除路径 — 对齐 TS isDangerousRemovalPath
    /// </summary>
    private static bool IsDangerousRemovalPath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return false;
        }

        var normalized = absolutePath.Replace('\\', '/').TrimEnd('/');

        return DangerousRemovalPaths.Any(dangerous =>
        {
            var normalizedDangerous = dangerous.Replace('\\', '/').TrimEnd('/');
            return string.Equals(normalized, normalizedDangerous, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(normalizedDangerous + "/", StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// 简化版路径工作区检查（不依赖 IPathValidator）
    /// </summary>
    private static bool IsPathWithinWorkspaceSimple(string path, string workingDirectory)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(workingDirectory))
        {
            return false;
        }

        try
        {
            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workingDirectory, path));
            var fullWorkDir = Path.GetFullPath(workingDirectory);

            return fullPath.StartsWith(fullWorkDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解析命令部分 — 提取命令名和参数
    /// </summary>
    private static (string CommandName, IReadOnlyList<string> Arguments) ParseCommandParts(
        string command)
    {
        var parts = SplitCommandTokens(command);
        if (parts.Count == 0)
        {
            return (string.Empty, Array.Empty<string>());
        }

        return (parts[0], parts.Skip(1).ToList());
    }

    /// <summary>
    /// 分割命令为 token — 对齐 TS tryParseShellCommand
    /// </summary>
    private static List<string> SplitCommandTokens(string command)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if ((c == '"' || c == '\'') && !inQuotes)
            {
                inQuotes = true;
                quoteChar = c;
                continue;
            }

            if (c == quoteChar && inQuotes)
            {
                inQuotes = false;
                quoteChar = '\0';
                continue;
            }

            // 遇到管道/分号/&& 结束当前命令
            if (!inQuotes && (c == '|' || c == ';' || c == '&'))
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                break;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts;
    }

    #endregion
}

/// <summary>
/// 输出重定向信息
/// </summary>
internal sealed record OutputRedirection(string Target, string Operator);
