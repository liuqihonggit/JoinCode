namespace JoinCode.Guard.Security.PowerShell;

public static partial class PsPermissions
{
    public static PsSecurityResult CheckPermission(
        string command,
        string workingDirectory,
        IReadOnlyList<string> denyRules,
        IReadOnlyList<string> askRules,
        IReadOnlyList<string> allowRules,
        IReadOnlyList<string> allowedDirectories,
        IReadOnlyList<string> denyDirectories,
        bool acceptEdits = false)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return PsSecurityResult.Passthrough;
        }

        var decisions = new List<PsSecurityResult>();

        // ─── 阶段一：预解析规则检查 ───────────────────────────────

        // 精确匹配 deny 规则
        var exactDeny = MatchExactRule(command, denyRules);
        if (exactDeny is not null)
        {
            return PsSecurityResult.Deny($"Command matches deny rule: {exactDeny}");
        }

        // 前缀/通配符匹配 deny 规则
        var prefixDeny = MatchPrefixRule(command, denyRules);
        if (prefixDeny is not null)
        {
            return PsSecurityResult.Deny($"Command matches deny rule pattern: {prefixDeny}");
        }

        // 前缀/通配符匹配 ask 规则（延迟，不立即返回）
        PsSecurityResult? preParseAsk = null;
        var prefixAsk = MatchPrefixRule(command, askRules);
        if (prefixAsk is not null)
        {
            preParseAsk = PsSecurityResult.Ask($"Command matches ask rule pattern: {prefixAsk}");
        }

        // ─── 阶段二：AST 解析 + 安全检查 ─────────────────────────

        var parsed = PsAstParser.Parse(command);

        if (!parsed.Valid)
        {
            // 解析失败降级路径
            return HandleParseFailure(command, denyRules, preParseAsk);
        }

        // 安全检查（子表达式/脚本块/编码命令/下载等）
        var securityResult = PsSecurityChecker.CommandIsSafe(command);
        if (securityResult.Behavior != PermissionBehavior.Passthrough)
        {
            decisions.Add(securityResult);
        }

        // using 语句 / #Requires 指令
        if (parsed.HasUsingStatements)
        {
            decisions.Add(PsSecurityResult.Ask("Command contains 'using' statements which may load external code"));
        }
        if (parsed.HasScriptRequirements)
        {
            decisions.Add(PsSecurityResult.Ask("Command contains '#requires' directives which may impose execution constraints"));
        }

        // Provider/UNC 路径扫描
        var providerResult = ScanProviderAndUncPaths(parsed);
        if (providerResult is not null)
        {
            decisions.Add(providerResult);
        }

        // ─── 阶段三：子命令级别规则检查 ───────────────────────────

        foreach (var statement in parsed.Statements)
        {
            // 主命令
            foreach (var cmd in statement.Commands)
            {
                CheckSubCommandRules(cmd, denyRules, askRules, allowRules, decisions);
            }

            // 嵌套命令
            foreach (var cmd in statement.NestedCommands)
            {
                CheckSubCommandRules(cmd, denyRules, askRules, allowRules, decisions);
            }
        }

        // ─── 阶段四：路径约束检查 ─────────────────────────────────

        var pathResult = PsPathValidation.CheckPathConstraints(command, workingDirectory, allowedDirectories, denyDirectories);
        if (pathResult.Behavior != PermissionBehavior.Passthrough)
        {
            decisions.Add(pathResult);
        }

        // ─── 阶段五：精确 allow 规则 ──────────────────────────────

        var exactAllow = MatchExactRule(command, allowRules);
        if (exactAllow is not null)
        {
            decisions.Add(new PsSecurityResult { Behavior = PermissionBehavior.Allow, DecisionReason = "exactAllowRule" });
        }

        // ─── 阶段六：只读命令白名单 ───────────────────────────────

        if (PsReadOnlyValidation.IsReadOnlyCommand(command, parsed))
        {
            decisions.Add(new PsSecurityResult { Behavior = PermissionBehavior.Allow, DecisionReason = "readOnlyWhitelist" });
        }

        // ─── 阶段七：acceptEdits 模式 ─────────────────────────────

        if (acceptEdits)
        {
            decisions.Add(new PsSecurityResult { Behavior = PermissionBehavior.Allow, DecisionReason = "acceptEdits" });
        }

        // ─── 归约：deny > ask > allow > passthrough ───────────────

        // 加入延迟的 preParseAsk
        if (preParseAsk is not null)
        {
            decisions.Add(preParseAsk);
        }

        return ReduceDecisions(decisions);
    }

    /// <summary>
    /// 归约决策数组 — deny > ask > allow > passthrough
    /// </summary>
    private static PsSecurityResult ReduceDecisions(List<PsSecurityResult> decisions)
    {
        if (decisions.Count == 0) return PsSecurityResult.Passthrough;

        PsSecurityResult? firstAsk = null;
        PsSecurityResult? firstAllow = null;

        foreach (var d in decisions)
        {
            if (d.Behavior == PermissionBehavior.Deny) return d;
            if (d.Behavior == PermissionBehavior.Ask && firstAsk is null) firstAsk = d;
            if (d.Behavior == PermissionBehavior.Allow && firstAllow is null) firstAllow = d;
        }

        // ask 优先于 allow
        if (firstAsk is not null) return firstAsk;
        if (firstAllow is not null) return firstAllow;

        return PsSecurityResult.Passthrough;
    }

    /// <summary>
    /// 解析失败时的降级处理
    /// </summary>
    private static PsSecurityResult HandleParseFailure(
        string command,
        IReadOnlyList<string> denyRules,
        PsSecurityResult? preParseAsk)
    {
        var backtickStripped = command.Replace("`", "");

        var fragments = SplitCommandFragments(backtickStripped);

        foreach (var fragment in fragments)
        {
            var normalized = NormalizeFragment(fragment);

            var exactDeny = MatchExactRule(normalized, denyRules);
            if (exactDeny is not null)
            {
                return PsSecurityResult.Deny($"Command fragment matches deny rule: {exactDeny}");
            }

            var prefixDeny = MatchPrefixRule(normalized, denyRules);
            if (prefixDeny is not null)
            {
                return PsSecurityResult.Deny($"Command fragment matches deny rule pattern: {prefixDeny}");
            }

            var tokens = normalized.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            var firstTok = tokens[0].ToLowerInvariant();
            var canonical = PsAliases.ResolveToCanonical(firstTok);

            if (canonical == "remove-item")
            {
                foreach (var arg in tokens[1..])
                {
                    if (arg.StartsWith('-')) continue;
                    if (IsDangerousRemovalRawPath(arg))
                    {
                        return PsSecurityResult.Deny($"Removing system-critical path is not allowed: {arg}");
                    }
                }
            }

            foreach (var arg in tokens)
            {
                if (arg.StartsWith(@"\\", StringComparison.Ordinal) || arg.StartsWith("//", StringComparison.Ordinal))
                {
                    return PsSecurityResult.Deny("UNC path is not allowed as it may trigger network authentication");
                }
            }
        }

        if (preParseAsk is not null) return preParseAsk;

        return PsSecurityResult.Ask("Command could not be fully parsed for security analysis");
    }

    private static bool IsDangerousRemovalRawPath(string path)
    {
        var lower = path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        return lower is @"c:\windows\system32" or @"c:\windows\system"
            or @"c:\program files" or @"c:\program files (x86)"
            || lower.StartsWith(@"c:\windows\system32\")
            || lower.StartsWith(@"c:\windows\system\")
            || lower.StartsWith(@"c:\program files\")
            || lower.StartsWith(@"c:\program files (x86)\");
    }

    /// <summary>
    /// 按分隔符分割命令片段
    /// </summary>
    private static string[] SplitCommandFragments(string command)
    {
        return command.Split([';', '|', '{', '}', '\n', '\r', '(', ')', '&'], StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// 归一化命令片段（剥离赋值前缀、反引号转义）
    /// </summary>
    private static string NormalizeFragment(string fragment)
    {
        var trimmed = fragment.Trim();

        // 剥离反引号
        trimmed = trimmed.Replace("`", "");

        // 剥离行续符
        trimmed = trimmed.Replace("`n", "").Replace("`r", "");

        // 剥离赋值前缀（$var = command → command）
        var eqIdx = trimmed.IndexOf('=');
        if (eqIdx > 0 && trimmed[..eqIdx].TrimStart().StartsWith('$'))
        {
            var afterEq = trimmed[(eqIdx + 1)..].TrimStart();
            if (afterEq.Length > 0)
            {
                trimmed = afterEq;
            }
        }

        return trimmed;
    }

    /// <summary>
    /// 精确匹配规则
    /// </summary>
    private static string? MatchExactRule(string command, IReadOnlyList<string> rules)
    {
        var cmdLower = command.Trim().ToLowerInvariant();

        // 提取第一个命令名
        var firstWord = GetFirstWord(cmdLower);
        if (string.IsNullOrEmpty(firstWord)) return null;

        foreach (var rule in rules)
        {
            var ruleLower = rule.Trim().ToLowerInvariant();
            if (ruleLower == cmdLower || ruleLower == firstWord)
            {
                return rule;
            }

            // 规范名匹配（别名解析）
            var canonical = PsAliases.ResolveToCanonical(firstWord);
            var ruleCanonical = PsAliases.ResolveToCanonical(ruleLower);
            if (canonical.Equals(ruleLower, StringComparison.OrdinalIgnoreCase)
                || canonical.Equals(ruleCanonical, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>
    /// 前缀/通配符匹配规则
    /// </summary>
    private static string? MatchPrefixRule(string command, IReadOnlyList<string> rules)
    {
        var cmdLower = command.Trim().ToLowerInvariant();
        var firstWord = GetFirstWord(cmdLower);
        if (string.IsNullOrEmpty(firstWord)) return null;

        foreach (var rule in rules)
        {
            var ruleLower = rule.Trim().ToLowerInvariant();

            // 前缀匹配
            if (cmdLower.StartsWith(ruleLower) || firstWord.StartsWith(ruleLower))
            {
                return rule;
            }

            // 通配符匹配（简单 * 匹配）
            if (ruleLower.Contains('*'))
            {
                var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(ruleLower).Replace("\\*", ".*") + "$";
                try
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(cmdLower, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        return rule;
                    }
                }
                catch (System.Text.RegularExpressions.RegexParseException ex)
                {
                    // 无效模式，跳过
                    System.Diagnostics.Trace.WriteLine($"Invalid regex pattern '{pattern}' for rule '{ruleLower}': {ex.Message}");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 检查子命令级别的规则
    /// </summary>
    private static void CheckSubCommandRules(
        PsCommandElement cmd,
        IReadOnlyList<string> denyRules,
        IReadOnlyList<string> askRules,
        IReadOnlyList<string> allowRules,
        List<PsSecurityResult> decisions)
    {
        var cmdName = cmd.Name.ToLowerInvariant();
        var canonical = PsAliases.ResolveToCanonical(cmdName);

        // deny 规则
        foreach (var rule in denyRules)
        {
            var ruleLower = rule.Trim().ToLowerInvariant();
            if (cmdName == ruleLower || canonical == ruleLower)
            {
                decisions.Add(PsSecurityResult.Deny($"Sub-command '{cmd.Name}' matches deny rule: {rule}"));
                return;
            }
        }

        // ask 规则
        foreach (var rule in askRules)
        {
            var ruleLower = rule.Trim().ToLowerInvariant();
            if (cmdName == ruleLower || canonical == ruleLower)
            {
                decisions.Add(PsSecurityResult.Ask($"Sub-command '{cmd.Name}' matches ask rule: {rule}"));
                return;
            }
        }

        // allow 规则
        foreach (var rule in allowRules)
        {
            var ruleLower = rule.Trim().ToLowerInvariant();
            if (cmdName == ruleLower || canonical == ruleLower)
            {
                decisions.Add(new PsSecurityResult { Behavior = PermissionBehavior.Allow, DecisionReason = "subCommandAllowRule" });
                return;
            }
        }

        // application 类型命令永远不能自动放行
        if (cmd.NameType == PsCommandNameType.Application)
        {
            decisions.Add(PsSecurityResult.Ask($"Command '{cmd.Name}' is an external application which cannot be automatically approved"));
        }
    }

    /// <summary>
    /// 扫描 Provider/UNC 路径
    /// </summary>
    private static PsSecurityResult? ScanProviderAndUncPaths(PsParsedCommand parsed)
    {
        foreach (var cmd in PsAstParser.GetAllCommands(parsed))
        {
            foreach (var arg in cmd.Args)
            {
                // UNC 路径
                if (arg.StartsWith(@"\\", StringComparison.Ordinal) || arg.StartsWith("//", StringComparison.Ordinal))
                {
                    return PsSecurityResult.Ask($"Command uses UNC path which may trigger network authentication: {arg}");
                }

                // 非 FS Provider 路径
                if (IsNonFsProviderArg(arg))
                {
                    return PsSecurityResult.Ask($"Command references a non-filesystem PowerShell provider: {arg}");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 检查参数是否为非文件系统 Provider 路径
    /// </summary>
    private static bool IsNonFsProviderArg(string arg)
    {
        // 去除参数前缀
        var value = arg;
        if (arg.StartsWith('-'))
        {
            var colonIdx = arg.IndexOf(':', 1);
            if (colonIdx > 0)
            {
                value = arg[(colonIdx + 1)..];
            }
            else
            {
                return false; // 纯参数名
            }
        }

        // 匹配 Provider 路径（env:、HKLM:、function:、alias: 等）
        var providerPattern = @"^(?:[\w.]+\\)?(env|hklm|hkcu|function|alias|variable|cert|wsman|registry)::?";
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(value, providerPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch (System.Text.RegularExpressions.RegexParseException)
        {
            return false;
        }
    }

    /// <summary>
    /// 提取命令的第一个词
    /// </summary>
    private static string GetFirstWord(string command)
    {
        var trimmed = command.TrimStart();
        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
    }
}
