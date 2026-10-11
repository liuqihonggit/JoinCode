namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 路径重定向验证器 — 从 PathConstraintValidator 拆分而来。
/// 职责：提取输出重定向、验证重定向目标安全性、检测进程替换与 Shell 展开语法。
/// 对齐 TS validateOutputRedirections + extractOutputRedirections。
/// </summary>
internal static class PathRedirectionValidator {

    /// <summary>
    /// 进程替换模式 — 对齐 TS checkPathConstraints 中的进程替换检测
    /// </summary>
    internal static readonly Regex ProcessSubstitutionPattern = new(
        @">>\s*>\s*\(|>\s*>\s*\(|<\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// Shell 展开模式 — 检测重定向目标中的变量引用
    /// </summary>
    internal static readonly Regex ShellExpansionPattern = new(
        @"\$[A-Za-z_]|%[A-Za-z_]%|\$\{", RegexOptions.Compiled);

    /// <summary>
    /// 验证输出重定向 — 对齐 TS validateOutputRedirections
    /// </summary>
    internal static PathConstraintResult ValidateOutputRedirections(
        IReadOnlyList<OutputRedirection> redirections,
        string workingDirectory,
        bool compoundCommandHasCd) {
        // cd + 重定向 → 要求手动审批
        if (compoundCommandHasCd && redirections.Count > 0) {
            return new PathConstraintResult(
                PermissionBehavior.Ask,
                "cd + output redirection requires manual approval");
        }

        foreach (var redirect in redirections) {
            // /dev/null 始终安全
            if (redirect.Target.Equals("/dev/null", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            // 保留设备名重定向需确认 — git bash 中会创建同名普通文件（Windows 保留设备名）— ADR 0012
            // 委托 RetainedDeviceNames.IsMatch（唯一数据源）— P0-③ 单数据源改造
            if (RetainedDeviceNames.IsMatch(redirect.Target)) {
                return new PathConstraintResult(
                    PermissionBehavior.Ask,
                    $"检测到保留设备名重定向 '{redirect.Target}' — 在 git bash 中会创建同名普通文件（Windows 保留设备名）。若本意是丢弃输出，请改用 /dev/null");
            }

            // 检查路径是否在工作区内
            if (!IsPathWithinWorkspaceSimple(redirect.Target, workingDirectory)) {
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
    internal static IReadOnlyList<OutputRedirection> ExtractOutputRedirections(string command) {
        var results = new List<OutputRedirection>();
        var i = 0;

        while (i < command.Length) {
            // 跳过引号内容
            if (command[i] is '"' or '\'') {
                var quote = command[i];
                i++;
                while (i < command.Length && command[i] != quote) {
                    i++;
                }

                i++;
                continue;
            }

            // 检测 >> 或 >
            if (command[i] == '>') {
                var isAppend = i + 1 < command.Length && command[i + 1] == '>';
                var start = isAppend ? i + 2 : i + 1;

                // 跳过空格
                while (start < command.Length && char.IsWhiteSpace(command[start])) {
                    start++;
                }

                // 提取目标路径
                var end = start;
                while (end < command.Length && !char.IsWhiteSpace(command[end])
                       && command[end] != '|' && command[end] != ';'
                       && command[end] != '&' && command[end] != '>') {
                    end++;
                }

                if (end > start) {
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
    /// 简化版路径工作区检查（不依赖 IPathValidator）
    /// </summary>
    internal static bool IsPathWithinWorkspaceSimple(string path, string workingDirectory) {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(workingDirectory)) {
            return false;
        }

        try {
            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(workingDirectory, path));
            var fullWorkDir = Path.GetFullPath(workingDirectory);

            return fullPath.StartsWith(fullWorkDir, StringComparison.OrdinalIgnoreCase);
        } catch {
            return false;
        }
    }
}

/// <summary>
/// 输出重定向信息
/// </summary>
internal sealed record OutputRedirection(string Target, string Operator);
