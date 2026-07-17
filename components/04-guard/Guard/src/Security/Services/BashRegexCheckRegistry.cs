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
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含进程替换 <()", true);
                if (Regex.IsMatch(cmd, @">\("))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含进程替换 >()", true);
                if (Regex.IsMatch(cmd, @"=\("))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含Zsh进程替换 =()", true);
                if (Regex.IsMatch(cmd, @"(?:^|[\s;&|])=[a-zA-Z_]"))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含Zsh equals展开(=cmd)", true);
                if (Regex.IsMatch(cmd, @"\$\("))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含$() 命令替换", true);
                if (Regex.IsMatch(cmd, @"\$\{"))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含${} 参数替换", true);
                if (Regex.IsMatch(cmd, @"\$\["))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含$[] 旧式算术展开", true);
                if (Regex.IsMatch(cmd, @"~\["))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含Zsh参数展开 ~[", true);
                if (Regex.IsMatch(cmd, @"\(e:"))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含Zsh glob限定符 (e:", true);
                if (Regex.IsMatch(cmd, @"\(\+"))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含Zsh glob限定符 (+", true);
                if (Regex.IsMatch(cmd, @"\}\s*always\s*\{"))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含Zsh always块", true);
                if (Regex.IsMatch(cmd, @"<#"))
                    return Fail(BashSecurityCheckId.CommandSubstitution, "命令包含PowerShell注释语法", true);
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
            cmd => HasBackslashEscapedWhitespace(cmd)
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
            cmd => CheckBraceExpansion(cmd)),

        new(BashSecurityCheckId.ObfuscatedFlags,
            "命令包含混淆标志，可能绕过安全检查",
            cmd => CheckObfuscatedFlags(cmd)),

        new(BashSecurityCheckId.ShellMetacharacters,
            "命令包含Shell元字符",
            cmd => CheckShellMetacharacters(cmd)),

        new(BashSecurityCheckId.DangerousVariables,
            "命令包含危险变量上下文",
            cmd => CheckDangerousVariables(cmd)),

        new(BashSecurityCheckId.MidWordHash,
            "命令包含词中井号",
            cmd => CheckMidWordHash(cmd)),

        new(BashSecurityCheckId.GitCommitSubstitution,
            "git commit消息包含命令替换",
            cmd => CheckGitCommit(cmd)),

        new(BashSecurityCheckId.Newlines,
            "命令包含换行符，可能分隔多个命令",
            cmd => CheckNewlines(cmd)),

        new(BashSecurityCheckId.InputRedirection,
            "命令包含重定向，可能读写任意文件",
            cmd => CheckRedirections(cmd)),

        new(BashSecurityCheckId.CommentQuoteDesync,
            "命令包含注释中引号，可能导致引号追踪失同步",
            cmd => CheckCommentQuoteDesync(cmd)),

        new(BashSecurityCheckId.QuotedNewline,
            "命令包含引号内换行+井号，可能隐藏参数",
            cmd => CheckQuotedNewline(cmd)),
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

    private static bool HasBackslashEscapedWhitespace(string command)
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
                    if (i + 1 < command.Length && (command[i + 1] == ' ' || command[i + 1] == '\t'))
                        return true;
                }
                i++;
                continue;
            }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
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

    private static BashSecurityResult CheckBraceExpansion(string command)
    {
        var fullyUnquoted = ExtractFullyUnquoted(command);

        var unescapedOpenBraces = 0;
        var unescapedCloseBraces = 0;
        for (var i = 0; i < fullyUnquoted.Length; i++)
        {
            if (fullyUnquoted[i] == '{' && !IsEscapedAtPosition(fullyUnquoted, i))
                unescapedOpenBraces++;
            else if (fullyUnquoted[i] == '}' && !IsEscapedAtPosition(fullyUnquoted, i))
                unescapedCloseBraces++;
        }

        if (unescapedOpenBraces > 0 && unescapedCloseBraces > unescapedOpenBraces)
            return Fail(BashSecurityCheckId.BraceExpansion,
                "命令包含引号剥离后多余闭合花括号，可能存在花括号展开混淆", true);

        if (unescapedOpenBraces > 0 && Regex.IsMatch(command, @"['""][{}]['""]"))
            return Fail(BashSecurityCheckId.BraceExpansion,
                "命令包含花括号上下文中的引号花括号字符（潜在花括号展开混淆）", true);

        for (var i = 0; i < fullyUnquoted.Length; i++)
        {
            if (fullyUnquoted[i] != '{') continue;
            if (IsEscapedAtPosition(fullyUnquoted, i)) continue;

            var depth = 1;
            var matchingClose = -1;
            for (var j = i + 1; j < fullyUnquoted.Length; j++)
            {
                if (fullyUnquoted[j] == '{' && !IsEscapedAtPosition(fullyUnquoted, j)) depth++;
                else if (fullyUnquoted[j] == '}' && !IsEscapedAtPosition(fullyUnquoted, j))
                {
                    depth--;
                    if (depth == 0)
                    {
                        matchingClose = j;
                        break;
                    }
                }
            }

            if (matchingClose == -1) continue;

            var innerDepth = 0;
            for (var k = i + 1; k < matchingClose; k++)
            {
                var ch = fullyUnquoted[k];
                if (ch == '{' && !IsEscapedAtPosition(fullyUnquoted, k)) innerDepth++;
                else if (ch == '}' && !IsEscapedAtPosition(fullyUnquoted, k)) innerDepth--;
                else if (innerDepth == 0)
                {
                    if (ch == ',' || (ch == '.' && k + 1 < matchingClose && fullyUnquoted[k + 1] == '.'))
                        return Fail(BashSecurityCheckId.BraceExpansion,
                            "命令包含花括号展开，可能改变命令解析", true);
                }
            }
        }

        return Safe();
    }

    private static BashSecurityResult CheckObfuscatedFlags(string command)
    {
        var baseCmd = ExtractBaseCommand(command);
        var hasShellOps = Regex.IsMatch(command, @"[|&;]");

        if (baseCmd.Equals("echo", StringComparison.OrdinalIgnoreCase) && !hasShellOps)
            return Safe();

        if (Regex.IsMatch(command, @"\$'[^']*'"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含ANSI-C引用，可能隐藏字符", true);

        if (Regex.IsMatch(command, @"\$""[^""]*"""))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含Locale引用，可能隐藏字符", true);

        if (Regex.IsMatch(command, @"\$['""]{2}\s*-"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含空特殊引号+破折号（潜在绕过）", true);

        if (Regex.IsMatch(command, @"(?:^|\s)(?:''|"""")+\s*-"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含空引号+破折号（潜在绕过）", true);

        if (Regex.IsMatch(command, @"(?:""""|'')+['""]-"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含空引号对+引号内破折号（潜在标志混淆）", true);

        if (Regex.IsMatch(command, @"(?:^|\s)['""]{3,}"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含连续引号字符（潜在混淆）", true);

        var quoteScanResult = ScanForQuotedFlags(command, baseCmd);
        if (!quoteScanResult.IsSafe) return quoteScanResult;

        var fullyUnquoted = ExtractFullyUnquoted(command);
        if (Regex.IsMatch(fullyUnquoted, @"\s['""]-"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含引号内标志名", true);

        if (Regex.IsMatch(fullyUnquoted, @"['""]{2}-"))
            return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含引号内标志名", true);

        return Safe();
    }

    private static BashSecurityResult ScanForQuotedFlags(string command, string baseCmd)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (var i = 0; i < command.Length - 1; i++)
        {
            var currentChar = command[i];
            var nextChar = command[i + 1];

            if (escaped) { escaped = false; continue; }

            if (currentChar == '\\' && !inSingleQuote) { escaped = true; continue; }

            if (currentChar == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (currentChar == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }

            if (inSingleQuote || inDoubleQuote) continue;

            if (char.IsWhiteSpace(currentChar) && nextChar is '\'' or '"' or '`')
            {
                var quoteChar = nextChar;
                var j = i + 2;
                var insideQuoteBuilder = new StringBuilder();

                while (j < command.Length && command[j] != quoteChar)
                {
                    insideQuoteBuilder.Append(command[j]);
                    j++;
                }

                var insideQuote = insideQuoteBuilder.ToString();

                var charAfterQuote = j + 1 < command.Length ? command[j + 1] : (char?)null;

                var hasFlagCharsInside = Regex.IsMatch(insideQuote, @"^-+[a-zA-Z0-9$`]");

                var hasFlagCharsContinuing = Regex.IsMatch(insideQuote, @"^-+$") &&
                    charAfterQuote.HasValue &&
                    Regex.IsMatch(charAfterQuote.GetValueOrDefault().ToString(), @"[a-zA-Z0-9\\${`-]");

                var hasFlagCharsInNextQuote = (insideQuote == "" || Regex.IsMatch(insideQuote, @"^-+$")) &&
                    charAfterQuote is not null &&
                    (charAfterQuote is '\'' or '"' or '`') &&
                    CheckChainedQuoteFlags(command, j + 1, insideQuote);

                if (j < command.Length && command[j] == quoteChar &&
                    (hasFlagCharsInside || hasFlagCharsContinuing || hasFlagCharsInNextQuote))
                {
                    return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含引号内标志名", true);
                }
            }

            if (char.IsWhiteSpace(currentChar) && nextChar == '-')
            {
                var j = i + 1;
                var flagContentBuilder = new StringBuilder();

                while (j < command.Length)
                {
                    var flagChar = command[j];

                    if (char.IsWhiteSpace(flagChar) || flagChar == '=') break;

                    if (flagChar is '\'' or '"' or '`')
                    {
                        if (baseCmd.Equals("cut", StringComparison.OrdinalIgnoreCase) &&
                            flagContentBuilder.ToString() == "-d")
                            break;

                        if (j + 1 < command.Length)
                        {
                            var nextFlagChar = command[j + 1];
                            if (!Regex.IsMatch(nextFlagChar.ToString(), @"[a-zA-Z0-9_'""-]"))
                                break;
                        }
                    }

                    flagContentBuilder.Append(flagChar);
                    j++;
                }

                var flagContent = flagContentBuilder.ToString();
                if (flagContent.Contains('\'') || flagContent.Contains('"'))
                    return Fail(BashSecurityCheckId.ObfuscatedFlags, "命令包含引号内标志名", true);
            }
        }

        return Safe();
    }

    private static bool CheckChainedQuoteFlags(string command, int startPos, string initialContent)
    {
        var pos = startPos;
        var combinedBuilder = new StringBuilder(initialContent);

        while (pos < command.Length && (command[pos] is '\'' or '"' or '`'))
        {
            var segQuote = command[pos];
            var end = pos + 1;
            while (end < command.Length && command[end] != segQuote)
                end++;

            var segment = command.Substring(pos + 1, end - pos - 1);
            var priorLength = combinedBuilder.Length;
            combinedBuilder.Append(segment);
            var combinedContent = combinedBuilder.ToString();

            if (Regex.IsMatch(combinedContent, @"^-+[a-zA-Z0-9$`]")) return true;

            var priorContent = priorLength > 0
                ? combinedContent[..priorLength]
                : combinedContent;
            if (Regex.IsMatch(priorContent, @"^-+$") && Regex.IsMatch(segment, @"[a-zA-Z0-9$`]"))
                return true;

            if (end >= command.Length) break;
            pos = end + 1;
        }

        var finalContent = combinedBuilder.ToString();
        if (pos < command.Length && Regex.IsMatch(command[pos].ToString(), @"[a-zA-Z0-9\\${`-]"))
        {
            if (Regex.IsMatch(finalContent, @"^-+$") || finalContent == "")
            {
                if (command[pos] == '-') return true;
                if (Regex.IsMatch(command[pos].ToString(), @"[a-zA-Z0-9\\${`]") && finalContent != "")
                    return true;
            }
            if (Regex.IsMatch(finalContent, @"^-")) return true;
        }

        return false;
    }

    private static BashSecurityResult CheckShellMetacharacters(string command)
    {
        var unquotedContent = ExtractWithDoubleQuotes(command);
        var message = "命令参数中包含Shell元字符（;、|或&）";

        if (Regex.IsMatch(unquotedContent, @"(?:^|\s)[""'][^""']*[;&][^""']*[""'](?:\s|$)"))
            return Fail(BashSecurityCheckId.ShellMetacharacters, message);

        if (Regex.IsMatch(unquotedContent, @"-name\s+[""'][^""']*[;|&][^""']*[""']") ||
            Regex.IsMatch(unquotedContent, @"-path\s+[""'][^""']*[;|&][^""']*[""']") ||
            Regex.IsMatch(unquotedContent, @"-iname\s+[""'][^""']*[;|&][^""']*[""']"))
            return Fail(BashSecurityCheckId.ShellMetacharacters, message);

        if (Regex.IsMatch(unquotedContent, @"-regex\s+[""'][^""']*[;&][^""']*[""']"))
            return Fail(BashSecurityCheckId.ShellMetacharacters, message);

        return Safe();
    }

    private static BashSecurityResult CheckDangerousVariables(string command)
    {
        var fullyUnquoted = ExtractFullyUnquoted(command);

        if (Regex.IsMatch(fullyUnquoted, @"[<>|]\s*\$[A-Za-z_]") ||
            Regex.IsMatch(fullyUnquoted, @"\$[A-Za-z_][A-Za-z0-9_]*\s*[|<>]"))
            return Fail(BashSecurityCheckId.DangerousVariables,
                "命令包含重定向或管道上下文中的变量（危险变量上下文）");

        return Safe();
    }

    private static BashSecurityResult CheckMidWordHash(string command)
    {
        var unquotedKeepQuoteChars = ExtractUnquotedKeepQuoteChars(command);

        if (HasMidWordHash(unquotedKeepQuoteChars))
            return Fail(BashSecurityCheckId.MidWordHash,
                "命令包含词中#，shell-quote与bash解析不同", true);

        var joined = JoinContinuations(unquotedKeepQuoteChars);
        if (HasMidWordHash(joined))
            return Fail(BashSecurityCheckId.MidWordHash,
                "命令包含词中#（续行合并后），shell-quote与bash解析不同", true);

        return Safe();
    }

    private static bool HasMidWordHash(string content)
    {
        for (var i = 1; i < content.Length; i++)
        {
            if (content[i] != '#') continue;
            if (!char.IsWhiteSpace(content[i - 1])) continue;

            if (i >= 2 && content[i - 2] == '$' && content[i - 1] == '{')
                continue;

            return true;
        }
        return false;
    }

    private static string JoinContinuations(string content)
    {
        var result = new StringBuilder(content.Length);
        var i = 0;
        while (i < content.Length)
        {
            if (content[i] == '\\' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                var backslashCount = 1;
                var j = i + 2;
                while (j + 1 < content.Length && content[j] == '\\' && content[j + 1] == '\n')
                {
                    backslashCount++;
                    j += 2;
                }
                if (backslashCount % 2 == 1)
                {
                    for (var k = 0; k < backslashCount - 1; k++)
                        result.Append('\\');
                }
                else
                {
                    for (var k = 0; k < backslashCount; k++)
                        result.Append('\\');
                    result.Append('\n');
                }
                i = j;
            }
            else
            {
                result.Append(content[i]);
                i++;
            }
        }
        return result.ToString();
    }

    private static BashSecurityResult CheckGitCommit(string command)
    {
        var trimmed = command.TrimStart();
        if (!trimmed.StartsWith("git ", StringComparison.OrdinalIgnoreCase)) return Safe();

        var restAfterGit = trimmed.AsSpan(4).TrimStart();
        if (!restAfterGit.StartsWith("commit ", StringComparison.OrdinalIgnoreCase)) return Safe();

        if (command.Contains('\\'))
            return Safe();

        var match = Regex.Match(command,
            @"^git[ \t]+commit[ \t]+[^;&|`$<>()\n\r]*?-m[ \t]+([""'])([\s\S]*?)\1(.*)$");
        if (!match.Success) return Safe();

        var quote = match.Groups[1].Value;
        var messageContent = match.Groups[2].Value;
        var remainder = match.Groups[3].Value;

        if (quote == "\"" && messageContent.Length > 0 &&
            Regex.IsMatch(messageContent, @"\$\(|`|\$\{"))
            return Fail(BashSecurityCheckId.GitCommitSubstitution,
                "git commit消息包含命令替换模式");

        if (remainder.Length > 0 && Regex.IsMatch(remainder, @"[;|&()`]|\$\(|\$\{"))
            return Safe();

        if (remainder.Length > 0)
        {
            var unquotedRemainder = ExtractUnquotedSimple(remainder);
            if (unquotedRemainder.Contains('<') || unquotedRemainder.Contains('>'))
                return Safe();
        }

        if (messageContent.Length > 0 && messageContent.StartsWith('-'))
            return Fail(BashSecurityCheckId.ObfuscatedFlags,
                "命令包含引号内标志名", true);

        return Safe();
    }

    private static BashSecurityResult CheckNewlines(string command)
    {
        if (!command.Contains('\n') && !command.Contains('\r')) return Safe();

        var fullyUnquoted = ExtractFullyUnquoted(command);

        if (!fullyUnquoted.Contains('\n') && !fullyUnquoted.Contains('\r'))
            return Safe();

        if (HasNonContinuationNewline(fullyUnquoted))
            return Fail(BashSecurityCheckId.Newlines, "命令包含换行符，可能分隔多个命令");

        if (command.Contains('\r') && HasUnquotedCarriageReturn(command))
            return Fail(BashSecurityCheckId.Newlines, "命令包含回车符（\\r），shell解析器可能产生不同结果", true);

        return Safe();
    }

    private static bool HasNonContinuationNewline(string content)
    {
        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (c != '\n' && c != '\r') continue;

            if (i > 0 && content[i - 1] == '\\' && char.IsWhiteSpace(content[i - 2]))
                continue;

            for (var j = i + 1; j < content.Length; j++)
            {
                if (content[j] == '\n' || content[j] == '\r') break;
                if (!char.IsWhiteSpace(content[j]))
                    return true;
            }
        }
        return false;
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

    private static BashSecurityResult CheckCommentQuoteDesync(string command)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && !inSingleQuote) { escaped = true; continue; }
            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }

            if (c == '#' && !inSingleQuote && !inDoubleQuote)
            {
                var lineEnd = command.IndexOf('\n', i);
                var commentText = lineEnd == -1
                    ? command.Substring(i + 1)
                    : command.Substring(i + 1, lineEnd - i - 1);
                if (commentText.Contains('\'') || commentText.Contains('"'))
                    return Fail(BashSecurityCheckId.CommentQuoteDesync,
                        "命令包含注释中引号字符，可能导致引号追踪失同步", true);
                if (lineEnd == -1) break;
                i = lineEnd;
            }
        }

        return Safe();
    }

    private static BashSecurityResult CheckQuotedNewline(string command)
    {
        if (!command.Contains('\n') || !command.Contains('#'))
            return Safe();

        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && !inSingleQuote) { escaped = true; continue; }
            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }

            if (c == '\n' && (inSingleQuote || inDoubleQuote))
            {
                var lineStart = i + 1;
                var nextNewline = command.IndexOf('\n', lineStart);
                var lineEnd = nextNewline == -1 ? command.Length : nextNewline;
                var nextLine = command.Substring(lineStart, lineEnd - lineStart);
                if (nextLine.TrimStart().StartsWith('#'))
                    return Fail(BashSecurityCheckId.QuotedNewline,
                        "命令包含引号内换行后跟井号行，可能从基于行的权限检查中隐藏参数", true);
            }
        }

        return Safe();
    }

    private static bool HasUnquotedCarriageReturn(string command)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && !inSingleQuote) { escaped = true; continue; }
            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }

            if (c == '\r' && !inDoubleQuote)
                return true;
        }

        return false;
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

    private static string ExtractBaseCommand(string command)
    {
        var trimmed = command.Trim();
        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (Regex.IsMatch(token, @"^[A-Za-z_]\w*=")) continue;
            if (token is "command" or "builtin" or "noglob" or "nocorrect") continue;
            return token;
        }
        return "";
    }

    private static string ExtractFullyUnquoted(string command)
    {
        var result = new StringBuilder(command.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && !inSingleQuote) { escaped = true; continue; }
            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
            if (!inSingleQuote && !inDoubleQuote) result.Append(c);
        }

        return result.ToString();
    }

    private static string ExtractWithDoubleQuotes(string command)
    {
        var result = new StringBuilder(command.Length);
        var inSingleQuote = false;
        var escaped = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (escaped) { escaped = false; if (!inSingleQuote) result.Append(c); continue; }
            if (c == '\\' && !inSingleQuote) { escaped = true; if (!inSingleQuote) result.Append(c); continue; }
            if (c == '\'') { inSingleQuote = !inSingleQuote; continue; }
            if (!inSingleQuote) result.Append(c);
        }

        return result.ToString();
    }

    private static string ExtractUnquotedKeepQuoteChars(string command)
    {
        var result = new StringBuilder(command.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (escaped) { escaped = false; if (!inSingleQuote && !inDoubleQuote) result.Append(c); continue; }
            if (c == '\\' && !inSingleQuote) { escaped = true; if (!inSingleQuote && !inDoubleQuote) result.Append(c); continue; }
            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; result.Append(c); continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; result.Append(c); continue; }
            if (!inSingleQuote && !inDoubleQuote) result.Append(c);
        }

        return result.ToString();
    }

    private static string ExtractUnquotedSimple(string text)
    {
        var result = new StringBuilder(text.Length);
        var inSQ = false;
        var inDQ = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'' && !inDQ) { inSQ = !inSQ; continue; }
            if (c == '"' && !inSQ) { inDQ = !inDQ; continue; }
            if (!inSQ && !inDQ) result.Append(c);
        }
        return result.ToString();
    }
}
