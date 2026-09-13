namespace Services.Shell;

/// <summary>
/// sed 编辑解析器 — 对齐 TS sedEditParser.ts
/// 将 sed -i 命令解析为 SedEditInfo，支持在进程内模拟替换
/// </summary>
public static class SedEditParser
{
    /// <summary>
    /// 解析 sed 命令为编辑信息 — 对齐 TS parseSedEditCommand
    /// 仅支持 sed -i 's/pattern/replacement/flags' file 格式
    /// </summary>
    public static SedEditInfo? ParseSedEditCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;

        // 检查是否以 sed 开头
        var trimmed = command.TrimStart();
        if (!trimmed.StartsWith("sed ", StringComparison.OrdinalIgnoreCase)) return null;

        // 简单 Shell token 解析
        var tokens = TryParseShellTokens(trimmed[4..]);
        if (tokens is null || tokens.Count == 0) return null;

        // 检查 glob 模式
        if (tokens.Any(t => t.Contains('*') || t.Contains('?'))) return null;

        var hasInPlaceFlag = false;
        var extendedRegex = false;
        string? expression = null;
        string? filePath = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token is "-i" or "--in-place")
            {
                hasInPlaceFlag = true;
                // macOS 备份后缀: -i '' 或 -i.bak
                if (i + 1 < tokens.Count)
                {
                    var next = tokens[i + 1];
                    if (string.IsNullOrEmpty(next) || next.StartsWith('.'))
                    {
                        i++; // 跳过备份后缀参数
                    }
                }
            }
            else if (token.StartsWith("-i.") || token.StartsWith("--in-place="))
            {
                hasInPlaceFlag = true;
            }
            else if (token is "-E" or "-r" or "--regexp-extended")
            {
                extendedRegex = true;
            }
            else if (token is "-e" or "--expression")
            {
                if (i + 1 < tokens.Count)
                {
                    expression = tokens[++i];
                }
                else
                {
                    return null;
                }
            }
            else if (token.StartsWith("--expression="))
            {
                expression = token["--expression=".Length..];
            }
            else if (token.StartsWith("-"))
            {
                // 不认识的标志，安全起见拒绝
                return null;
            }
            else
            {
                // 非标志参数：第一个作为表达式，第二个作为文件路径
                if (expression is null)
                {
                    expression = token;
                }
                else if (filePath is null)
                {
                    filePath = token;
                }
                else
                {
                    // 超过2个非标志参数，太复杂
                    return null;
                }
            }
        }

        // 必须同时满足：有 -i 标志、有表达式、有文件路径
        if (!hasInPlaceFlag || expression is null || filePath is null) return null;

        // 解析替换表达式 s/pattern/replacement/flags
        return ParseSubstitutionExpression(expression, filePath, extendedRegex);
    }

    /// <summary>
    /// 解析替换表达式 — 对齐 TS parseSedEditCommand 中的状态机解析
    /// </summary>
    private static SedEditInfo? ParseSubstitutionExpression(string expression, string filePath, bool extendedRegex)
    {
        // 必须以 s/ 开头
        if (!expression.StartsWith("s/", StringComparison.Ordinal)) return null;

        var rest = expression[2..]; // 跳过 's/'
        var pattern = new StringBuilder();
        var replacement = new StringBuilder();
        var flags = new StringBuilder();
        var state = ParseState.Pattern;

        for (var i = 0; i < rest.Length; i++)
        {
            var c = rest[i];

            if (c == '\\' && i + 1 < rest.Length)
            {
                // 转义字符
                var next = rest[i + 1];
                switch (state)
                {
                    case ParseState.Pattern:
                        pattern.Append('\\');
                        pattern.Append(next);
                        break;
                    case ParseState.Replacement:
                        replacement.Append('\\');
                        replacement.Append(next);
                        break;
                    case ParseState.Flags:
                        // flags 中不允许转义
                        return null;
                }
                i++; // 跳过转义字符
                continue;
            }

            if (c == '/')
            {
                switch (state)
                {
                    case ParseState.Pattern:
                        state = ParseState.Replacement;
                        break;
                    case ParseState.Replacement:
                        state = ParseState.Flags;
                        break;
                    case ParseState.Flags:
                        // flags 中再遇到 / 是多余的
                        return null;
                }
                continue;
            }

            // 普通字符
            switch (state)
            {
                case ParseState.Pattern:
                    pattern.Append(c);
                    break;
                case ParseState.Replacement:
                    replacement.Append(c);
                    break;
                case ParseState.Flags:
                    flags.Append(c);
                    break;
            }
        }

        // 最终状态必须是 Flags（即至少有两个 / 分隔符）
        if (state != ParseState.Flags) return null;

        // 验证标志
        if (!IsValidSedFlags(flags.ToString())) return null;

        return new SedEditInfo
        {
            FilePath = filePath,
            Pattern = pattern.ToString(),
            Replacement = replacement.ToString(),
            Flags = flags.ToString(),
            ExtendedRegex = extendedRegex
        };
    }

    /// <summary>
    /// 验证 sed 标志 — 对齐 TS validFlags
    /// </summary>
    private static bool IsValidSedFlags(string flags)
    {
        if (string.IsNullOrEmpty(flags)) return true;

        // 仅允许 g, p, i, I, m, M, 1-9
        foreach (var c in flags)
        {
            if (c is not ('g' or 'p' or 'i' or 'I' or 'm' or 'M')
                && (c < '1' || c > '9'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 应用 sed 替换到文件内容 — 对齐 TS applySedSubstitution
    /// </summary>
    public static string ApplySedSubstitution(string content, SedEditInfo sedInfo)
    {
        try
        {
            var jsPattern = sedInfo.ExtendedRegex
                ? sedInfo.Pattern
                : ConvertBreToEre(sedInfo.Pattern);

            var jsReplacement = ConvertSedReplacement(sedInfo.Replacement);
            var regexFlags = BuildRegexFlags(sedInfo.Flags);

            var regex = new Regex(jsPattern, regexFlags, TimeSpan.FromSeconds(5));
            return regex.Replace(content, jsReplacement);
        }
        catch (RegexMatchTimeoutException)
        {
            return content;
        }
        catch (ArgumentException)
        {
            // 无效正则
            return content;
        }
    }

    /// <summary>
    /// BRE 到 ERE 转换 — 对齐 TS convertBreToEre
    /// </summary>
    private static string ConvertBreToEre(string pattern)
    {
        // 简化版 BRE→ERE 转换
        // 完整版需要4步占位符转换，这里处理最常见的情况
        var result = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            if (c == '\\' && i + 1 < pattern.Length)
            {
                var next = pattern[i + 1];
                switch (next)
                {
                    case '+': // BRE: \+ = 一个或多个 → ERE: +
                        result.Append('+');
                        i++;
                        break;
                    case '?': // BRE: \? = 零或一个 → ERE: ?
                        result.Append('?');
                        i++;
                        break;
                    case '(': // BRE: \( = 分组 → ERE: (
                        result.Append('(');
                        i++;
                        break;
                    case ')': // BRE: \) = 分组 → ERE: )
                        result.Append(')');
                        i++;
                        break;
                    case '|': // BRE: \| = 或 → ERE: |
                        result.Append('|');
                        i++;
                        break;
                    case '{': // BRE: \{ = 重复 → ERE: {
                        result.Append('{');
                        i++;
                        break;
                    case '}': // BRE: \} = 重复 → ERE: }
                        result.Append('}');
                        i++;
                        break;
                    default:
                        result.Append(c);
                        break;
                }
            }
            else
            {
                // BRE 中未转义的 + ? ( ) { } | 是字面量
                // 在 ERE/JS 中需要转义
                switch (c)
                {
                    case '+' or '?' or '(' or ')' or '{' or '}' or '|':
                        result.Append('\\');
                        result.Append(c);
                        break;
                    default:
                        result.Append(c);
                        break;
                }
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// 转换 sed 替换文本为 .NET 正则替换 — 对齐 TS convertSedReplacement
    /// </summary>
    private static string ConvertSedReplacement(string replacement)
    {
        var result = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            var c = replacement[i];

            if (c == '\\' && i + 1 < replacement.Length)
            {
                var next = replacement[i + 1];
                switch (next)
                {
                    case '/': // \/ → /
                        result.Append('/');
                        i++;
                        break;
                    case '&': // \& → 字面量 &
                        result.Append("&");
                        i++;
                        break;
                    case 'n': // \n → 换行
                        result.Append("\n");
                        i++;
                        break;
                    case 't': // \t → 制表符
                        result.Append("\t");
                        i++;
                        break;
                    default:
                        result.Append(c);
                        break;
                }
            }
            else if (c == '&')
            {
                // & → $& (完整匹配)
                result.Append("$&");
            }
            else if (c == '$')
            {
                // $ 在 .NET 替换中有特殊含义，需要转义
                result.Append("$$");
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// 构建 .NET 正则标志 — 对齐 TS buildRegexFlags
    /// </summary>
    private static RegexOptions BuildRegexFlags(string sedFlags)
    {
        var options = RegexOptions.None;

        foreach (var c in sedFlags)
        {
            switch (c)
            {
                case 'i' or 'I':
                    options |= RegexOptions.IgnoreCase;
                    break;
                case 'm' or 'M':
                    options |= RegexOptions.Multiline;
                    break;
                // g 标志在 .NET 中由 Regex.Replace 自动处理全局替换
            }
        }

        return options;
    }

    /// <summary>
    /// 简单 Shell token 解析 — 对齐 TS tryParseShellCommand
    /// 支持单引号、双引号和转义
    /// </summary>
    internal static List<string>? TryParseShellTokens(string command)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (c == '\\' && !inSingleQuote && i + 1 < command.Length)
            {
                current.Append(command[i + 1]);
                i++;
                continue;
            }

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

            if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
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

        if (inSingleQuote || inDoubleQuote) return null; // 未闭合的引号

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private enum ParseState
    {
        Pattern,
        Replacement,
        Flags
    }
}
