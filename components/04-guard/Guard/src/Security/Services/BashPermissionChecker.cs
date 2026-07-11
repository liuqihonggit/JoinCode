using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Bash 工具权限检查器实现 — 对齐 TS bashPermissions.ts
/// 核心链路: 安全验证 → 路径约束 → 只读检查 → 权限决策
/// </summary>
[Register]
public sealed partial class BashPermissionChecker : IBashPermissionChecker
{
    [Inject] private readonly IBashSecurityValidator _securityValidator;
    [Inject] private readonly IPathConstraintValidator _pathConstraintValidator;
    [Inject] private readonly IReadOnlyCommandDetector _readOnlyDetector;

    /// <summary>
    /// 最大子命令数量 — 对齐 TS MAX_SUBCOMMANDS_FOR_SECURITY_CHECK
    /// </summary>
    private const int MaxSubcommandsForSecurityCheck = 50;

    /// <summary>
    /// 安全环境变量白名单 — 对齐 TS SAFE_ENV_VARS
    /// </summary>
    private static readonly FrozenSet<string> SafeEnvVars = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "NODE_ENV", "PYTHONUNBUFFERED", "PYTHONDONTWRITEBYTECODE",
        "RUST_BACKTRACE", "RUST_LOG", "GOOS", "GOARCH", "CGO_ENABLED",
        "GO111MODULE", "GOEXPERIMENT", "LANG", "LC_ALL", "LC_CTYPE",
        "TERM", "TZ", "NO_COLOR", "FORCE_COLOR", "CLICOLOR",
        "LS_COLORS", "GREP_COLORS", ProviderEnvVar.AnthropicApiKey.ToValue(),
        "CUDA_VISIBLE_DEVICES", "DOCKER_HOST", "KUBECONFIG");

    /// <summary>
    /// 禁止作为前缀建议的命令 — 对齐 TS BARE_SHELL_PREFIXES
    /// </summary>
    private static readonly FrozenSet<string> BareShellPrefixes = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "sh", "bash", "zsh", "fish", "csh", "tcsh", "ksh", "dash",
        "cmd", "powershell", "pwsh", "env", "xargs",
        "nice", "stdbuf", "nohup", "timeout", "time",
        "sudo", "doas", "pkexec");

    /// <summary>
    /// 二进制劫持环境变量模式 — 对齐 TS BINARY_HIJACK_VARS
    /// </summary>
    private static readonly Regex BinaryHijackVarsPattern = new(
        @"^(LD_|DYLD_|PATH$)", RegexOptions.Compiled);

    /// <summary>
    /// 检查 Bash 命令的权限 — 主入口，对齐 TS bashToolHasPermission
    /// </summary>
    public BashPermissionResult CheckPermission(
        string command,
        string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new BashPermissionResult(PermissionBehavior.Passthrough);
        }

        var trimmed = command.Trim();

        // 1. 安全验证器检查（最高优先级）
        var securityResult = _securityValidator.Validate(trimmed);
        if (!securityResult.IsSafe)
        {
            return new BashPermissionResult(
                PermissionBehavior.Ask,
                Message: securityResult.Message ?? $"Security check failed: {securityResult.CheckId}",
                SuggestedRule: null);
        }

        // 2. 子命令拆分
        var subcommands = SplitSubcommands(trimmed);
        if (subcommands.Count > MaxSubcommandsForSecurityCheck)
        {
            return new BashPermissionResult(
                PermissionBehavior.Ask,
                Message: $"Command has too many subcommands ({subcommands.Count} > {MaxSubcommandsForSecurityCheck})");
        }

        // 3. 多重 cd 检查
        var cdCount = subcommands.Count(sc => IsCdCommand(sc));
        var compoundCommandHasCd = cdCount > 0;

        if (cdCount > 1)
        {
            return new BashPermissionResult(
                PermissionBehavior.Ask,
                Message: "Multiple cd commands in compound command require manual approval");
        }

        // 4. cd + git 复合命令安全检查
        if (compoundCommandHasCd && ContainsGitCommand(subcommands))
        {
            return new BashPermissionResult(
                PermissionBehavior.Ask,
                Message: "cd + git compound command requires manual approval");
        }

        // 5. 逐子命令检查
        foreach (var subcommand in subcommands)
        {
            // 5a. 路径约束检查
            var pathResult = _pathConstraintValidator.CheckPathConstraints(
                subcommand, workingDirectory, compoundCommandHasCd);
            if (pathResult.Behavior == PermissionBehavior.Deny)
            {
                return new BashPermissionResult(
                    PermissionBehavior.Deny,
                    Message: pathResult.Message,
                    SuggestedRule: null);
            }

            if (pathResult.Behavior == PermissionBehavior.Ask)
            {
                return new BashPermissionResult(
                    PermissionBehavior.Ask,
                    Message: pathResult.Message,
                    SuggestedRule: GenerateSuggestedRule(subcommand, pathResult));
            }

            // 5b. 只读检查
            var readOnlyResult = _readOnlyDetector.CheckReadOnlyConstraints(
                subcommand, compoundCommandHasCd);
            if (readOnlyResult.Behavior == PermissionBehavior.Ask)
            {
                return new BashPermissionResult(
                    PermissionBehavior.Ask,
                    Message: readOnlyResult.Message,
                    SuggestedRule: GenerateSuggestedRule(subcommand));
            }
        }

        // 6. 全部子命令都通过只读检查 → 允许
        var overallReadOnly = _readOnlyDetector.CheckReadOnlyConstraints(
            trimmed, compoundCommandHasCd);
        if (overallReadOnly.Behavior == PermissionBehavior.Allow)
        {
            return new BashPermissionResult(PermissionBehavior.Allow);
        }

        // 7. 无明确规则匹配 → 传递
        return new BashPermissionResult(
            PermissionBehavior.Passthrough,
            Message: "No explicit permission rule matched",
            SuggestedRule: GenerateSuggestedRule(trimmed));
    }

    #region 辅助方法

    /// <summary>
    /// 拆分子命令 — 对齐 TS splitCommand_DEPRECATED
    /// </summary>
    private static List<string> SplitSubcommands(string command)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                current.Append(c);
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                current.Append(c);
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                current.Append(c);
                continue;
            }

            // 分号分隔
            if (c == ';')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                continue;
            }

            // && 和 || 分隔
            if (c == '&' && i + 1 < command.Length && command[i + 1] == '&')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                i++; // 跳过第二个 &
                continue;
            }

            if (c == '|' && i + 1 < command.Length && command[i + 1] == '|')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                i++; // 跳过第二个 |
                continue;
            }

            // 管道分隔
            if (c == '|')
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            var last = current.ToString().Trim();
            if (last.Length > 0)
            {
                result.Add(last);
            }
        }

        return result;
    }

    /// <summary>
    /// 检查是否为 cd 命令 — 对齐 TS isNormalizedCdCommand
    /// </summary>
    private static bool IsCdCommand(string command)
    {
        var stripped = StripSafeWrappers(command);
        var spaceIdx = stripped.IndexOf(' ');
        var cmdName = spaceIdx >= 0 ? stripped[..spaceIdx] : stripped;
        return cmdName is "cd" or "pushd" or "popd";
    }

    /// <summary>
    /// 检查子命令列表是否包含 git 命令
    /// </summary>
    private static bool ContainsGitCommand(List<string> subcommands) =>
        subcommands.Any(sc =>
        {
            var stripped = StripSafeWrappers(sc);
            return stripped.StartsWith("git ", StringComparison.OrdinalIgnoreCase)
                || stripped.Equals("git", StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>
    /// 剥离安全包装命令 — 对齐 TS stripSafeWrappers（简化版）
    /// </summary>
    private static string StripSafeWrappers(string command)
    {
        var result = command.TrimStart();

        // 剥离前导安全环境变量
        while (result.Length > 0 && IsEnvVarAssignment(result))
        {
            var spaceIdx = result.IndexOf(' ');
            if (spaceIdx < 0) break;

            var varPart = result[..spaceIdx];
            var eqIdx = varPart.IndexOf('=');
            if (eqIdx < 0) break;

            var varName = varPart[..eqIdx];
            if (!SafeEnvVars.Contains(varName)) break;

            result = result[(spaceIdx + 1)..].TrimStart();
        }

        // 剥离包装命令
        var wrappers = new[] { "timeout", "time", "nice", "nohup", "stdbuf", "env" };
        foreach (var wrapper in wrappers)
        {
            if (result.StartsWith(wrapper + " ", StringComparison.OrdinalIgnoreCase))
            {
                result = result[(wrapper.Length + 1)..].TrimStart();
            }
        }

        return result;
    }

    /// <summary>
    /// 检查字符串开头是否为环境变量赋值
    /// </summary>
    private static bool IsEnvVarAssignment(string s)
    {
        var eqIdx = s.IndexOf('=');
        if (eqIdx <= 0) return false;

        // 检查变量名是否合法
        for (var i = 0; i < eqIdx; i++)
        {
            var c = s[i];
            if (i == 0 && !char.IsLetter(c) && c != '_') return false;
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }

        return true;
    }

    /// <summary>
    /// 生成建议规则 — 对齐 TS suggestionForPrefix / suggestionForExactCommand
    /// </summary>
    private static string GenerateSuggestedRule(
        string command,
        PathConstraintResult? pathResult = null)
    {
        var stripped = StripSafeWrappers(command);
        var spaceIdx = stripped.IndexOf(' ');
        var cmdName = spaceIdx >= 0 ? stripped[..spaceIdx] : stripped;

        // 禁止为 shell 前缀生成规则
        if (BareShellPrefixes.Contains(cmdName))
        {
            return command;
        }

        // 为路径约束生成前缀规则
        if (pathResult is not null && pathResult.OperationType is not null)
        {
            var prefix = GetSimpleCommandPrefix(stripped);
            if (prefix is not null)
            {
                return $"{prefix}:*";
            }
        }

        // 默认生成精确命令规则
        return command;
    }

    /// <summary>
    /// 获取简单命令前缀 — 对齐 TS getSimpleCommandPrefix
    /// </summary>
    private static string? GetSimpleCommandPrefix(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        foreach (var c in command)
        {
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

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        if (tokens.Count == 0) return null;

        // 第一个 token 是命令名
        var cmdName = tokens[0];

        // 禁止 shell 前缀
        if (BareShellPrefixes.Contains(cmdName)) return null;

        // 第二个 token 看起来像子命令（小写字母数字+连字符）
        if (tokens.Count > 1 && LooksLikeSubcommand(tokens[1]))
        {
            return $"{cmdName} {tokens[1]}";
        }

        return cmdName;
    }

    /// <summary>
    /// 检查 token 是否看起来像子命令
    /// </summary>
    private static bool LooksLikeSubcommand(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        if (token.StartsWith('-')) return false; // 标志不是子命令

        // 子命令应该是小写字母数字+连字符
        return token.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
            && token.Any(char.IsLetter);
    }

    #endregion
}
