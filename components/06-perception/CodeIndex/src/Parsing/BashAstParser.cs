namespace CodeIndex.Ast;

/// <summary>
/// Bash AST 解析器 — 使用 TreeSitter.DotNet 解析 bash 命令
/// 对齐 TS ast.ts 的 parseForSecurity 功能
/// </summary>
public sealed partial class BashAstParser : IDisposable
{
    private readonly Language _language;
    private readonly Parser _parser;
    private int _disposed;

    public BashAstParser()
    {
        _language = new Language("bash");
        _parser = new Parser(_language);
    }

    /// <summary>
    /// 解析 bash 命令字符串，返回 AST 根节点。
    /// 返回 null 表示解析失败。
    /// </summary>
    public Node? Parse(string command)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (string.IsNullOrEmpty(command) || command.Length > 10_000)
            return null;

        try
        {
            var tree = _parser.Parse(command);
            return tree?.RootNode;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从 AST 中提取简单命令的 argv 列表。
    /// 对齐 TS ast.ts 的 parseForSecurity 逻辑。
    /// </summary>
    public static List<BashSimpleCommand> ExtractSimpleCommands(Node root)
    {
        var commands = new List<BashSimpleCommand>();
        WalkForCommands(root, commands);
        return commands;
    }

    /// <summary>
    /// 安全分析入口 — 对齐 TS ast.ts parseForSecurity
    /// 解析命令并返回三态结果：Simple/TooComplex/ParseUnavailable
    /// </summary>
    public BashAstSecurityResult ParseForSecurity(string command)
    {
        var root = Parse(command);
        if (root is null)
            return new BashAstSecurityResult.ParseUnavailable("tree-sitter 解析失败");

        // 检查解析错误节点
        if (HasErrorNode(root))
            return new BashAstSecurityResult.TooComplex("AST 包含错误节点");

        var commands = ExtractSimpleCommands(root);
        if (commands.Count == 0)
            return new BashAstSecurityResult.TooComplex("无法提取任何命令");

        return new BashAstSecurityResult.Simple([.. commands]);
    }

    /// <summary>
    /// 语义安全检查 — 对齐 TS ast.ts checkSemantics
    /// 对提取的命令列表进行完整安全规则检查
    /// </summary>
    public static BashSemanticCheckResult CheckSemantics(BashSimpleCommand[] commands)
    {
        foreach (var cmd in commands)
        {
            if (cmd.Argv.Length == 0) continue;

            // 0. 安全包装器剥离 — 对齐 TS checkSemantics 的 wrapper stripping
            var a = StripSafeWrappers(cmd.Argv);
            var name = a.Length > 0 ? a[0] : "";

            if (string.IsNullOrEmpty(name)) continue;

            // 1. 空命令名检测 — 对齐 TS: name === ''
            if (name.Length == 0)
            {
                return new BashSemanticCheckResult(false,
                    "空命令名 — argv[0] 可能不反映 bash 实际执行的命令",
                    BashSecurityCheckId.EmptyCommandName);
            }

            // 2. 不完整片段检测 — 对齐 TS: name.startsWith('-') || '|' || '&'
            if (name.StartsWith('-') || name.StartsWith('|') || name.StartsWith('&'))
            {
                return new BashSemanticCheckResult(false,
                    $"命令 '{name}' 似乎是不完整片段",
                    BashSecurityCheckId.IncompleteFragment);
            }

            // 3. 危险下标标志检测 — 对齐 TS SUBSCRIPT_EVAL_FLAGS
            var subscriptResult = CheckSubscriptEvalFlags(name, a);
            if (!subscriptResult.IsOk) return subscriptResult;

            // 4. [[ 算术比较检测 — 对齐 TS: name === '[['
            var arithResult = CheckArithmeticComparison(name, a);
            if (!arithResult.IsOk) return arithResult;

            // 5. BARE_SUBSCRIPT_NAME_BUILTINS (read/unset) — 对齐 TS
            var bareSubscriptResult = CheckBareSubscriptNameBuiltins(name, a);
            if (!bareSubscriptResult.IsOk) return bareSubscriptResult;

            // 6. Shell关键字检测 — 对齐 TS SHELL_KEYWORDS
            var keywordResult = CheckShellKeywords(name);
            if (!keywordResult.IsOk) return keywordResult;

            // 7. 换行+井号检测 — 对齐 TS NEWLINE_HASH_RE
            var hashResult = CheckNewlineHash(cmd);
            if (!hashResult.IsOk) return hashResult;

            // 8. jq system() + 危险标志检测 — 对齐 TS
            var jqResult = CheckJqSecurity(name, a);
            if (!jqResult.IsOk) return jqResult;

            // 9. Zsh危险命令检测 — 对齐 TS ZSH_DANGEROUS_BUILTINS
            var zshResult = CheckZshDangerousBuiltins(name);
            if (!zshResult.IsOk) return zshResult;

            // 10. eval类内置命令检测 — 对齐 TS EVAL_LIKE_BUILTINS
            var evalResult = CheckEvalLikeBuiltins(name, a);
            if (!evalResult.IsOk) return evalResult;

            // 11. /proc/*/environ 访问检测
            var procResult = CheckProcEnvironAccess(cmd);
            if (!procResult.IsOk) return procResult;
        }

        return new BashSemanticCheckResult(true);
    }

    #region 安全包装器剥离

    /// <summary>
    /// 剥离安全包装命令 — 对齐 TS checkSemantics 的 wrapper stripping
    /// timeout/nice/env/stdbuf/nohup/time 这些命令只是包装器，实际执行的是内部命令
    /// </summary>
    private static string[] StripSafeWrappers(string[] argv)
    {
        var a = argv;
        for (; ; )
        {
            if (a.Length == 0) break;

            switch (a[0])
            {
                case "time":
                case "nohup":
                    a = a[1..];
                    break;

                case "timeout":
                    {
                        // 对齐 TS: 跳过 timeout 的标志和持续时间参数
                        var i = 1;
                        while (i < a.Length)
                        {
                            var arg = a[i];
                            if (arg is "--foreground" or "--preserve-status" or "--verbose")
                            {
                                i++;
                            }
                            else if (arg.StartsWith("--kill-after=") || arg.StartsWith("--signal="))
                            {
                                i++; // 融合值的长标志
                            }
                            else if ((arg is "--kill-after" or "--signal") &&
                                     i + 1 < a.Length)
                            {
                                i += 2; // 空格分隔值的长标志
                            }
                            else if (arg.StartsWith("--"))
                            {
                                // 未知长标志 — 无法静态分析
                                return a;
                            }
                            else if (arg == "-v")
                            {
                                i++;
                            }
                            else if ((arg is "-k" or "-s") && i + 1 < a.Length)
                            {
                                i += 2; // -k DUR / -s SIG
                            }
                            else if (arg.StartsWith("-k") || arg.StartsWith("-s"))
                            {
                                i++; // 融合: -k5, -sTERM
                            }
                            else if (arg.StartsWith('-'))
                            {
                                // 未知短标志 — 无法静态分析
                                return a;
                            }
                            else
                            {
                                break; // 非标志 — 应该是持续时间
                            }
                        }
                        // 检查持续时间参数
                        if (i < a.Length && DurationRegex().IsMatch(a[i]))
                        {
                            a = a[(i + 1)..];
                        }
                        else if (i < a.Length)
                        {
                            // 无法识别的持续时间格式 — 无法静态分析
                            return a;
                        }
                        else
                        {
                            break; // timeout 无后续命令
                        }
                    }
                    break;

                case "nice":
                    {
                        // nice cmd / nice -n N cmd / nice -N cmd
                        if (a.Length > 2 && a[1] == "-n" && NiceNumRegex().IsMatch(a[2]))
                        {
                            a = a[3..];
                        }
                        else if (a.Length > 1 && NiceLegacyRegex().IsMatch(a[1]))
                        {
                            a = a[2..]; // nice -10 cmd
                        }
                        else if (a.Length > 1 && a[1].Contains('$'))
                        {
                            // 包含变量展开 — 无法静态分析
                            return a;
                        }
                        else if (a.Length > 1)
                        {
                            a = a[1..]; // bare nice cmd
                        }
                        else
                        {
                            break;
                        }
                    }
                    break;

                case "env":
                    {
                        // env [VAR=val...] [-i] [-0] [-v] [-u NAME] cmd args
                        var i = 1;
                        while (i < a.Length)
                        {
                            var arg = a[i];
                            if (arg.Contains('=') && !arg.StartsWith('-'))
                            {
                                i++; // VAR=val 赋值
                            }
                            else if (arg is "-i" or "-0" or "-v")
                            {
                                i++; // 无参数标志
                            }
                            else if (arg == "-u" && i + 1 < a.Length)
                            {
                                i += 2; // -u NAME
                            }
                            else if (arg.StartsWith('-'))
                            {
                                // -S (argv分割器), -C (替代目录), -P (替代路径) 等
                                // 无法静态分析
                                return a;
                            }
                            else
                            {
                                break; // 被包装的命令
                            }
                        }
                        if (i < a.Length)
                        {
                            a = a[i..];
                        }
                        else
                        {
                            break; // env 无后续命令
                        }
                    }
                    break;

                case "stdbuf":
                    {
                        // stdbuf -o0 cmd / stdbuf -o 0 cmd / stdbuf --output=0 cmd
                        var i = 1;
                        while (i < a.Length)
                        {
                            var arg = a[i];
                            // -o MODE (空格分隔)
                            if (StdbufShortSepRegex().IsMatch(arg) && i + 1 < a.Length)
                            {
                                i += 2;
                            }
                            // -o0 (融合)
                            else if (StdbufShortFusedRegex().IsMatch(arg))
                            {
                                i++;
                            }
                            // --output=MODE (融合长标志)
                            else if (StdbufLongRegex().IsMatch(arg))
                            {
                                i++;
                            }
                            else if (arg.StartsWith('-'))
                            {
                                // 未知标志 — 无法静态分析
                                return a;
                            }
                            else
                            {
                                break; // 被包装的命令
                            }
                        }
                        if (i > 1 && i < a.Length)
                        {
                            a = a[i..];
                        }
                        else
                        {
                            break; // stdbuf 无标志或无后续命令
                        }
                    }
                    break;

                default:
                    goto Done; // 不是包装器，退出循环
            }
        }
    Done:
        return a;
    }

    [GeneratedRegex(@"^\d+(\.\d+)?[smhd]?$")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"^-?\d+$")]
    private static partial Regex NiceNumRegex();

    [GeneratedRegex(@"^-\d+$")]
    private static partial Regex NiceLegacyRegex();

    // stdbuf 短标志 空格分隔: -o MODE, -e MODE, -i MODE
    [GeneratedRegex(@"^-[oei]$")]
    private static partial Regex StdbufShortSepRegex();

    // stdbuf 短标志 融合: -o0, -eL, -i0
    [GeneratedRegex(@"^-[oei][0Ll]$")]
    private static partial Regex StdbufShortFusedRegex();

    // stdbuf 长标志 融合: --output=0, --error=L, --input=0
    [GeneratedRegex(@"^--(?:output|error|input)=[0Ll]$")]
    private static partial Regex StdbufLongRegex();

    #endregion

    // 对齐 TS EVAL_LIKE_BUILTINS — 可执行任意代码的内置命令
    private static readonly FrozenSet<string> EvalLikeBuiltins = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "eval", "source", ".", "exec", "command", "builtin",
        "fc", "coproc", "noglob", "nocorrect", "trap",
        "enable", "mapfile", "readarray", "hash", "bind",
        "complete", "compgen", "alias", "let");

    /// <summary>
    /// eval类内置命令检测 — 对齐 TS EVAL_LIKE_BUILTINS
    /// 特殊处理: command -v/-V 安全, fc -l 安全, compgen -c/-f/-v 安全
    /// </summary>
    private static BashSemanticCheckResult CheckEvalLikeBuiltins(string name, string[] a)
    {
        if (!EvalLikeBuiltins.Contains(name)) return new BashSemanticCheckResult(true);

        // command -v/-V — 仅打印路径，不执行
        if (name.Equals("command", StringComparison.OrdinalIgnoreCase))
        {
            if (a.Length > 1 && (a[1] is "-v" or "-V"))
                return new BashSemanticCheckResult(true); // 安全，继续后续检查
            // bare command foo — 绕过函数/别名查找
            return new BashSemanticCheckResult(false,
                $"'{name}' 可绕过函数/别名查找执行命令",
                BashSecurityCheckId.EvalLikeBuiltins);
        }

        // fc -l/-ln 列出历史 — 安全; fc -e/-s 执行 — 危险
        if (name.Equals("fc", StringComparison.OrdinalIgnoreCase))
        {
            var hasExecFlag = false;
            for (var i = 1; i < a.Length; i++)
            {
                if (a[i].StartsWith('-') && a[i].Length > 1)
                {
                    // 检查是否包含 e 或 s 标志
                    for (var j = 1; j < a[i].Length; j++)
                    {
                        if (a[i][j] is 'e' or 's')
                        {
                            hasExecFlag = true;
                            break;
                        }
                    }
                }
                if (hasExecFlag) break;
            }
            if (!hasExecFlag)
                return new BashSemanticCheckResult(true); // fc -l 安全
        }

        // compgen -C/-F/-W 危险; compgen -c/-f/-v 仅列出补全 — 安全
        if (name.Equals("compgen", StringComparison.OrdinalIgnoreCase))
        {
            var hasDangerFlag = false;
            for (var i = 1; i < a.Length; i++)
            {
                if (a[i].StartsWith('-') && a[i].Length > 1 && a[i][1] != '-')
                {
                    for (var j = 1; j < a[i].Length; j++)
                    {
                        if (a[i][j] is 'C' or 'F' or 'W')
                        {
                            hasDangerFlag = true;
                            break;
                        }
                    }
                }
                if (hasDangerFlag) break;
            }
            if (!hasDangerFlag)
                return new BashSemanticCheckResult(true); // compgen -c 安全
        }

        // builtin — 如果后面跟危险命令则标记
        if (name.Equals("builtin", StringComparison.OrdinalIgnoreCase))
        {
            if (a.Length > 1 && EvalLikeBuiltins.Contains(a[1]))
                return new BashSemanticCheckResult(false,
                    $"builtin {a[1]} 可绕过函数定义执行内置命令",
                    BashSecurityCheckId.EvalLikeBuiltins);
            return new BashSemanticCheckResult(true);
        }

        return new BashSemanticCheckResult(false,
            $"'{name}' 可将参数作为Shell代码执行",
            BashSecurityCheckId.EvalLikeBuiltins);
    }

    // 对齐 TS ZSH_DANGEROUS_BUILTINS
    private static readonly FrozenSet<string> ZshDangerousBuiltins = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "zmodload", "emulate", "sysopen", "sysread", "syswrite", "sysseek",
        "zpty", "ztcp", "zsocket",
        "zf_rm", "zf_mv", "zf_ln", "zf_chmod", "zf_chown", "zf_mkdir", "zf_rmdir", "zf_chgrp");

    private static BashSemanticCheckResult CheckZshDangerousBuiltins(string name)
    {
        if (ZshDangerousBuiltins.Contains(name))
            return new BashSemanticCheckResult(false,
                $"Zsh内置命令 '{name}' 可绕过安全检查",
                BashSecurityCheckId.ZshDangerousBuiltins);

        return new BashSemanticCheckResult(true);
    }

    // 对齐 TS SUBSCRIPT_EVAL_FLAGS — 命令名 → 危险标志集合
    // bash 会对 arr[EXPR] 的 EXPR 进行算术求值，运行 $(cmd)
    private static readonly FrozenSet<string> SubscriptEvalFlagsTest = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "-v");
    private static readonly FrozenSet<string> SubscriptEvalFlagsPrintf = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "-v");
    private static readonly FrozenSet<string> SubscriptEvalFlagsWait = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "-n");

    /// <summary>
    /// 危险下标标志检测 — 对齐 TS SUBSCRIPT_EVAL_FLAGS
    /// 检测 test -v/printf -v/wait -n 后跟包含 [ 的操作数
    /// </summary>
    private static BashSemanticCheckResult CheckSubscriptEvalFlags(string name, string[] a)
    {
        FrozenSet<string>? dangerFlags = name switch
        {
            "test" or "[" => SubscriptEvalFlagsTest,
            "printf" => SubscriptEvalFlagsPrintf,
            "wait" => SubscriptEvalFlagsWait,
            _ => null,
        };

        if (dangerFlags is null) return new BashSemanticCheckResult(true);

        for (var i = 1; i < a.Length; i++)
        {
            var arg = a[i];
            // 独立标志: -v 后跟包含 [ 的操作数
            if (dangerFlags.Contains(arg) && i + 1 < a.Length && a[i + 1].Contains('['))
            {
                return new BashSemanticCheckResult(false,
                    $"'{name} {arg}' 操作数包含数组下标 — bash 会在下标中求值 $(cmd)",
                    BashSecurityCheckId.SubscriptEvalFlags);
            }
            // 融合标志: -vNAME 中包含 [
            foreach (var flag in dangerFlags)
            {
                if (arg.StartsWith(flag) && arg.Length > flag.Length && arg.Contains('['))
                {
                    return new BashSemanticCheckResult(false,
                        $"'{name} {flag}' (融合) 操作数包含数组下标 — bash 会在下标中求值 $(cmd)",
                        BashSecurityCheckId.SubscriptEvalFlags);
                }
            }
            // 组合短标志: -av 中包含危险标志字符
            if (arg.Length > 2 && arg[0] == '-' && arg[1] != '-' && !arg.Contains('['))
            {
                foreach (var flag in dangerFlags)
                {
                    if (flag.Length == 2 && arg.Contains(flag[1]))
                    {
                        if (i + 1 < a.Length && a[i + 1].Contains('['))
                        {
                            return new BashSemanticCheckResult(false,
                                $"'{name} {flag}' (组合在 '{arg}' 中) 操作数包含数组下标",
                                BashSecurityCheckId.SubscriptEvalFlags);
                        }
                    }
                }
            }
        }

        return new BashSemanticCheckResult(true);
    }

    // 对齐 TS TEST_ARITH_CMP_OPS — [[ ... ]] 中的算术比较操作符
    private static readonly FrozenSet<string> TestArithCmpOps = FrozenSet.Create(
        StringComparer.Ordinal,
        "-eq", "-ne", "-lt", "-le", "-gt", "-ge");

    /// <summary>
    /// [[ 算术比较检测 — 对齐 TS
    /// [[ ARG OP ARG ]] 中 OP 为算术比较时，bash 会对两侧操作数进行算术求值
    /// </summary>
    private static BashSemanticCheckResult CheckArithmeticComparison(string name, string[] a)
    {
        if (!name.Equals("[[", StringComparison.Ordinal)) return new BashSemanticCheckResult(true);

        // a[0]='[[', a[1] 是第一个操作数，算术操作符最早出现在 a[2]
        for (var i = 2; i < a.Length; i++)
        {
            if (!TestArithCmpOps.Contains(a[i])) continue;
            if ((i > 0 && a[i - 1].Contains('[')) || (i + 1 < a.Length && a[i + 1].Contains('[')))
            {
                return new BashSemanticCheckResult(false,
                    $"'[[ ... {a[i]} ... ]]' 操作数包含数组下标 — bash 会在下标中求值 $(cmd)",
                    BashSecurityCheckId.SubscriptEvalFlags);
            }
        }

        return new BashSemanticCheckResult(true);
    }

    // 对齐 TS BARE_SUBSCRIPT_NAME_BUILTINS — 位置参数即 NAME 的命令
    private static readonly FrozenSet<string> BareSubscriptNameBuiltins = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "read", "unset");

    // 对齐 TS READ_DATA_FLAGS — read 的数据获取标志（跳过其操作数）
    private static readonly FrozenSet<string> ReadDataFlags = FrozenSet.Create(
        StringComparer.Ordinal, "-p", "-d", "-t", "-n", "-N", "-u");

    /// <summary>
    /// BARE_SUBSCRIPT_NAME_BUILTINS 检测 — 对齐 TS
    /// read/unset 的每个位置参数都是 NAME，bash 会对 arr[EXPR] 求值
    /// </summary>
    private static BashSemanticCheckResult CheckBareSubscriptNameBuiltins(string name, string[] a)
    {
        if (!BareSubscriptNameBuiltins.Contains(name)) return new BashSemanticCheckResult(true);

        var skipNext = false;
        for (var i = 1; i < a.Length; i++)
        {
            var arg = a[i];
            if (skipNext) { skipNext = false; continue; }

            if (arg.StartsWith('-'))
            {
                if (name.Equals("read", StringComparison.OrdinalIgnoreCase))
                {
                    if (ReadDataFlags.Contains(arg))
                    {
                        skipNext = true;
                    }
                    else if (arg.Length > 2 && arg[1] != '-')
                    {
                        // 组合短标志如 -rp — 最后一个字符是数据标志则跳过下一个
                        for (var j = 1; j < arg.Length; j++)
                        {
                            if (ReadDataFlags.Contains($"-{arg[j]}"))
                            {
                                if (j == arg.Length - 1) skipNext = true;
                                break;
                            }
                        }
                    }
                }
                continue;
            }

            // 位置参数包含 [ — 危险
            if (arg.Contains('['))
            {
                return new BashSemanticCheckResult(false,
                    $"'{name}' 位置参数 '{arg}' 包含数组下标 — bash 会在下标中求值 $(cmd)",
                    BashSecurityCheckId.SubscriptEvalFlags);
            }
        }

        return new BashSemanticCheckResult(true);
    }

    // Shell关键字检测 — 对齐 TS SHELL_KEYWORDS
    private static readonly FrozenSet<string> ShellKeywords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "if", "then", "else", "elif", "fi", "while", "until", "for",
        "do", "done", "case", "esac", "in", "function", "select", "time");

    private static BashSemanticCheckResult CheckShellKeywords(string name)
    {
        if (ShellKeywords.Contains(name))
            return new BashSemanticCheckResult(false,
                $"Shell关键字 '{name}' 作为命令名 — 可能是 tree-sitter 误解析",
                BashSecurityCheckId.ShellKeywords);

        return new BashSemanticCheckResult(true);
    }

    // 对齐 TS NEWLINE_HASH_RE — 换行后跟 # 的模式
    [GeneratedRegex(@"\n\s*#")]
    private static partial Regex NewlineHashRegex();

    /// <summary>
    /// 换行+井号检测 — 对齐 TS NEWLINE_HASH_RE
    /// 引号内的换行+井号可对路径验证隐藏参数
    /// </summary>
    private static BashSemanticCheckResult CheckNewlineHash(BashSimpleCommand cmd)
    {
        // 检查 argv
        foreach (var arg in cmd.Argv)
        {
            if (arg.Contains('\n') && NewlineHashRegex().IsMatch(arg))
            {
                return new BashSemanticCheckResult(false,
                    "引号参数中的换行+井号可对路径验证隐藏参数",
                    BashSecurityCheckId.MidWordHash);
            }
        }
        // 检查环境变量值
        foreach (var ev in cmd.EnvVars)
        {
            if (ev.Value.Contains('\n') && NewlineHashRegex().IsMatch(ev.Value))
            {
                return new BashSemanticCheckResult(false,
                    "环境变量值中的换行+井号可对路径验证隐藏参数",
                    BashSecurityCheckId.MidWordHash);
            }
        }
        // 检查重定向目标
        foreach (var r in cmd.Redirects)
        {
            if (r.Target.Contains('\n') && NewlineHashRegex().IsMatch(r.Target))
            {
                return new BashSemanticCheckResult(false,
                    "重定向目标中的换行+井号可对路径验证隐藏参数",
                    BashSecurityCheckId.MidWordHash);
            }
        }

        return new BashSemanticCheckResult(true);
    }

    // 对齐 TS PROC_ENVIRON_RE
    [GeneratedRegex(@"/proc/\S+/environ")]
    private static partial Regex ProcEnvironRegex();

    /// <summary>
    /// /proc/*/environ 访问检测 — 对齐 TS
    /// </summary>
    private static BashSemanticCheckResult CheckProcEnvironAccess(BashSimpleCommand cmd)
    {
        foreach (var arg in cmd.Argv)
        {
            if (arg.Contains("/proc/") && ProcEnvironRegex().IsMatch(arg))
                return new BashSemanticCheckResult(false,
                    "访问 /proc/*/environ 可暴露敏感环境变量",
                    BashSecurityCheckId.ProcEnvironAccess);
        }

        foreach (var redirect in cmd.Redirects)
        {
            if (redirect.Target.Contains("/proc/") && ProcEnvironRegex().IsMatch(redirect.Target))
                return new BashSemanticCheckResult(false,
                    "重定向访问 /proc/*/environ 可暴露敏感环境变量",
                    BashSecurityCheckId.ProcEnvironAccess);
        }

        return new BashSemanticCheckResult(true);
    }

    // 对齐 TS jq 危险标志正则
    [GeneratedRegex(@"^(?:-[fL](?:$|[^A-Za-z])|--(?:from-file|rawfile|slurpfile|library-path)(?:$|=))")]
    private static partial Regex JqDangerousFlagsRegex();

    /// <summary>
    /// jq 安全检测 — 对齐 TS checkSemantics 的 jq 检查
    /// system() 函数 + 危险标志 (--from-file, -f 等)
    /// </summary>
    private static BashSemanticCheckResult CheckJqSecurity(string name, string[] a)
    {
        if (!name.Equals("jq", StringComparison.OrdinalIgnoreCase))
            return new BashSemanticCheckResult(true);

        // system() 函数检测
        foreach (var arg in a)
        {
            if (JqSystemRegex().IsMatch(arg))
            {
                return new BashSemanticCheckResult(false,
                    "jq system() 函数可执行任意Shell命令",
                    BashSecurityCheckId.JqSystemFunction);
            }
        }

        // 危险标志检测
        foreach (var arg in a)
        {
            if (JqDangerousFlagsRegex().IsMatch(arg))
            {
                return new BashSemanticCheckResult(false,
                    "jq 危险标志可执行代码或读取任意文件",
                    BashSecurityCheckId.JqSystemFunction);
            }
        }

        return new BashSemanticCheckResult(true);
    }

    [GeneratedRegex(@"\bsystem\s*\(")]
    private static partial Regex JqSystemRegex();

    private static void WalkForCommands(Node node, List<BashSimpleCommand> commands)
    {
        switch (node.Type)
        {
            // 结构类型 — 递归
            case "program":
            case "list":
            case "pipeline":
            case "subshell":
            case "compound_statement":
            case "if_command":
            case "while_command":
            case "for_command":
            case "case_command":
            case "function_definition":
                foreach (var child in node.Children)
                    WalkForCommands(child, commands);
                break;

            // redirected_statement — 需要将同级 file_redirect 关联到 command
            case "redirected_statement":
                ExtractRedirectedStatement(node, commands);
                break;

            // 命令类型 — 提取 argv
            case "command":
            case "declaration_command":
                var cmd = ExtractCommand(node);
                if (cmd is not null) commands.Add(cmd);
                break;
        }
    }

    /// <summary>
    /// 处理 redirected_statement — 将同级 file_redirect 关联到 command
    /// AST 结构: redirected_statement → [command, file_redirect, ...]
    /// </summary>
    private static void ExtractRedirectedStatement(Node node, List<BashSimpleCommand> commands)
    {
        BashSimpleCommand? baseCmd = null;
        var redirects = new List<BashRedirect>();

        foreach (var child in node.Children)
        {
            switch (child.Type)
            {
                case "command":
                case "declaration_command":
                    baseCmd = ExtractCommand(child);
                    break;

                case "file_redirect":
                    var redirect = ExtractRedirect(child);
                    if (redirect is not null) redirects.Add(redirect);
                    break;

                // 嵌套结构继续递归
                default:
                    WalkForCommands(child, commands);
                    break;
            }
        }

        // 将 redirect 合并到 command
        if (baseCmd is not null && redirects.Count > 0)
        {
            var mergedRedirects = baseCmd.Redirects.ToList();
            mergedRedirects.AddRange(redirects);
            baseCmd = baseCmd with { Redirects = [.. mergedRedirects] };
        }

        if (baseCmd is not null) commands.Add(baseCmd);
    }

    private static BashSimpleCommand? ExtractCommand(Node commandNode)
    {
        var argv = new List<string>();
        var envVars = new List<BashEnvVar>();
        var redirects = new List<BashRedirect>();

        foreach (var child in commandNode.Children)
        {
            switch (child.Type)
            {
                case "variable_assignment":
                    var eqIdx = child.Text.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        envVars.Add(new BashEnvVar(
                            child.Text[..eqIdx],
                            eqIdx + 1 < child.Text.Length ? child.Text[(eqIdx + 1)..] : ""));
                    }
                    break;

                case "command_name":
                    argv.Add(StripQuotes(child.Text));
                    break;

                case "word":
                case "string":
                case "raw_string":
                case "number":
                    argv.Add(StripQuotes(child.Text));
                    break;

                case "simple_expansion":
                case "expansion":
                case "command_substitution":
                case "arithmetic_expansion":
                    argv.Add(child.Text);
                    break;

                case "file_redirect":
                    var redirect = ExtractRedirect(child);
                    if (redirect is not null) redirects.Add(redirect);
                    break;
            }
        }

        if (argv.Count == 0) return null;

        return new BashSimpleCommand(
            [.. argv],
            [.. envVars],
            [.. redirects],
            commandNode.Text);
    }

    private static BashRedirect? ExtractRedirect(Node redirectNode)
    {
        // file_redirect 结构: [fd?] operator target
        var children = redirectNode.Children;
        if (children.Count == 0) return null;

        var op = "";
        var target = "";
        var fd = -1;

        foreach (var child in children)
        {
            switch (child.Type)
            {
                case "file_descriptor":
                    fd = int.TryParse(child.Text, out var f) ? f : -1;
                    break;
                case ">":
                case ">>":
                case "<":
                case ">&":
                case "<&":
                case ">|":
                case "&>":
                case "&>>":
                    op = child.Type;
                    break;
                case "word":
                case "string":
                case "simple_expansion":
                    target = child.Text;
                    break;
            }
        }

        if (string.IsNullOrEmpty(op)) return null;

        return new BashRedirect(op, target, fd >= 0 ? fd : null);
    }

    private static string StripQuotes(string text)
    {
        if (text.Length >= 2)
        {
            if ((text[0] == '"' && text[^1] == '"') ||
                (text[0] == '\'' && text[^1] == '\''))
                return text[1..^1];
        }
        return text;
    }

    /// <summary>
    /// 检查 AST 中是否包含错误节点 — 对齐 TS parseForSecurity 的 ERROR 检测
    /// </summary>
    private static bool HasErrorNode(Node node)
    {
        if (node.IsError || node.IsMissing) return true;
        foreach (var child in node.Children)
        {
            if (HasErrorNode(child)) return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _parser.Dispose();
        _language.Dispose();
    }
}

/// <summary>简单命令 — 对齐 TS ast.ts SimpleCommand</summary>
public sealed record BashSimpleCommand(
    string[] Argv,
    BashEnvVar[] EnvVars,
    BashRedirect[] Redirects,
    string Text);

/// <summary>环境变量赋值</summary>
public sealed record BashEnvVar(string Name, string Value);

/// <summary>重定向</summary>
public sealed record BashRedirect(string Op, string Target, int? Fd);

/// <summary>
/// 安全分析结果 — 对齐 TS ast.ts ParseForSecurityResult
/// 三态: Simple(可静态分析) / TooComplex(过于复杂) / ParseUnavailable(解析不可用)
/// </summary>
public abstract record BashAstSecurityResult
{
    /// <summary>命令可静态分析，提取的命令列表安全</summary>
    public sealed record Simple(BashSimpleCommand[] Commands) : BashAstSecurityResult;

    /// <summary>命令过于复杂，无法静态分析</summary>
    public sealed record TooComplex(string Reason) : BashAstSecurityResult;

    /// <summary>解析不可用（tree-sitter 未安装等）</summary>
    public sealed record ParseUnavailable(string Reason) : BashAstSecurityResult;
}

/// <summary>
/// 语义检查结果 — 对齐 TS ast.ts SemanticCheckResult
/// </summary>
public sealed record BashSemanticCheckResult(
    bool IsOk,
    string? Reason = null,
    BashSecurityCheckId? CheckId = null);

/// <summary>
/// 安全检查ID — 对齐 TS BASH_SECURITY_CHECK_IDS
/// </summary>
public enum BashSecurityCheckId
{
    /// <summary>eval类内置命令(eval/source/exec/command等)</summary>
    [EnumValue("evalLikeBuiltins")]
    EvalLikeBuiltins = 1,
    /// <summary>Zsh危险命令(zmodload/emulate等)</summary>
    [EnumValue("zshDangerousBuiltins")]
    ZshDangerousBuiltins = 2,
    /// <summary>危险下标标志(test -v/printf -v等)</summary>
    [EnumValue("subscriptEvalFlags")]
    SubscriptEvalFlags = 3,
    /// <summary>Shell关键字(if/while/for等)</summary>
    [EnumValue("shellKeywords")]
    ShellKeywords = 4,
    /// <summary>/proc/*/environ访问</summary>
    [EnumValue("procEnvironAccess")]
    ProcEnvironAccess = 5,
    /// <summary>jq system()函数</summary>
    [EnumValue("jqSystemFunction")]
    JqSystemFunction = 6,
    /// <summary>词中井号(潜在注释注入)</summary>
    [EnumValue("midWordHash")]
    MidWordHash = 7,
    /// <summary>空命令名</summary>
    [EnumValue("emptyCommandName")]
    EmptyCommandName = 8,
    /// <summary>不完整片段(argv[0]以-开头)</summary>
    [EnumValue("incompleteFragment")]
    IncompleteFragment = 9,
}
