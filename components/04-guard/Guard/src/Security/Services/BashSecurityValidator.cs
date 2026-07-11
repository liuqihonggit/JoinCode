using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Bash安全验证器实现
/// 对齐 TS bashSecurity.ts 的核心验证器集合
///
/// 集成策略：优先使用 AST 安全步行器（FAIL-CLOSED），
/// 回退到 regex 检查（兼容性）
/// </summary>
[Register]
public sealed partial class BashSecurityValidator : IBashSecurityValidator
{
    [Inject] private readonly IBashAstSecurityWalker _astWalker;
    // 命令替换模式（对齐 TS COMMAND_SUBSTITUTION_PATTERNS）
    private static readonly (Regex Pattern, string Message)[] CommandSubstitutionPatterns =
    [
        (new Regex(@"<\(", RegexOptions.Compiled), "进程替换 <()"),
        (new Regex(@">\(", RegexOptions.Compiled), "进程替换 >()"),
        (new Regex(@"\$\(", RegexOptions.Compiled), "$() 命令替换"),
        (new Regex(@"\$\{", RegexOptions.Compiled), "${} 参数替换"),
        (new Regex(@"\$\[", RegexOptions.Compiled), "$[] 旧式算术展开"),
    ];

    // Zsh危险命令（对齐 TS ZSH_DANGEROUS_COMMANDS）
    private static readonly FrozenSet<string> ZshDangerousCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "zmodload", "emulate", "sysopen", "sysread", "syswrite", "sysseek",
        "zpty", "ztcp", "zsocket", "mapfile",
        "zf_rm", "zf_mv", "zf_ln", "zf_chmod", "zf_chown", "zf_mkdir", "zf_rmdir", "zf_chgrp");

    // 控制字符正则（对齐 TS CONTROL_CHAR_RE）
    private static readonly Regex ControlCharRegex = new(
        @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

    // Unicode空白字符（对齐 TS UNICODE_WS_RE）
    private static readonly Regex UnicodeWhitespaceRegex = new(
        @"[\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]", RegexOptions.Compiled);

    // Shell操作符（对齐 TS SHELL_OPERATORS）
    private static readonly FrozenSet<char> ShellOperators = FrozenSet.Create(';', '|', '&', '<', '>');

    public BashSecurityResult Validate(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new BashSecurityResult(true);
        }

        // 0. 优先使用 AST 安全步行器（FAIL-CLOSED，对齐 TS ast.ts parseForSecurity）
        var astResult = _astWalker.ParseForSecurity(command);
        if (astResult is BashAstSecurityResult.Simple simple)
        {
            // AST 步行器确认命令可静态分析 → 运行语义检查
            var semanticResult = _astWalker.CheckSemantics(simple.Commands);
            if (!semanticResult.IsOk)
            {
                return new BashSecurityResult(false,
                    semanticResult.CheckId,
                    semanticResult.Reason,
                    false);
            }

            // 语义检查通过 → 安全
            return new BashSecurityResult(true);
        }

        // AST 返回 TooComplex 或 ParseUnavailable → 回退到 regex 检查
        // 保留 regex 检查作为兼容性回退，因为 AST 步行器可能过于保守

        // 1. 控制字符检查（P0，最高优先级）
        var controlResult = ValidateControlCharacters(command);
        if (!controlResult.IsSafe) return controlResult;

        // 2. 不完整命令检查
        var incompleteResult = ValidateIncompleteCommands(command);
        if (!incompleteResult.IsSafe) return incompleteResult;

        // 3. 命令替换/注入检查
        var substitutionResult = ValidateCommandSubstitution(command);
        if (!substitutionResult.IsSafe) return substitutionResult;

        // 4. IFS注入检查
        var ifsResult = ValidateIfsInjection(command);
        if (!ifsResult.IsSafe) return ifsResult;

        // 5. /proc/environ访问检查
        var procResult = ValidateProcEnvironAccess(command);
        if (!procResult.IsSafe) return procResult;

        // 6. Zsh危险命令检查
        var zshResult = ValidateZshDangerousCommands(command);
        if (!zshResult.IsSafe) return zshResult;

        // 7. 反斜杠转义空白检查
        var backslashWsResult = ValidateBackslashEscapedWhitespace(command);
        if (!backslashWsResult.IsSafe) return backslashWsResult;

        // 8. 反斜杠转义操作符检查
        var backslashOpResult = ValidateBackslashEscapedOperators(command);
        if (!backslashOpResult.IsSafe) return backslashOpResult;

        // 9. Unicode空白检查
        var unicodeWsResult = ValidateUnicodeWhitespace(command);
        if (!unicodeWsResult.IsSafe) return unicodeWsResult;

        // 10. 花括号展开检查
        var braceResult = ValidateBraceExpansion(command);
        if (!braceResult.IsSafe) return braceResult;

        // 11. 混淆标志检查
        var obfuscatedResult = ValidateObfuscatedFlags(command);
        if (!obfuscatedResult.IsSafe) return obfuscatedResult;

        // 12. 换行符检查
        var newlineResult = ValidateNewlines(command);
        if (!newlineResult.IsSafe) return newlineResult;

        // 13. 重定向检查
        var redirectResult = ValidateRedirections(command);
        if (!redirectResult.IsSafe) return redirectResult;

        return new BashSecurityResult(true);
    }

    /// <summary>
    /// 控制字符检查 — 非打印控制字符可用于绕过安全检查
    /// </summary>
    private static BashSecurityResult ValidateControlCharacters(string command)
    {
        if (ControlCharRegex.IsMatch(command))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.ControlCharacters,
                "命令包含非打印控制字符，可能用于绕过安全检查", true);
        }
        return new BashSecurityResult(true);
    }

    /// <summary>
    /// 不完整命令检查 — 以tab/标志/操作符开头的命令可能是片段
    /// </summary>
    private static BashSecurityResult ValidateIncompleteCommands(string command)
    {
        var trimmed = command.TrimStart();

        if (command.Length > 0 && command[0] == '\t')
        {
            return new BashSecurityResult(false, BashSecurityCheckId.IncompleteCommands,
                "命令以制表符开头，可能是不完整片段", true);
        }

        if (trimmed.StartsWith('-'))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.IncompleteCommands,
                "命令以标志开头，可能是不完整片段", true);
        }

        if (trimmed.StartsWith("&&") || trimmed.StartsWith("||") ||
            trimmed.StartsWith(";") || trimmed.StartsWith(">>") ||
            trimmed.StartsWith(">") || trimmed.StartsWith("<"))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.IncompleteCommands,
                "命令以操作符开头，可能是续行", true);
        }

        return new BashSecurityResult(true);
    }

    /// <summary>
    /// 命令替换检查 — $()、${}、反引号等可执行任意命令
    /// </summary>
    private static BashSecurityResult ValidateCommandSubstitution(string command)
    {
        // 检查未转义的反引号
        if (HasUnescapedChar(command, '`'))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.CommandSubstitution,
                "命令包含反引号（`）用于命令替换", true);
        }

        foreach (var (pattern, message) in CommandSubstitutionPatterns)
        {
            if (pattern.IsMatch(command))
            {
                return new BashSecurityResult(false, BashSecurityCheckId.CommandSubstitution,
                    $"命令包含 {message}", true);
            }
        }

        return new BashSecurityResult(true);
    }

    /// <summary>
    /// IFS注入检查 — 修改IFS变量可绕过安全验证
    /// </summary>
    private static BashSecurityResult ValidateIfsInjection(string command)
    {
        if (command.Contains("$IFS", StringComparison.Ordinal) ||
            Regex.IsMatch(command, @"\$\{[^}]*IFS"))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.IfsInjection,
                "命令包含IFS变量使用，可能绕过安全验证", true);
        }
        return new BashSecurityResult(true);
    }

    /// <summary>
    /// /proc/environ访问检查 — 可暴露敏感环境变量
    /// </summary>
    private static BashSecurityResult ValidateProcEnvironAccess(string command)
    {
        if (Regex.IsMatch(command, @"/proc/.*?/environ"))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.ProcEnvironAccess,
                "命令访问 /proc/*/environ，可能暴露敏感环境变量");
        }
        return new BashSecurityResult(true);
    }

    /// <summary>
    /// Zsh危险命令检查 — zmodload等可绕过安全检查
    /// </summary>
    private static BashSecurityResult ValidateZshDangerousCommands(string command)
    {
        var trimmed = command.Trim();
        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var baseCmd = "";

        foreach (var token in tokens)
        {
            // 跳过环境变量赋值
            if (Regex.IsMatch(token, @"^[A-Za-z_]\w*=")) continue;
            // 跳过Zsh预命令修饰符
            if (token is "command" or "builtin" or "noglob" or "nocorrect") continue;
            baseCmd = token;
            break;
        }

        if (ZshDangerousCommands.Contains(baseCmd))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.ZshDangerousCommands,
                $"命令使用Zsh特有命令 '{baseCmd}'，可能绕过安全检查", true);
        }

        // fc -e 检查
        if (baseCmd.Equals("fc", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(trimmed, @"\s-\S*e"))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.ZshDangerousCommands,
                "命令使用 'fc -e'，可通过编辑器执行任意命令", true);
        }

        return new BashSecurityResult(true);
    }

    /// <summary>
    /// 反斜杠转义空白检查 — 可改变命令解析
    /// </summary>
    private static BashSecurityResult ValidateBackslashEscapedWhitespace(string command)
    {
        if (HasBackslashEscapedWhitespace(command))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.BackslashEscapedWhitespace,
                "命令包含反斜杠转义空白，可能改变命令解析", true);
        }
        return new BashSecurityResult(true);
    }

    /// <summary>
    /// 反斜杠转义操作符检查 — \; \| 等可隐藏命令结构
    /// </summary>
    private static BashSecurityResult ValidateBackslashEscapedOperators(string command)
    {
        if (HasBackslashEscapedOperator(command))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.BackslashEscapedOperators,
                "命令包含反斜杠转义操作符（;|&<>），可能隐藏命令结构", true);
        }
        return new BashSecurityResult(true);
    }

    /// <summary>
    /// Unicode空白检查 — 可导致解析不一致
    /// </summary>
    private static BashSecurityResult ValidateUnicodeWhitespace(string command)
    {
        if (UnicodeWhitespaceRegex.IsMatch(command))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.UnicodeWhitespace,
                "命令包含Unicode空白字符，可能导致解析不一致", true);
        }
        return new BashSecurityResult(true);
    }

    /// <summary>
    /// 花括号展开检查 — {a,b} 可改变命令解析
    /// </summary>
    private static BashSecurityResult ValidateBraceExpansion(string command)
    {
        // 简化版：检查未转义的 {,} 和逗号/..
        for (var i = 0; i < command.Length; i++)
        {
            if (command[i] == '{' && !IsEscapedAtPosition(command, i))
            {
                // 查找匹配的 }
                var depth = 1;
                for (var j = i + 1; j < command.Length; j++)
                {
                    if (command[j] == '{' && !IsEscapedAtPosition(command, j)) depth++;
                    else if (command[j] == '}' && !IsEscapedAtPosition(command, j))
                    {
                        depth--;
                        if (depth == 0)
                        {
                            // 检查花括号内是否有逗号或..
                            var inner = command.Substring(i + 1, j - i - 1);
                            if (inner.Contains(',') || inner.Contains(".."))
                            {
                                return new BashSecurityResult(false, BashSecurityCheckId.BraceExpansion,
                                    "命令包含花括号展开，可能改变命令解析", true);
                            }
                            break;
                        }
                    }
                }
            }
        }
        return new BashSecurityResult(true);
    }

    /// <summary>
    /// 混淆标志检查 — 引号内的标志名可能绕过黑名单
    /// </summary>
    private static BashSecurityResult ValidateObfuscatedFlags(string command)
    {
        // ANSI-C引用 $'...'
        if (Regex.IsMatch(command, @"\$'[^']*'"))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.ObfuscatedFlags,
                "命令包含ANSI-C引用，可能隐藏字符", true);
        }

        // Locale引用 $"..."
        if (Regex.IsMatch(command, @"\$""[^""]*"""))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.ObfuscatedFlags,
                "命令包含Locale引用，可能隐藏字符", true);
        }

        // 空引号后跟破折号：''- 或 ""-
        if (Regex.IsMatch(command, @"(?:''|"""")+\s*-") ||
            Regex.IsMatch(command, @"(?:''|"""")+\s*['""]-"))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.ObfuscatedFlags,
                "命令包含空引号后的破折号（潜在绕过）", true);
        }

        // 3+连续引号在词首
        if (Regex.IsMatch(command, @"(?:^|\s)['""]{3,}"))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.ObfuscatedFlags,
                "命令包含连续引号字符（潜在混淆）", true);
        }

        return new BashSecurityResult(true);
    }

    /// <summary>
    /// 换行符检查 — 多行命令可能隐藏额外命令
    /// </summary>
    private static BashSecurityResult ValidateNewlines(string command)
    {
        if (!command.Contains('\n') && !command.Contains('\r')) return new BashSecurityResult(true);

        // 检查换行后跟非空白（可能是隐藏命令）
        if (Regex.IsMatch(command, @"[\n\r]\s*\S"))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.Newlines,
                "命令包含换行符，可能分隔多个命令");
        }

        // 回车符特殊检查（解析差异）
        if (command.Contains('\r'))
        {
            return new BashSecurityResult(false, BashSecurityCheckId.Newlines,
                "命令包含回车符（\\r），shell解析器可能产生不同结果", true);
        }

        return new BashSecurityResult(true);
    }

    /// <summary>
    /// 重定向检查 — 输入输出重定向可读写任意文件
    /// </summary>
    private static BashSecurityResult ValidateRedirections(string command)
    {
        // 简化版：检查引号外的 < 和 >
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (c == '\\' && !inSingleQuote && i + 1 < command.Length)
            {
                i++; // 跳过转义字符
                continue;
            }

            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }

            if (!inSingleQuote && !inDoubleQuote)
            {
                if (c == '<')
                {
                    return new BashSecurityResult(false, BashSecurityCheckId.InputRedirection,
                        "命令包含输入重定向（<），可能读取敏感文件");
                }
                if (c == '>')
                {
                    return new BashSecurityResult(false, BashSecurityCheckId.OutputRedirection,
                        "命令包含输出重定向（>），可能写入任意文件");
                }
            }
        }

        return new BashSecurityResult(true);
    }

    #region 辅助方法

    /// <summary>
    /// 检查是否包含未转义的指定字符
    /// </summary>
    private static bool HasUnescapedChar(string content, char ch)
    {
        var i = 0;
        while (i < content.Length)
        {
            if (content[i] == '\\' && i + 1 < content.Length)
            {
                i += 2; // 跳过转义序列
                continue;
            }
            if (content[i] == ch) return true;
            i++;
        }
        return false;
    }

    /// <summary>
    /// 检查位置是否被转义（奇数个反斜杠前置）
    /// </summary>
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

    /// <summary>
    /// 检查反斜杠转义空白（对齐 TS hasBackslashEscapedWhitespace）
    /// </summary>
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
                    {
                        return true;
                    }
                }
                i++; // 跳过转义字符
                continue;
            }

            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
        }

        return false;
    }

    /// <summary>
    /// 检查反斜杠转义操作符（对齐 TS hasBackslashEscapedOperator）
    /// </summary>
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
                    {
                        return true;
                    }
                }
                i++; // 跳过转义字符
                continue;
            }

            if (c == '\'' && !inDoubleQuote) { inSingleQuote = !inSingleQuote; continue; }
            if (c == '"' && !inSingleQuote) { inDoubleQuote = !inDoubleQuote; continue; }
        }

        return false;
    }

    #endregion
}
