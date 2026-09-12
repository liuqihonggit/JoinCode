namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// sed 命令安全验证 — 深度对齐 TS sedValidation.ts
/// 双层防御: 允许列表(Allowlist) + 拒绝列表(Denylist)
/// 防止 w/W(写文件) 和 e/E(执行命令) 危险操作
/// </summary>
public static class SedValidation
{
    /// <summary>
    /// 检查 sed 命令是否被允许列表通过 — 对齐 TS sedCommandIsAllowedByAllowlist
    /// 两种安全模式: 行打印命令(sed -n 'Np') 和 替换命令(sed 's/old/new/flags')
    /// </summary>
    public static bool IsAllowedByAllowlist(string command, bool allowFileWrites = false)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var trimmed = command.Trim();

        // 提取 sed 表达式
        var extraction = ExtractSedExpressions(trimmed);
        if (!extraction.Success)
        {
            return false;
        }

        // 检查是否有文件参数
        var hasFileArgs = HasFileArgs(trimmed, extraction);

        // 模式 1: 行打印命令 — 对齐 TS isLinePrintingCommand
        if (IsLinePrintingCommand(trimmed, extraction))
        {
            return true;
        }

        // 模式 2: 替换命令 — 对齐 TS isSubstitutionCommand
        if (IsSubstitutionCommand(trimmed, extraction, hasFileArgs, allowFileWrites))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 检查 sed 命令是否包含危险操作 — 对齐 TS containsDangerousOperations
    /// 纵深防御: 即使通过允许列表，仍执行拒绝列表检查
    /// </summary>
    public static bool ContainsDangerousOperations(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return true; // 无法验证则视为危险
        }

        var trimmed = command.Trim();

        // 1. 非 ASCII 字符 — 防止 Unicode 同形字攻击
        if (ContainsNonAscii(trimmed))
        {
            return true;
        }

        // 2. 花括号 — 块结构太复杂无法安全解析
        if (trimmed.Contains('{') || trimmed.Contains('}'))
        {
            return true;
        }

        // 3. 换行符 — 多行命令太复杂
        if (trimmed.Contains('\n') || trimmed.Contains('\r'))
        {
            return true;
        }

        // 4. 注释 — 除非紧跟在 s 后面（作为分隔符）
        if (trimmed.Contains('#'))
        {
            // 检查 # 是否是 s 命令的分隔符 (s#old#new#)
            var hashIdx = trimmed.IndexOf('#');
            if (hashIdx <= 0 || trimmed[hashIdx - 1] != 's')
            {
                return true;
            }
        }

        // 5. 否定操作符 — 在行首或地址后的 !
        if (trimmed.Contains('!'))
        {
            return true;
        }

        // 6. GNU 步进地址 ~ (N~M 格式)
        if (trimmed.Contains('~'))
        {
            return true;
        }

        // 7. 反斜杠技巧 — s\ 作为分隔符或 \X 替代分隔符
        var backslashIdx = trimmed.IndexOf('\\');
        while (backslashIdx >= 0)
        {
            if (backslashIdx + 1 < trimmed.Length)
            {
                var nextChar = trimmed[backslashIdx + 1];
                // s\ 作为反斜杠分隔符
                if (backslashIdx > 0 && trimmed[backslashIdx - 1] == 's')
                {
                    return true;
                }
                // \X 替代分隔符 (|, #, %, @ 等)
                if (nextChar is '|' or '#' or '%' or '@' or '!' or '^' or '&' or '*' or '+' or '?' or ':' or ',' or ';' or '=')
                {
                    return true;
                }
            }
            backslashIdx = trimmed.IndexOf('\\', backslashIdx + 1);
        }

        // 8. 提取表达式并检查危险模式
        var extraction = ExtractSedExpressions(trimmed);
        if (!extraction.Success)
        {
            return true;
        }

        foreach (var expr in extraction.Expressions)
        {
            if (ExpressionContainsDangerousOps(expr))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查 sed 命令约束 — 对齐 TS checkSedConstraints
    /// 返回 true 表示安全，false 表示需要用户确认
    /// </summary>
    public static bool IsSedCommandSafe(string command, bool allowFileWrites = false)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        // 双层防御: 先检查允许列表，再检查拒绝列表
        if (!IsAllowedByAllowlist(command, allowFileWrites))
        {
            return false;
        }

        // 纵深防御: 即使通过允许列表，仍执行拒绝列表检查
        if (ContainsDangerousOperations(command))
        {
            return false;
        }

        return true;
    }

    #region 表达式提取

    /// <summary>
    /// sed 表达式提取结果
    /// </summary>
    private sealed class SedExtractionResult
    {
        public bool Success { get; init; }
        public List<string> Expressions { get; init; } = [];
        public List<string> FileArgs { get; init; } = [];
        public bool HasInPlace { get; init; }
    }

    /// <summary>
    /// 从 sed 命令中提取表达式 — 对齐 TS extractSedExpressions
    /// </summary>
    private static SedExtractionResult ExtractSedExpressions(string command)
    {
        var tokens = SplitSedTokens(command);
        if (tokens.Count == 0)
        {
            return new SedExtractionResult();
        }

        // 第一个 token 应该是 "sed"
        if (!tokens[0].Equals("sed", StringComparison.OrdinalIgnoreCase))
        {
            return new SedExtractionResult();
        }

        var expressions = new List<string>();
        var fileArgs = new List<string>();
        var hasInPlace = false;
        var i = 1; // 跳过 "sed"

        while (i < tokens.Count)
        {
            var token = tokens[i];

            // -- 定界符
            if (token == "--")
            {
                i++;
                // 剩余全是文件参数
                while (i < tokens.Count)
                {
                    fileArgs.Add(tokens[i]);
                    i++;
                }
                break;
            }

            // -e/--expression 标志
            if (token is "-e" or "--expression")
            {
                i++;
                if (i < tokens.Count)
                {
                    expressions.Add(tokens[i]);
                }
                else
                {
                    return new SedExtractionResult(); // 缺少参数
                }
                i++;
                continue;
            }

            // --expression=value 格式
            if (token.StartsWith("--expression=", StringComparison.Ordinal))
            {
                expressions.Add(token["--expression=".Length..]);
                i++;
                continue;
            }

            // -e=value 格式 (非标准但某些实现支持)
            if (token.StartsWith("-e=", StringComparison.Ordinal))
            {
                expressions.Add(token["-e=".Length..]);
                i++;
                continue;
            }

            // -f/--file 标志
            if (token is "-f" or "--file")
            {
                i += 2; // 跳过标志和文件路径
                continue;
            }

            // --file=value 格式
            if (token.StartsWith("--file=", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            // -i/--in-place 标志
            if (token is "-i" or "--in-place")
            {
                hasInPlace = true;
                // -i 可选参数 (如 -i.bak)
                if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-'))
                {
                    // 检查是否是 -i 的可选后缀 (如 .bak)
                    var next = tokens[i + 1];
                    if (next.StartsWith('.') || char.IsLetter(next[0]))
                    {
                        i++; // 跳过后缀
                    }
                }
                i++;
                continue;
            }

            // --in-place=value 格式
            if (token.StartsWith("--in-place=", StringComparison.Ordinal))
            {
                hasInPlace = true;
                i++;
                continue;
            }

            // 其他短标志组合 (如 -nE, -nz)
            if (token.StartsWith('-') && token.Length > 1 && !token.StartsWith("--"))
            {
                // 检查危险标志组合: -ew, -eW, -ee, -we, -wE
                if (ContainsDangerousFlagCombo(token))
                {
                    return new SedExtractionResult(); // 危险组合
                }

                // 检查 -e 融合 (如 -e's/old/new/g')
                var eIdx = token.IndexOf('e');
                if (eIdx > 0 && token.Length > eIdx + 1)
                {
                    // -e 后面直接跟表达式
                    expressions.Add(token[(eIdx + 1)..]);
                }

                i++;
                continue;
            }

            // 非标志参数 — 第一个是表达式，后续是文件参数
            if (expressions.Count == 0)
            {
                expressions.Add(token);
            }
            else
            {
                fileArgs.Add(token);
            }

            i++;
        }

        return new SedExtractionResult
        {
            Success = true,
            Expressions = expressions,
            FileArgs = fileArgs,
            HasInPlace = hasInPlace,
        };
    }

    /// <summary>
    /// 检查短标志组合是否包含危险组合 — 对齐 TS 危险标志组合拦截
    /// </summary>
    private static bool ContainsDangerousFlagCombo(string token)
    {
        // -ew, -eW, -ee: -e 标志与危险命令
        // -we, -wE: -w 标志与执行命令
        var lower = token.ToLowerInvariant();
        return lower.Contains("ew") || lower.Contains("ee")
            || lower.Contains("we");
    }

    /// <summary>
    /// 判断 sed 命令是否有文件参数 — 对齐 TS hasFileArgs
    /// </summary>
    private static bool HasFileArgs(string command, SedExtractionResult extraction)
    {
        if (!extraction.Success)
        {
            return true; // 解析失败，假设危险
        }

        if (extraction.FileArgs.Count > 0)
        {
            return true;
        }

        // glob 模式（如 *.log）被视为文件参数
        var tokens = SplitSedTokens(command);
        foreach (var token in tokens.Skip(1))
        {
            if (!token.StartsWith('-') && token.Contains('*') || token.Contains('?'))
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region 允许列表模式

    /// <summary>
    /// 模式 1: 行打印命令 — 对齐 TS isLinePrintingCommand
    /// 允许格式: sed -n 'Np' 或 sed -n 'N,Mp'，支持分号分隔
    /// </summary>
    private static bool IsLinePrintingCommand(string command, SedExtractionResult extraction)
    {
        if (!extraction.Success || extraction.Expressions.Count == 0)
        {
            return false;
        }

        // 必须有 -n 标志
        if (!command.Contains("-n") && !command.Contains("--quiet") && !command.Contains("--silent"))
        {
            return false;
        }

        // 只允许安全标志
        var tokens = SplitSedTokens(command);
        foreach (var token in tokens.Skip(1))
        {
            if (token.StartsWith('-') && token != "--")
            {
                // 允许 -n, -E, -r, -z 及其长形式
                if (!IsSafePrintingFlag(token))
                {
                    return false;
                }
            }
        }

        // 所有表达式必须是纯 p 命令
        foreach (var expr in extraction.Expressions)
        {
            if (!IsPrintCommand(expr))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 检查是否是安全的打印标志
    /// </summary>
    private static bool IsSafePrintingFlag(string token)
    {
        // 去除 =value 部分
        var eqIdx = token.IndexOf('=');
        var flag = eqIdx >= 0 ? token[..eqIdx] : token;

        return flag is "-n" or "--quiet" or "--silent"
            or "-E" or "--regexp-extended" or "-r"
            or "-z" or "--null-data" or "--zero-terminated"
            or "--posix"
            or "-e" or "--expression"
            or "-f" or "--file"
            or "--help" or "--version";
    }

    /// <summary>
    /// 检查是否是纯 p 命令 — 对齐 TS isPrintCommand
    /// 允许: p, Np, N,Mp
    /// </summary>
    private static bool IsPrintCommand(string expr)
    {
        // 支持分号分隔 (如 "1p;2p;3p")
        var parts = expr.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            // p 命令: 纯 p, Np, N,Mp
            if (trimmed == "p")
            {
                continue;
            }

            // Np (如 1p, 42p)
            if (trimmed.Length > 1 && trimmed[^1] == 'p' && uint.TryParse(trimmed[..^1], out _))
            {
                continue;
            }

            // N,Mp (如 1,5p)
            var commaIdx = trimmed.IndexOf(',');
            if (commaIdx > 0 && trimmed[^1] == 'p')
            {
                var beforeComma = trimmed[..commaIdx];
                var afterComma = trimmed[(commaIdx + 1)..^1];
                if (uint.TryParse(beforeComma, out _) && uint.TryParse(afterComma, out _))
                {
                    continue;
                }
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// 模式 2: 替换命令 — 对齐 TS isSubstitutionCommand
    /// 允许格式: sed 's/pattern/replacement/flags'
    /// </summary>
    private static bool IsSubstitutionCommand(
        string command,
        SedExtractionResult extraction,
        bool hasFileArgs,
        bool allowFileWrites)
    {
        if (!extraction.Success || extraction.Expressions.Count == 0)
        {
            return false;
        }

        // 必须恰好一个表达式
        if (extraction.Expressions.Count != 1)
        {
            return false;
        }

        var expr = extraction.Expressions[0];

        // 不允许分号 (防止 s/old/new/g;w file 注入)
        if (expr.Contains(';'))
        {
            return false;
        }

        // 必须以 s 开头
        if (!expr.StartsWith('s'))
        {
            return false;
        }

        // 只允许 / 作为分隔符
        if (expr.Length < 2 || expr[1] != '/')
        {
            return false;
        }

        // 解析 s/pattern/replacement/flags
        var parseResult = ParseSubstitution(expr);
        if (!parseResult.Success)
        {
            return false;
        }

        // 替换标志只允许 g, p, i, I, m, M 和可选的一个数字 1-9
        if (!AreSubstitutionFlagsSafe(parseResult.Flags))
        {
            return false;
        }

        // 默认不允许文件参数和 -i 标志（stdout-only）
        if (!allowFileWrites)
        {
            if (hasFileArgs || extraction.HasInPlace)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 替换命令解析结果
    /// </summary>
    private sealed class SubstitutionParseResult
    {
        public bool Success { get; init; }
        public string Pattern { get; init; } = string.Empty;
        public string Replacement { get; init; } = string.Empty;
        public string Flags { get; init; } = string.Empty;
    }

    /// <summary>
    /// 解析 s/pattern/replacement/flags — 只支持 / 分隔符
    /// </summary>
    private static SubstitutionParseResult ParseSubstitution(string expr)
    {
        // s/pattern/replacement/flags
        // 从位置 2 开始 (跳过 s/)
        var content = expr[2..];
        var slashCount = 0;
        var firstSlashEnd = -1;
        var secondSlashEnd = -1;

        // 计算未转义的斜杠
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\\' && i + 1 < content.Length && content[i + 1] == '/')
            {
                i++; // 跳过转义的斜杠
                continue;
            }

            if (content[i] == '/')
            {
                slashCount++;
                if (slashCount == 1 && firstSlashEnd < 0)
                {
                    firstSlashEnd = i;
                }
                else if (slashCount == 2 && secondSlashEnd < 0)
                {
                    secondSlashEnd = i;
                    break;
                }
            }
        }

        // 必须恰好 2 个分隔符
        if (firstSlashEnd < 0 || secondSlashEnd < 0)
        {
            return new SubstitutionParseResult();
        }

        var pattern = content[..firstSlashEnd];
        var replacement = content[(firstSlashEnd + 1)..secondSlashEnd];
        var flags = secondSlashEnd + 1 < content.Length
            ? content[(secondSlashEnd + 1)..]
            : string.Empty;

        return new SubstitutionParseResult
        {
            Success = true,
            Pattern = pattern,
            Replacement = replacement,
            Flags = flags,
        };
    }

    /// <summary>
    /// 检查替换标志是否安全 — 只允许 g, p, i, I, m, M 和一个数字 1-9
    /// </summary>
    private static bool AreSubstitutionFlagsSafe(string flags)
    {
        if (string.IsNullOrEmpty(flags))
        {
            return true;
        }

        var hasDigit = false;
        foreach (var c in flags)
        {
            switch (c)
            {
                case 'g' or 'p' or 'i' or 'I' or 'm' or 'M':
                    break;
                case '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9':
                    if (hasDigit)
                    {
                        return false; // 只允许一个数字
                    }
                    hasDigit = true;
                    break;
                default:
                    return false; // 未知标志
            }
        }

        return true;
    }

    #endregion

    #region 拒绝列表

    /// <summary>
    /// 检查表达式是否包含危险操作 — 对齐 TS containsDangerousOperations 的表达式级检查
    /// </summary>
    private static bool ExpressionContainsDangerousOps(string expr)
    {
        // 1. 写命令检测: w file, 1w file, $w file, /pattern/w file, 1,10w file
        if (ContainsWriteCommand(expr))
        {
            return true;
        }

        // 2. 执行命令检测: e cmd, 1e, $e, /pattern/e, 1,10e
        if (ContainsExecuteCommand(expr))
        {
            return true;
        }

        // 3. 替换标志中的 w/W/e/E: s/old/new/w, s/old/new/gw, s/old/new/e
        if (ContainsDangerousSubstitutionFlags(expr))
        {
            return true;
        }

        // 4. y 命令后跟 w/W/e/E — 偏执拒绝
        if (ContainsDangerousYCommand(expr))
        {
            return true;
        }

        // 5. 畸形替换命令: s/foobareoutput.txt (缺少分隔符) 或 s/foo/bar//w (多余分隔符)
        if (ContainsMalformedSubstitution(expr))
        {
            return true;
        }

        // 6. 偏执检查: 以 s 开头但以 w/W/e/E 结尾
        if (expr.StartsWith('s') && expr.Length > 1)
        {
            var lastChar = expr[^1];
            if (lastChar is 'w' or 'W' or 'e' or 'E')
            {
                // 验证是否是格式正确的替换命令
                var parseResult = ParseSubstitution(expr);
                if (!parseResult.Success)
                {
                    return true; // 无法解析且以危险字符结尾
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 检测写命令模式 — 对齐 TS 写命令检测
    /// </summary>
    private static bool ContainsWriteCommand(string expr)
    {
        // w file, 1w file, $w file, /pattern/w file, 1,10w file, /s/,/e/w file
        // 简化正则: 匹配 w 后跟空格和文件名
        var wIdx = expr.IndexOf('w');
        while (wIdx >= 0)
        {
            // w 前面是地址 (数字, $, /pattern/) 或行首
            if (wIdx == 0 || char.IsDigit(expr[wIdx - 1]) || expr[wIdx - 1] == '$'
                || expr[wIdx - 1] == '/' || expr[wIdx - 1] == ',')
            {
                // w 后面跟空格和文件名
                if (wIdx + 1 < expr.Length && char.IsWhiteSpace(expr[wIdx + 1]))
                {
                    return true;
                }

                // w 在表达式末尾 (如 1w)
                if (wIdx + 1 >= expr.Length)
                {
                    return true;
                }
            }

            wIdx = expr.IndexOf('w', wIdx + 1);
        }

        return false;
    }

    /// <summary>
    /// 检测执行命令模式 — 对齐 TS 执行命令检测
    /// </summary>
    private static bool ContainsExecuteCommand(string expr)
    {
        // e cmd, 1e, $e, /pattern/e, 1,10e
        var eIdx = expr.IndexOf('e');
        while (eIdx >= 0)
        {
            // e 前面是地址或行首
            if (eIdx == 0 || char.IsDigit(expr[eIdx - 1]) || expr[eIdx - 1] == '$'
                || expr[eIdx - 1] == '/' || expr[eIdx - 1] == ',')
            {
                // e 后面跟空格和命令，或在表达式末尾
                if (eIdx + 1 >= expr.Length || char.IsWhiteSpace(expr[eIdx + 1]))
                {
                    return true;
                }
            }

            eIdx = expr.IndexOf('e', eIdx + 1);
        }

        return false;
    }

    /// <summary>
    /// 检测替换标志中的 w/W/e/E — 对齐 TS 替换标志危险检测
    /// </summary>
    private static bool ContainsDangerousSubstitutionFlags(string expr)
    {
        if (!expr.StartsWith('s') || expr.Length < 2 || expr[1] != '/')
        {
            return false;
        }

        var parseResult = ParseSubstitution(expr);
        if (!parseResult.Success)
        {
            return false;
        }

        // 检查替换标志中是否有 w/W/e/E
        foreach (var c in parseResult.Flags)
        {
            if (c is 'w' or 'W' or 'e' or 'E')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检测 y 命令中的危险字符 — 偏执拒绝
    /// </summary>
    private static bool ContainsDangerousYCommand(string expr)
    {
        if (!expr.StartsWith('y'))
        {
            return false;
        }

        // y 命令中包含 w/W/e/E — 偏执拒绝
        return expr.Contains('w') || expr.Contains('W')
            || expr.Contains('e') || expr.Contains('E');
    }

    /// <summary>
    /// 检测畸形替换命令 — 对齐 TS 畸形检测
    /// </summary>
    private static bool ContainsMalformedSubstitution(string expr)
    {
        if (!expr.StartsWith('s'))
        {
            return false;
        }

        // s/foobareoutput.txt — 缺少分隔符
        if (expr.Length > 2 && expr[1] == '/')
        {
            var slashCount = 0;
            for (var i = 2; i < expr.Length; i++)
            {
                if (expr[i] == '\\' && i + 1 < expr.Length && expr[i + 1] == '/')
                {
                    i++;
                    continue;
                }
                if (expr[i] == '/')
                {
                    slashCount++;
                }
            }

            // 恰好 0 个额外斜杠 = 缺少分隔符
            if (slashCount == 0)
            {
                return true;
            }

            // 超过 2 个额外斜杠 = 多余分隔符 (如 s/foo/bar//w)
            if (slashCount > 2)
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 检查是否包含非 ASCII 字符 — 防止 Unicode 同形字攻击
    /// </summary>
    private static bool ContainsNonAscii(string s)
    {
        foreach (var c in s)
        {
            if (c > 127)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 分割 sed 命令为 token — 引号感知的空白分割
    /// </summary>
    private static List<string> SplitSedTokens(string command)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if ((c == '\'' || c == '"') && !inQuotes)
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

    #endregion
}
