namespace Services.Shell;

/// <summary>
/// sed 命令验证器 — 对齐 TS sedValidation.ts
/// 白名单+黑名单双重验证，只允许安全的打印命令和替换命令
/// </summary>
public static partial class SedValidator
{
    /// <summary>
    /// 检查 sed 命令约束 — 对齐 TS checkSedConstraints
    /// </summary>
    public static SedValidationResult CheckSedConstraints(string command, bool allowFileWrites = false)
    {
        if (string.IsNullOrWhiteSpace(command)) return new(PermissionBehavior.Passthrough);

        var trimmed = command.TrimStart();
        if (!trimmed.StartsWith("sed ", StringComparison.OrdinalIgnoreCase))
        {
            return new(PermissionBehavior.Passthrough);
        }

        if (IsLinePrintingCommand(trimmed))
        {
            return new(PermissionBehavior.Passthrough);
        }

        var substResult = IsSubstitutionCommand(trimmed, allowFileWrites);
        if (substResult.Behavior == PermissionBehavior.Passthrough)
        {
            var expressions = ExtractSedExpressions(trimmed);
            foreach (var expr in expressions)
            {
                if (ContainsDangerousOperations(expr))
                {
                    return new(PermissionBehavior.Deny, $"sed 表达式包含危险操作: {expr}");
                }
            }
        }

        return substResult;
    }

    /// <summary>
    /// 检查是否为行打印命令 — 对齐 TS isLinePrintingCommand
    /// 允许: sed -n 'Np' 或 sed -n 'N,Mp'
    /// </summary>
    private static bool IsLinePrintingCommand(string command)
    {
        var tokens = SedEditParser.TryParseShellTokens(command[4..]);
        if (tokens is null) return false;

        var hasQuietFlag = false;
        string? expression = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token is "-n" or "--quiet" or "--silent")
            {
                hasQuietFlag = true;
            }
            else if (token is "-E" or "-r" or "--regexp-extended")
            {
                // 允许扩展正则
            }
            else if (token is "-z" or "--zero-terminated" or "--posix")
            {
                // 允许
            }
            else if (token.StartsWith("-"))
            {
                return false; // 不认识的标志
            }
            else if (expression is null)
            {
                expression = token;
            }
        }

        if (!hasQuietFlag || expression is null) return false;

        // 检查表达式格式: Np 或 N,Mp（支持分号分隔）
        var parts = expression.Split(';');
        foreach (var part in parts)
        {
            var trimmedPart = part.Trim();
            if (!LinePrintingRegex().IsMatch(trimmedPart))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 检查是否为替换命令 — 对齐 TS isSubstitutionCommand
    /// </summary>
    private static SedValidationResult IsSubstitutionCommand(string command, bool allowFileWrites)
    {
        var tokens = SedEditParser.TryParseShellTokens(command[4..]);
        if (tokens is null) return new(PermissionBehavior.Deny, "无法解析 sed 命令");

        var hasInPlaceFlag = false;
        var expressionCount = 0;
        string? expression = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token is "-i" or "--in-place")
            {
                hasInPlaceFlag = true;
                if (i + 1 < tokens.Count)
                {
                    var next = tokens[i + 1];
                    if (string.IsNullOrEmpty(next) || next.StartsWith('.'))
                    {
                        i++;
                    }
                }
            }
            else if (token.StartsWith("-i.") || token.StartsWith("--in-place="))
            {
                hasInPlaceFlag = true;
            }
            else if (token is "-E" or "-r" or "--regexp-extended")
            {
                // 允许扩展正则
            }
            else if (token is "--posix")
            {
                // 允许
            }
            else if (token is "-e" or "--expression")
            {
                expressionCount++;
                if (i + 1 < tokens.Count)
                {
                    expression = tokens[++i];
                }
            }
            else if (token.StartsWith("--expression="))
            {
                expressionCount++;
                expression = token["--expression=".Length..];
            }
            else if (token.StartsWith("-"))
            {
                return new(PermissionBehavior.Deny, $"不支持的 sed 标志: {token}");
            }
            else
            {
                if (expression is null)
                {
                    expression = token;
                    expressionCount = 1;
                }
                else
                {
                    // 文件参数 — 已在 expression 之后，属于合法的文件路径参数
                    // 无需额外验证，sed 命令格式为: sed 's/old/new/' file
                }
            }
        }

        // 必须恰好 1 个表达式
        if (expressionCount != 1 || expression is null)
        {
            return new(PermissionBehavior.Deny, "sed 替换命令必须恰好 1 个表达式");
        }

        // 表达式必须以 s 开头
        if (!expression.StartsWith("s", StringComparison.Ordinal))
        {
            return new(PermissionBehavior.Deny, "sed 表达式必须以 s 开头");
        }

        // 分隔符必须是 /
        if (expression.Length < 2 || expression[1] != '/')
        {
            return new(PermissionBehavior.Deny, "sed 替换命令必须使用 / 作为分隔符");
        }

        // 检查未转义的 / 数量
        var unescapedSlashCount = 0;
        for (var i = 1; i < expression.Length; i++)
        {
            if (expression[i] == '/' && (i == 0 || expression[i - 1] != '\\'))
            {
                unescapedSlashCount++;
            }
        }

        if (unescapedSlashCount != 2)
        {
            return new(PermissionBehavior.Deny, "sed 替换命令必须恰好 2 个未转义的 / 分隔符");
        }

        // 提取并验证标志
        var lastSlashIndex = expression.LastIndexOf('/');
        var flags = lastSlashIndex >= 0 && lastSlashIndex < expression.Length - 1
            ? expression[(lastSlashIndex + 1)..]
            : "";

        if (!SubstitutionFlagsRegex().IsMatch(flags))
        {
            return new(PermissionBehavior.Deny, $"不支持的 sed 替换标志: {flags}");
        }

        // -i 标志检查
        if (hasInPlaceFlag && !allowFileWrites)
        {
            return new(PermissionBehavior.Ask, "sed -i 命令将修改文件，需要确认");
        }

        return new(PermissionBehavior.Passthrough);
    }

    /// <summary>
    /// 检查表达式是否包含危险操作 — 对齐 TS containsDangerousOperations
    /// </summary>
    private static bool ContainsDangerousOperations(string expression)
    {
        // 非 ASCII 字符
        if (expression.Any(c => c > 127)) return true;

        // 代码块
        if (expression.Contains('{') || expression.Contains('}')) return true;

        // 换行符
        if (expression.Contains('\n')) return true;

        // 注释
        if (expression.Contains('#')) return true;

        // 否定操作符
        if (expression.Contains('!')) return true;

        // GNU 步进地址
        if (expression.Contains('~')) return true;

        // 开头逗号
        if (expression.StartsWith(",")) return true;

        // 写文件命令
        if (expression.Contains("w ", StringComparison.Ordinal)) return true;
        if (expression.Contains("W ", StringComparison.Ordinal)) return true;

        // 执行命令
        if (expression.Contains("e ", StringComparison.Ordinal)) return true;
        if (expression.Contains("E ", StringComparison.Ordinal)) return true;

        return false;
    }

    /// <summary>
    /// 提取 sed 表达式 — 对齐 TS extractSedExpressions
    /// </summary>
    private static List<string> ExtractSedExpressions(string command)
    {
        var tokens = SedEditParser.TryParseShellTokens(command[4..]);
        if (tokens is null) return [];

        var expressions = new List<string>();
        var hasExplicitExpression = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (token is "-e" or "--expression")
            {
                hasExplicitExpression = true;
                if (i + 1 < tokens.Count)
                {
                    expressions.Add(tokens[++i]);
                }
            }
            else if (token.StartsWith("--expression="))
            {
                hasExplicitExpression = true;
                expressions.Add(token["--expression=".Length..]);
            }
            else if (!token.StartsWith("-"))
            {
                if (!hasExplicitExpression && expressions.Count == 0)
                {
                    expressions.Add(token);
                }
            }
        }

        return expressions;
    }

    [GeneratedRegex(@"^(?:\d+|\d+,\d+)?p$")]
    private static partial Regex LinePrintingRegex();

    [GeneratedRegex(@"^[gpimIM]*[1-9]?[gpimIM]*$")]
    private static partial Regex SubstitutionFlagsRegex();
}
