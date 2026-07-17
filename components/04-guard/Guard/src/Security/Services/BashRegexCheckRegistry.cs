namespace JoinCode.Abstractions.Security.Shell;

public sealed record BashRegexCheckItem(
    BashSecurityCheckId CheckId,
    string Message,
    Func<string, BashSecurityResult> Validate);

public static class BashRegexCheckRegistry
{
    public static readonly BashRegexCheckItem[] All =
    [
        new(BashSecurityCheckId.ControlCharacters,
            "命令包含非打印控制字符，可能用于绕过安全检查",
            cmd => BashSecurityRegex.ControlCharRegex().IsMatch(cmd)
                ? Fail(BashSecurityCheckId.ControlCharacters, "命令包含非打印控制字符，可能用于绕过安全检查", true)
                : Safe()),

        new(BashSecurityCheckId.IncompleteCommands,
            "命令可能是不完整片段",
            cmd =>
            {
                var trimmed = cmd.TrimStart();
                if (cmd.Length > 0 && cmd[0] == '\t')
                    return Fail(BashSecurityCheckId.IncompleteCommands, "命令以制表符开头，可能是不完整片段", true);
                if (trimmed.StartsWith('-'))
                    return Fail(BashSecurityCheckId.IncompleteCommands, "命令以标志开头，可能是不完整片段", true);
                if (trimmed.StartsWith("&&") || trimmed.StartsWith("||") ||
                    trimmed.StartsWith(";") || trimmed.StartsWith(">>") ||
                    trimmed.StartsWith(">") || trimmed.StartsWith("<"))
                    return Fail(BashSecurityCheckId.IncompleteCommands, "命令以操作符开头，可能是续行", true);
                return Safe();
            }),

        new(BashSecurityCheckId.CommandSubstitution,
            "命令包含命令替换",
            cmd =>
            {
                if (HasUnescapedChar(cmd, '`'))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含反引号（`）用于命令替换", true);
                if (Regex.IsMatch(cmd, @"<\("))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含 进程替换 <()", true);
                if (Regex.IsMatch(cmd, @">\("))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含 进程替换 >()", true);
                if (Regex.IsMatch(cmd, @"\$\("))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含 $() 命令替换", true);
                if (Regex.IsMatch(cmd, @"\$\{"))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含 ${} 参数替换", true);
                if (Regex.IsMatch(cmd, @"\$\["))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含 $[] 旧式算术展开", true);
                return Safe();
            }),

        new(BashSecurityCheckId.IfsInjection,
            "命令包含IFS变量使用，可能绕过安全验证",
            cmd => (cmd.Contains("$IFS", StringComparison.Ordinal) ||
                    Regex.IsMatch(cmd, @"\$\{[^}]*IFS"))
                ? Fail(BashSecurityCheckId.IfsInjection, "命令包含IFS变量使用，可能绕过安全验证", true)
                : Safe()),

        new(BashSecurityCheckId.ProcEnvironAccess,
            "命令访问 /proc/*/environ，可能暴露敏感环境变量",
            cmd => BashSecurityRegex.ProcEnvironRegex().IsMatch(cmd)
                ? Fail(BashSecurityCheckId.ProcEnvironAccess, "命令访问 /proc/*/environ，可能暴露敏感环境变量")
                : Safe()),

        new(BashSecurityCheckId.ZshDangerousCommands,
            "命令使用Zsh特有命令，可能绕过安全检查",
            cmd =>
            {
                var trimmed = cmd.Trim();
                var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var baseCmd = "";
                foreach (var token in tokens)
                {
                    if (Regex.IsMatch(token, @"^[A-Za-z_]\w*=")) continue;
                    if (token is "command" or "builtin" or "noglob" or "nocorrect") continue;
                    baseCmd = token;
                    break;
                }
                if (BashSecurityConstants.ZshDangerousBuiltins.Contains(baseCmd))
                    return Fail(BashSecurityCheckId.ZshDangerousCommands, $"命令使用Zsh特有命令 '{baseCmd}'，可能绕过安全检查", true);
                if (baseCmd.Equals("fc", StringComparison.OrdinalIgnoreCase) &&
                    Regex.IsMatch(trimmed, @"\s-\S*e"))
                    return Fail(BashSecurityCheckId.ZshDangerousCommands, "命令使用 'fc -e'，可通过编辑器执行任意命令", true);
                return Safe();
            }),

        new(BashSecurityCheckId.BackslashEscapedWhitespace,
            "命令包含反斜杠转义空白，可能改变命令解析",
            cmd => BashSecurityRegex.BackslashWhitespaceRegex().IsMatch(cmd)
                ? Fail(BashSecurityCheckId.BackslashEscapedWhitespace, "命令包含反斜杠转义空白，可能改变命令解析", true)
                : Safe()),

        new(BashSecurityCheckId.BackslashEscapedOperators,
            "命令包含反斜杠转义操作符（;|&<>），可能隐藏命令结构",
            cmd => HasBackslashEscapedOperator(cmd)
                ? Fail(BashSecurityCheckId.BackslashEscapedOperators, "命令包含反斜杠转义操作符（;|&<>），可能隐藏命令结构", true)
                : Safe()),

        new(BashSecurityCheckId.UnicodeWhitespace,
            "命令包含Unicode空白字符，可能导致解析不一致",
            cmd => BashSecurityRegex.UnicodeWhitespaceRegex().IsMatch(cmd)
                ? Fail(BashSecurityCheckId.UnicodeWhitespace, "命令包含Unicode空白字符，可能导致解析不一致", true)
                : Safe()),

        new(BashSecurityCheckId.BraceExpansion,
            "命令包含花括号展开，可能改变命令解析",
            cmd => HasBraceExpansion(cmd)
                ? Fail(BashSecurityCheckId.BraceExpansion, "命令包含花括号展开，可能改变命令解析", true)
                : Safe()),

        new(BashSecurityCheckId.ObfuscatedFlags,
            "命令包含混淆标志，可能绕过安全检查",
            cmd => CheckObfuscatedFlags(cmd)),

        new(BashSecurityCheckId.Newlines,
            "命令包含换行符，可能分隔多个命令",
            cmd =>
            {
                if (!cmd.Contains('\n') && !cmd.Contains('\r')) return Safe();
                if (Regex.IsMatch(cmd, @"[\n\r]\s*\S"))
                    return Fail(BashSecurityCheckId.Newlines, "命令包含换行符，可能分隔多个命令");
                if (cmd.Contains('\r'))
                    return Fail(BashSecurityCheckId.Newlines, "命令包含回车符（\\r），shell解析器可能产生不同结果", true);
                return Safe();
            }),

        new(BashSecurityCheckId.InputRedirection,
            "命令包含重定向，可能读写任意文件",
            cmd => CheckRedirections(cmd)),
    ];

    private static readonly FrozenSet<char> ShellOperators = FrozenSet.Create(';', '|', '&', '<', '>');

    private static BashSecurityResult Fail(BashSecurityCheckId checkId, string message, bool isMisparsing = false)
        => new(false, checkId, message, isMisparsing);

    private static BashSecurityResult Safe()
        => new(true);

    private static bool HasUnescapedChar(string content, char ch)
    {
        var i = 0;
        while (i < content.Length)
        {
            if (content[i] == '\\' && i + 1 < content.Length)
            {
                i += 2;
                continue;
            }
            if (content[i] == ch) return true;
            i++;
        }
        return false;
    }

    private static bool HasBackslashEscapedOperator(string command)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (c == '\\' && !inSingleQuote)
            {
                if (!inDoubleQuote)
                {
                    if (i + 1 < command.Length && ShellOperators.Contains(command[i + 1]))
                        return true;
                }
                i++;
                continue;
            }
            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
        }
        return false;
    }

    private static bool HasBraceExpansion(string command)
    {
        for (var i = 0; i < command.Length; i++)
        {
            if (command[i] == '{' && !IsEscapedAtPosition(command, i))
            {
                var depth = 1;
                for (var j = i + 1; j < command.Length; j++)
                {
                    if (command[j] == '{' && !IsEscapedAtPosition(command, j)) depth++;
                    else if (command[j] == '}' && !IsEscapedAtPosition(command, j))
                    {
                        depth--;
                        if (depth == 0)
                        {
                            var inner = command.Substring(i + 1, j - i - 1);
                            if (inner.Contains(',') || inner.Contains(".."))
                                return true;
                            break;
                        }
                    }
                }
            }
        }
        return false;
    }

    private static BashSecurityResult CheckObfuscatedFlags(string command)
    {
        if (Regex.IsMatch(command, @"\$'[^']*'"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含ANSI-C引用，可能隐藏字符", true);
        if (Regex.IsMatch(command, @"\$""[^""]*"""))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含Locale引用，可能隐藏字符", true);
        if (Regex.IsMatch(command, @"(?:''|"""")+\s*-") ||
            Regex.IsMatch(command, @"(?:''|"""")+\s*['""]-"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含空引号后的破折号（潜在绕过）", true);
        if (Regex.IsMatch(command, @"(?:^|\s)['""]{3,}"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含连续引号字符（潜在混淆）", true);
        return Safe();
    }

    private static BashSecurityResult CheckRedirections(string command)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (c == '\\' && !inSingleQuote && i + 1 < command.Length)
            {
                i++;
                continue;
            }
            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
            if (!inSingleQuote && !inDoubleQuote)
            {
                if (c == '<')
                    return Fail(BashSecurityCheckId.InputRedirection, "命令包含输入重定向（<），可能读取敏感文件");
                if (c == '>')
                    return Fail(BashSecurityCheckId.OutputRedirection, "命令包含输出重定向（>），可能写入任意文件");
            }
        }
        return Safe();
    }

    private static bool IsEscapedAtPosition(string content, int pos)
    {
        var backslashCount = 0;
        var i = pos - 1;
        while (i >= 0 && content[i] == '\\')
        {
            backslashCount++;
            i--;
        }
        return backslashCount % 2 == 1;
    }
}
