using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 破坏性命令检测器实现
/// </summary>
[Register]
public sealed class DestructiveCommandDetector : IDestructiveCommandDetector
{
    // 破坏性命令字典 - 命令名 -> 风险类型
    private static readonly Dictionary<string, CommandRisk> DestructiveCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        // 文件删除命令
        ["rm"] = CommandRisk.FileDeletion,
        ["del"] = CommandRisk.FileDeletion,
        ["erase"] = CommandRisk.FileDeletion,
        ["Remove-Item"] = CommandRisk.FileDeletion,
        ["rmdir"] = CommandRisk.DirectoryDeletion,
        ["rd"] = CommandRisk.DirectoryDeletion,

        // 格式化命令
        ["format"] = CommandRisk.SystemModification,
        ["mkfs"] = CommandRisk.SystemModification,
        ["fdisk"] = CommandRisk.SystemModification,
        ["diskpart"] = CommandRisk.SystemModification,

        // 数据修改命令
        ["dd"] = CommandRisk.DataModification,
        ["shred"] = CommandRisk.DataModification,
        ["wipe"] = CommandRisk.DataModification,

        // 系统修改命令
        ["chmod"] = CommandRisk.SystemModification,
        ["chown"] = CommandRisk.SystemModification,
        ["chgrp"] = CommandRisk.SystemModification,
        ["attrib"] = CommandRisk.SystemModification,
        ["cacls"] = CommandRisk.SystemModification,
        ["icacls"] = CommandRisk.SystemModification,

        // 注册表操作
        ["reg"] = CommandRisk.SystemModification,
        ["regedit"] = CommandRisk.SystemModification,

        // 远程执行
        ["curl"] = CommandRisk.RemoteExecution,
        ["wget"] = CommandRisk.RemoteExecution,
        ["Invoke-WebRequest"] = CommandRisk.RemoteExecution,
        ["Invoke-RestMethod"] = CommandRisk.RemoteExecution,

        // 权限提升
        ["sudo"] = CommandRisk.PrivilegeEscalation,
        ["runas"] = CommandRisk.PrivilegeEscalation,
        ["Start-Process"] = CommandRisk.PrivilegeEscalation,

        // 移动/重命名（工作目录内安全，超范围需确认）
        ["mv"] = CommandRisk.DataModification,
        ["move"] = CommandRisk.DataModification,
        ["Rename-Item"] = CommandRisk.DataModification,

        // 复制（工作目录内安全，超范围需确认）
        ["cp"] = CommandRisk.DataModification,
        ["copy"] = CommandRisk.DataModification,
        ["xcopy"] = CommandRisk.DataModification,
        ["robocopy"] = CommandRisk.DataModification,
        ["Copy-Item"] = CommandRisk.DataModification,
    };

    // 危险参数模式
    private static readonly FrozenDictionary<string, (CommandRisk Risk, string Description)> DangerousFlags = new Dictionary<string, (CommandRisk Risk, string Description)>(StringComparer.OrdinalIgnoreCase)
    {
        // 递归删除
        ["-r"] = (CommandRisk.RecursiveOperation, "Recursive operation"),
        ["-R"] = (CommandRisk.RecursiveOperation, "Recursive operation"),
        ["/s"] = (CommandRisk.RecursiveOperation, "Recursive operation"),
        ["/S"] = (CommandRisk.RecursiveOperation, "Recursive operation"),
        ["-recurse"] = (CommandRisk.RecursiveOperation, "Recursive operation"),
        ["-Recurse"] = (CommandRisk.RecursiveOperation, "Recursive operation"),

        // 强制操作
        ["-f"] = (CommandRisk.ForceOperation, "Force operation"),
        ["-force"] = (CommandRisk.ForceOperation, "Force operation"),
        ["-Force"] = (CommandRisk.ForceOperation, "Force operation"),
        ["/f"] = (CommandRisk.ForceOperation, "Force operation"),
        ["/F"] = (CommandRisk.ForceOperation, "Force operation"),
        ["/q"] = (CommandRisk.ForceOperation, "Quiet mode (no confirmation)"),
        ["/Q"] = (CommandRisk.ForceOperation, "Quiet mode (no confirmation)"),

        // 根目录操作
        ["/"] = (CommandRisk.PathEscape, "Root directory target"),
        ["C:\\"] = (CommandRisk.PathEscape, "System drive target"),
        ["C:/"] = (CommandRisk.PathEscape, "System drive target"),
        ["*"] = (CommandRisk.FileDeletion, "Wildcard deletion"),
        ["*."] = (CommandRisk.FileDeletion, "Wildcard deletion"),
        ["*.*"] = (CommandRisk.FileDeletion, "Wildcard deletion"),
    }.ToFrozenDictionary();

    // 危险模式组合
    private static readonly List<(string[] LowerPatterns, CommandRisk Risk, string Description)> DangerousCombinations = new()
    {
        (new[] { "rm", "-r", "-f", "/" }, CommandRisk.PathEscape, "Recursive force delete root"),
        (new[] { "rm", "-rf", "/" }, CommandRisk.PathEscape, "Recursive force delete root"),
        (new[] { "del", "/s", "/q" }, CommandRisk.RecursiveOperation, "Silent recursive delete"),
        (new[] { "erase", "/s", "/q" }, CommandRisk.RecursiveOperation, "Silent recursive delete"),
        (new[] { "format", "c:" }, CommandRisk.SystemModification, "Format system drive"),
        (new[] { "dd", "of=" }, CommandRisk.DataModification, "Direct disk write"),
        (new[] { "dd", "if=" }, CommandRisk.DataModification, "Direct disk read/write"),
        (new[] { "powershell", "-enc" }, CommandRisk.RemoteExecution, "Encoded command execution"),
        (new[] { "powershell", "-encodedcommand" }, CommandRisk.RemoteExecution, "Encoded command execution"),
        (new[] { "pwsh", "-enc" }, CommandRisk.RemoteExecution, "Encoded command execution"),
        (new[] { "|", "sh" }, CommandRisk.RemoteExecution, "Piped to shell"),
        (new[] { "|", "bash" }, CommandRisk.RemoteExecution, "Piped to bash"),
        (new[] { "|", "powershell" }, CommandRisk.RemoteExecution, "Piped to PowerShell"),
    };

    public DestructiveCommandResult Detect(ShellCommand command)
    {
        var risks = new List<CommandRisk>();
        var details = new List<string>();

        // 1. 检查命令名是否在破坏性命令列表中（支持前缀匹配，如 mkfs.ext4 匹配 mkfs）
        var matchedCommand = DestructiveCommands.Keys.FirstOrDefault(cmd =>
            command.CommandName.Equals(cmd, StringComparison.OrdinalIgnoreCase) ||
            command.CommandName.StartsWith(cmd + ".", StringComparison.OrdinalIgnoreCase) ||
            command.CommandName.StartsWith(cmd + " ", StringComparison.OrdinalIgnoreCase));

        if (matchedCommand != null)
        {
            var baseRisk = DestructiveCommands[matchedCommand];
            risks.Add(baseRisk);
            details.Add($"Command '{command.CommandName}' is inherently destructive");
        }

        // 2. 检查危险参数
        foreach (var arg in command.Arguments)
        {
            if (DangerousFlags.TryGetValue(arg, out var flagInfo))
            {
                risks.Add(flagInfo.Risk);
                details.Add($"Dangerous flag '{arg}': {flagInfo.Description}");
            }

            // 检查参数中的危险路径
            if (IsDangerousPath(arg))
            {
                risks.Add(CommandRisk.PathEscape);
                details.Add($"Dangerous path in argument: '{arg}'");
            }
        }

        // 3. 检查危险模式组合
        var rawLower = command.RawCommand.ToLowerInvariant();
        var matchedCombos = DangerousCombinations
            .Where(c => c.LowerPatterns.All(p => rawLower.Contains(p)))
            .ToList();
        risks.AddRange(matchedCombos.Select(c => c.Risk).ToList());
        details.AddRange(matchedCombos.Select(c => $"Dangerous pattern: {c.Description}").ToList());

        // 4. 特殊检查：Remove-Item 的 -Recurse -Force 组合（包括 -rf 这种组合参数）
        if (command.CommandName.Equals("Remove-Item", StringComparison.OrdinalIgnoreCase) ||
            command.CommandName.Equals("rm", StringComparison.OrdinalIgnoreCase) ||
            command.CommandName.Equals("del", StringComparison.OrdinalIgnoreCase) ||
            command.CommandName.Equals("erase", StringComparison.OrdinalIgnoreCase))
        {
            var hasRecurse = command.Arguments.Any(a =>
                a.Equals("-recurse", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-r", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-R", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/s", StringComparison.OrdinalIgnoreCase) ||
                a.Contains('r', StringComparison.OrdinalIgnoreCase) && a.StartsWith('-')); // 处理 -rf

            var hasForce = command.Arguments.Any(a =>
                a.Equals("-force", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/f", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("/q", StringComparison.OrdinalIgnoreCase) ||
                a.Contains('f', StringComparison.OrdinalIgnoreCase) && a.StartsWith('-')); // 处理 -rf

            if (hasRecurse && hasForce)
            {
                risks.Add(CommandRisk.RecursiveOperation);
                risks.Add(CommandRisk.ForceOperation);
                details.Add("Recursive + Force combination detected - extremely dangerous");
            }
        }

        // 去重风险
        var uniqueRisks = risks.Distinct().ToList();

        // 判断是否为破坏性命令：必须有真正的破坏性风险（不包括 PathEscape）
        var destructiveRisks = uniqueRisks.Where(r => r != CommandRisk.PathEscape).ToList();
        var isDestructive = destructiveRisks.Count > 0;

        return new DestructiveCommandResult(
            isDestructive,
            uniqueRisks,
            details.Count > 0 ? string.Join("; ", details) : null);
    }

    private static readonly string[] DangerousPaths =
    [
        "/", "C:\\", "C:/", "/*", "C:\\*", "C:/*",
        "/home", "/root", "/etc", "/usr", "/var",
        "~", "~/", "..", "../", "..\\"
    ];

    private static bool IsDangerousPath(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg))
        {
            return false;
        }

        return DangerousPaths.Any(dp =>
            arg.Equals(dp, StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith(dp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith(dp + "/", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith(dp + "\\", StringComparison.OrdinalIgnoreCase));
    }
}
