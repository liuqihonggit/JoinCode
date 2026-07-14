using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 只读命令检测器实现 — 深度对齐 TS readOnlyValidation.ts
/// 核心功能: 白名单标志验证 + 正则验证 + 变量扩展检测 + git 沙箱逃逸防护
/// </summary>
[Register]
public sealed partial class ReadOnlyCommandDetector : IReadOnlyCommandDetector
{
    /// <summary>
    /// 简单只读命令列表 — 对齐 TS READONLY_COMMANDS
    /// </summary>
    private static readonly FrozenSet<string> SimpleReadOnlyCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        // 时间日期
        "cal", "uptime",
        // 文件内容查看
        "cat", "head", "tail", "wc", "stat", "strings", "hexdump", "od", "nl",
        // 系统信息
        "id", "uname", "free", "df", "du", "locale", "groups", "nproc",
        // 路径信息
        "basename", "dirname", "realpath",
        // 文本处理
        "cut", "paste", "tr", "column", "tac", "rev", "fold", "expand", "unexpand",
        "fmt", "comm", "cmp", "numfmt",
        // 路径信息（附加）
        "readlink",
        // 文件比较
        "diff",
        // 布尔值
        "true", "false",
        // 杂项安全命令
        "sleep", "which", "type", "expr", "test", "getconf", "seq", "tsort", "pr",
        // 目录列表
        "ls", "dir", "ll", "la",
        // 搜索（find/grep/rg 走白名单标志验证，不在此列表）
        // 进程
        "ps", "top", "htop",
        // 网络
        "ping", "netstat", "ifconfig", "nslookup", "traceroute",
        // 版本
        "whoami", "pwd", "echo", "printenv", "env",
        // Git 只读
        "git");

    /// <summary>
    /// 安全的 Git 子命令 — 宽放模式：日常工作命令无条件放行
    /// </summary>
    private static readonly FrozenSet<string> SafeGitSubcommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "status", "log", "show", "diff", "branch", "tag", "remote", "config",
        "help", "version", "stash", "blame", "annotate", "describe",
        "shortlog", "reflog", "ls-files", "ls-tree", "ls-remote",
        "name-rev", "rev-parse", "rev-list", "merge-base",
        "cherry", "cherry-pick" /* --no-commit is read-only preview */,
        "grep", "whatchanged", "show-branch", "verify-pack",
        "cat-file", "for-each-ref", "worktree",
        "add", "commit", "mv", "restore", "switch", "checkout",
        "fetch", "pull", "merge", "rebase", "stash",
        "init", "clone", "submodule", "am", "apply", "notes");

    /// <summary>
    /// 危险的 Git 子命令 — 仅真正破坏性操作需确认
    /// </summary>
    private static readonly FrozenSet<string> DangerousGitSubcommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "push", "reset", "rm", "clean",
        "format-patch", "send-email", "filter-branch", "replace", "update-ref");

    /// <summary>
    /// xargs 自动批准的安全目标命令 — 对齐 TS SAFE_TARGET_COMMANDS_FOR_XARGS
    /// </summary>
    private static readonly FrozenSet<string> SafeXargsTargets = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "echo", "printf", "wc", "grep", "head", "tail");

    /// <summary>
    /// 命令白名单配置 — 对齐 TS COMMAND_ALLOWLIST
    /// </summary>
    private static readonly FrozenDictionary<string, CommandConfig> CommandAllowlist = BuildCommandAllowlist();

    /// <summary>
    /// Git 内部路径模式 — 对齐 TS GIT_INTERNAL_PATTERNS
    /// </summary>
    private static readonly Regex[] GitInternalPatterns =
    [
        new(@"^HEAD$", RegexOptions.Compiled),
        new(@"^objects(?:\/|$)", RegexOptions.Compiled),
        new(@"^refs(?:\/|$)", RegexOptions.Compiled),
        new(@"^hooks(?:\/|$)", RegexOptions.Compiled),
    ];

    /// <summary>
    /// 非创建型写入命令 — 对齐 TS NON_CREATING_WRITE_COMMANDS
    /// </summary>
    private static readonly FrozenSet<string> NonCreatingWriteCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "rm", "rmdir", "sed");

    /// <summary>
    /// Shell 元字符 — 用于检测注入
    /// </summary>
    private static readonly FrozenSet<char> ShellMetacharacters = FrozenSet.Create(
        '<', '>', '$', '`', '{', '}', '&', ';', '|', '#', '\\', '!', '\n', '\r');

    public bool IsReadOnly(ShellCommand command)
    {
        var result = CheckReadOnlyConstraints(command.RawCommand);
        return result.Behavior == PermissionBehavior.Allow;
    }

    /// <summary>
    /// 检查原始命令字符串是否只读 — 对齐 TS checkReadOnlyConstraints
    /// </summary>
    public ShellPermissionCheckResult CheckReadOnlyConstraints(string command, bool compoundCommandHasCd = false)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ShellPermissionCheckResult(PermissionBehavior.Passthrough);
        }

        var trimmed = command.Trim();

        // 1. 去除尾部 2>&1 重定向
        if (trimmed.EndsWith("2>&1", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^4].TrimEnd();
        }

        // 2. 检查 Windows UNC 路径
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return new ShellPermissionCheckResult(PermissionBehavior.Ask, "UNC path detected");
        }

        // 3. 检查未引用的变量扩展
        if (ContainsUnquotedExpansion(trimmed))
        {
            return new ShellPermissionCheckResult(PermissionBehavior.Passthrough);
        }

        // 4. 白名单标志验证
        if (IsCommandSafeViaFlagParsing(trimmed))
        {
            return new ShellPermissionCheckResult(PermissionBehavior.Allow);
        }

        // 5. 正则验证
        if (MatchesReadOnlyRegex(trimmed))
        {
            // 额外检查 git 命令的危险标志
            if (ContainsGitDangerousFlags(trimmed))
            {
                return new ShellPermissionCheckResult(PermissionBehavior.Passthrough);
            }

            return new ShellPermissionCheckResult(PermissionBehavior.Allow);
        }

        return new ShellPermissionCheckResult(PermissionBehavior.Passthrough);
    }

    /// <summary>
    /// 白名单标志验证 — 对齐 TS isCommandSafeViaFlagParsing
    /// </summary>
    private static bool IsCommandSafeViaFlagParsing(string command)
    {
        var tokens = SplitCommandTokens(command);
        if (tokens.Count == 0)
        {
            return false;
        }

        // 存在操作符（管道/重定向等）→ 不安全
        if (ContainsShellOperators(command))
        {
            return false;
        }

        var baseCommand = tokens[0];

        // 在白名单中查找匹配的命令配置（支持1/2/3-token键，如 "git config --get"）
        if (!CommandAllowlist.TryGetValue(baseCommand, out var config)
            && !CommandAllowlist.TryGetValue(string.Join(" ", tokens.Take(2).ToArray()), out config)
            && !CommandAllowlist.TryGetValue(string.Join(" ", tokens.Take(3).ToArray()), out config))
        {
            return false;
        }

        var args = tokens.Skip(1).ToList();

        // $ 变量扩展检测
        if (args.Any(arg => arg.Contains('$')))
        {
            return false;
        }

        // 花括号扩展检测
        if (args.Any(arg => arg.Contains('{') && (arg.Contains(',') || arg.Contains(".."))))
        {
            return false;
        }

        // 验证标志合法性
        if (!ValidateFlags(args, config.SafeFlags, config.RespectsDoubleDash))
        {
            return false;
        }

        // 检查正则
        if (config.Regex is not null && !config.Regex.IsMatch(command))
        {
            return false;
        }

        // 无正则时阻止反引号
        if (config.Regex is null && command.Contains('`'))
        {
            return false;
        }

        // 无正则时阻止 grep/rg 中的换行符
        if (config.Regex is null
            && (baseCommand.Equals("grep", StringComparison.OrdinalIgnoreCase)
                || baseCommand.Equals("rg", StringComparison.OrdinalIgnoreCase))
            && command.Contains('\n'))
        {
            return false;
        }

        // 额外危险回调
        if (config.AdditionalDangerousCallback is not null
            && config.AdditionalDangerousCallback(command, args))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 验证标志合法性 — 对齐 TS validateFlags
    /// </summary>
    private static bool ValidateFlags(
        IReadOnlyList<string> args,
        FrozenDictionary<string, FlagArgType> safeFlags,
        bool respectsDoubleDash)
    {
        var i = 0;
        var pastDelimiter = false;

        while (i < args.Count)
        {
            var arg = args[i];

            if (pastDelimiter)
            {
                // -- 之后全是位置参数，安全
                i++;
                continue;
            }

            if (arg == "--")
            {
                if (!respectsDoubleDash)
                {
                    return false;
                }

                pastDelimiter = true;
                i++;
                continue;
            }

            // 非标志参数（位置参数），安全
            if (!arg.StartsWith('-') || arg.Length == 1)
            {
                i++;
                continue;
            }

            // 长选项 --flag
            if (arg.StartsWith("--"))
            {
                // --flag=value 形式
                var eqIdx = arg.IndexOf('=');
                var flagName = eqIdx >= 0 ? arg[..eqIdx] : arg;

                if (!safeFlags.TryGetValue(flagName, out var flagType))
                {
                    return false; // 未知标志
                }

                if (flagType == FlagArgType.Required && eqIdx < 0)
                {
                    i += 2; // 跳过标志和值
                }
                else
                {
                    i++;
                }

                continue;
            }

            // 短选项 -abc (融合选项)
            for (var j = 1; j < arg.Length; j++)
            {
                var shortFlag = $"-{arg[j]}";
                if (!safeFlags.TryGetValue(shortFlag, out var flagType))
                {
                    return false; // 未知短选项
                }

                if (flagType == FlagArgType.Required)
                {
                    // 参数可能是融合的（如 -n5）或下一个 token
                    if (j + 1 < arg.Length)
                    {
                        break; // 融合参数，跳过剩余
                    }

                    i++; // 跳过下一个 token（参数值）
                    break;
                }
            }

            i++;
        }

        return true;
    }

    /// <summary>
    /// 正则验证 — 对齐 TS READONLY_COMMAND_REGEXES
    /// </summary>
    private static bool MatchesReadOnlyRegex(string command)
    {
        // 简单命令: 命令名后无 shell 元字符
        var spaceIdx = command.IndexOf(' ');
        var cmdName = spaceIdx >= 0 ? command[..spaceIdx] : command;

        if (SimpleReadOnlyCommands.Contains(cmdName))
        {
            // 检查无 shell 元字符
            if (!ContainsShellMetacharacters(command))
            {
                return true;
            }
        }

        // 特殊正则匹配
        return command is "pwd" or "whoami"
            || Regex.IsMatch(command, @"^echo(?:\s|$)")
            || Regex.IsMatch(command, @"^cd\s+")
            || Regex.IsMatch(command, @"^ls(?:\s|$)")
            || Regex.IsMatch(command, @"^find(?:\s|$)")
            || Regex.IsMatch(command, @"^node\s+-v$")
            || Regex.IsMatch(command, @"^node\s+--version$")
            || Regex.IsMatch(command, @"^python3?\s+--version$")
            || Regex.IsMatch(command, @"^history(?:\s+\d+)?\s*$")
            || Regex.IsMatch(command, @"^alias\s*$")
            || Regex.IsMatch(command, @"^arch(?:\s+(?:--help|-h))?\s*$")
            || Regex.IsMatch(command, @"^hostname(?:\s+(?:-[a-zA-Z]|--[a-zA-Z-]+))*\s*$");
    }

    /// <summary>
    /// 检查未引用的变量扩展 — 对齐 TS containsUnquotedExpansion
    /// </summary>
    private static bool ContainsUnquotedExpansion(string command)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            // 单引号内一切为字面量
            if (inSingleQuote)
            {
                continue;
            }

            // $ 变量扩展（双引号内也会扩展）
            if (c == '$' && i + 1 < command.Length
                && (char.IsLetterOrDigit(command[i + 1]) || command[i + 1] == '_' || command[i + 1] == '{'))
            {
                return true;
            }

            // 双引号外检查 glob
            if (!inDoubleQuote && (c is '?' or '*' || c == '['))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查 git 命令的危险标志 — 对齐 TS 中的额外检查
    /// </summary>
    private static bool ContainsGitDangerousFlags(string command)
    {
        if (!command.StartsWith("git", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // -c 可执行 git 命令
        if (command.Contains(" -c ", StringComparison.Ordinal)
            || command.Contains(" --exec-path", StringComparison.Ordinal)
            || command.Contains(" --config-env", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 检查是否包含 shell 操作符
    /// </summary>
    private static bool ContainsShellOperators(string command)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
            if (inSingleQuote || inDoubleQuote) continue;

            if (c is '|' or ';' or '&' or '<' or '>' or '`') return true;
            if (c == '>' && i + 1 < command.Length && command[i + 1] == '>') return true;
        }

        return false;
    }

    /// <summary>
    /// 检查是否包含 shell 元字符
    /// </summary>
    private static bool ContainsShellMetacharacters(string command)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
            if (inSingleQuote || inDoubleQuote) continue;

            if (ShellMetacharacters.Contains(c)) return true;
        }

        return false;
    }

    /// <summary>
    /// 分割命令为 token
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
}
