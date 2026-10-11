namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 路径约束验证器实现 — 对齐 TS pathValidation.ts
/// 核心功能: 34个命令的路径提取 + 操作类型映射 + 危险路径检查 + 重定向验证
/// 拆分后职责：接口实现 + 主入口编排 + 命令操作类型/动作描述映射。
/// 路径提取/命令解析/安全包装剥离 → <see cref="PathExpansionDetector"/>。
/// 重定向提取/验证 → <see cref="PathRedirectionValidator"/>。
/// 危险删除路径检测 → <see cref="PathRemovalChecker"/>。
/// </summary>
[Register(typeof(IPathConstraintValidator), ServiceLifetime.Singleton)]
public sealed partial class PathConstraintValidator : ServiceEntity, IPathConstraintValidator {
    /// <summary>
    /// 命令操作类型映射 — 对齐 TS COMMAND_OPERATION_TYPE
    /// </summary>
    private static readonly FrozenDictionary<PathCommand, FileOperationType> CommandOperationTypeMap = new Dictionary<PathCommand, FileOperationType> {
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
    private static readonly FrozenDictionary<PathCommand, string> ActionVerbs = new Dictionary<PathCommand, string> {
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

    private readonly IPathValidator _pathValidator;

    /// <summary>
    /// 构造路径约束验证器
    /// </summary>
    public PathConstraintValidator(IPathValidator pathValidator) {
        _pathValidator = pathValidator;
    }

    /// <summary>
    /// 检查命令的路径约束 — 主入口，对齐 TS checkPathConstraints
    /// </summary>
    public PathConstraintResult CheckPathConstraints(
        string command,
        string workingDirectory,
        bool compoundCommandHasCd = false) {
        if (string.IsNullOrWhiteSpace(command)) {
            return new PathConstraintResult(PermissionBehavior.Passthrough);
        }

        // 1. 进程替换检测 — 对齐 TS: >>(cmd) 或 <(...) 要求手动审批
        if (PathRedirectionValidator.ProcessSubstitutionPattern.IsMatch(command)) {
            return new PathConstraintResult(
                PermissionBehavior.Ask,
                "Process substitution detected — requires manual approval");
        }

        // 2. 提取输出重定向
        var redirections = PathRedirectionValidator.ExtractOutputRedirections(command);
        if (redirections.Count > 0) {
            // 危险重定向检测: 重定向目标含 shell 展开语法
            foreach (var redirect in redirections) {
                if (PathRedirectionValidator.ShellExpansionPattern.IsMatch(redirect.Target)) {
                    return new PathConstraintResult(
                        PermissionBehavior.Ask,
                        $"Shell expansion in redirection target: {redirect.Target}");
                }
            }

            // 验证输出重定向路径
            var redirectResult = PathRedirectionValidator.ValidateOutputRedirections(
                redirections, workingDirectory, compoundCommandHasCd);
            if (redirectResult.Behavior != PermissionBehavior.Passthrough) {
                return redirectResult;
            }
        }

        // 3. 解析命令并验证路径
        var (cmdName, args) = PathExpansionDetector.ParseCommandParts(command);
        if (string.IsNullOrEmpty(cmdName)) {
            return new PathConstraintResult(PermissionBehavior.Passthrough);
        }

        // 剥离安全包装命令
        var (innerCmd, innerArgs) = PathExpansionDetector.StripSafeWrappers(cmdName, args);

        // 查找匹配的 PathCommand
        var pathCommand = PathCommandExtensions.FromValue(innerCmd);
        if (pathCommand is null) {
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
        FileOperationType? operationTypeOverride = null) {
        var operationType = operationTypeOverride
            ?? CommandOperationTypeMap.GetValueOrDefault(command);

        // 1. 命令特定验证器: mv/cp 带标志时拒绝（--target-directory 可绕过路径提取）
        if ((command == PathCommand.Mv || command == PathCommand.Cp)
            && args.Any(a => a.StartsWith('-'))) {
            return new PathConstraintResult(
                PermissionBehavior.Ask,
                $"{command.ToValue()} with flags may bypass path extraction — requires manual approval",
                OperationType: operationType,
                Command: command);
        }

        // 2. cd + 写操作拦截 — 防止 cd .claude/ && mv test.txt settings.json 绕过
        if (compoundCommandHasCd && operationType != FileOperationType.Read) {
            return new PathConstraintResult(
                PermissionBehavior.Ask,
                $"cd + write operation ({command.ToValue()}) requires manual approval",
                OperationType: operationType,
                Command: command);
        }

        // 3. 提取路径
        var paths = PathExpansionDetector.ExtractPaths(command, args, workingDirectory);

        // 4. 验证每个路径
        foreach (var path in paths) {
            if (!_pathValidator.IsPathWithinWorkspace(path, workingDirectory)) {
                return new PathConstraintResult(
                    PermissionBehavior.Ask,
                    $"Cannot {ActionVerbs.GetValueOrDefault(command, "access")} '{path}' — outside working directory",
                    BlockedPath: path,
                    OperationType: operationType,
                    Command: command);
            }
        }

        // 5. 危险删除路径检查（rm/rmdir）
        if (command == PathCommand.Rm || command == PathCommand.Rmdir) {
            var removalResult = CheckDangerousRemovalPaths(command, args, workingDirectory);
            if (removalResult.Behavior != PermissionBehavior.Passthrough) {
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
        string workingDirectory) {
        var paths = PathExpansionDetector.FilterOutFlags(args);

        foreach (var rawPath in paths) {
            var expandedPath = PathExpansionDetector.ExpandTilde(rawPath);
            var absolutePath = PathExpansionDetector.ResolvePath(expandedPath, workingDirectory);

            if (PathRemovalChecker.IsDangerousRemovalPath(absolutePath)) {
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
}
