namespace JoinCode.Abstractions.Security.Shell;

public static class BashSemanticChecker
{
    public static BashSemanticCheckResult CheckSemantics(BashSimpleCommandInfo[] commands)
<<<<<<< HEAD
        => CheckSemantics(commands, BashSemanticCheckIdMap.Default, null);

    public static BashSemanticCheckResult CheckSemantics(
        BashSimpleCommandInfo[] commands,
        BashSemanticCheckIdMap checkIds,
        Func<string, BashSemanticCheckResult?>? preCheck)
=======
>>>>>>> main
    {
        foreach (var cmd in commands)
        {
            if (cmd.Argv.Length == 0) continue;

            var a = BashSafeWrapperStripper.StripSafeWrappers(cmd.Argv);
            var name = a.Length > 0 ? a[0] : "";

            if (string.IsNullOrEmpty(name)) continue;

<<<<<<< HEAD
            if (preCheck is not null)
            {
                var preResult = preCheck(name);
                if (preResult is not null) return preResult;
            }

            if (name.Length == 0)
                return new BashSemanticCheckResult(false, "空命令名", checkIds.EmptyCommandName);

            if (name.StartsWith('-') || name.StartsWith('|') || name.StartsWith('&'))
                return new BashSemanticCheckResult(false, $"命令 '{name}' 似乎是不完整片段", checkIds.IncompleteFragment);
=======
            if (name.Length == 0)
                return new BashSemanticCheckResult(false, "空命令名", BashSecurityCheckId.EmptyCommandName);

            if (name.StartsWith('-') || name.StartsWith('|') || name.StartsWith('&'))
                return new BashSemanticCheckResult(false, $"命令 '{name}' 似乎是不完整片段", BashSecurityCheckId.IncompleteFragment);
>>>>>>> main

            var subscriptResult = CheckSubscriptEvalFlags(name, a);
            if (!subscriptResult.IsOk) return subscriptResult;

            var arithResult = CheckArithmeticComparison(name, a);
            if (!arithResult.IsOk) return arithResult;

            var bareSubscriptResult = CheckBareSubscriptNameBuiltins(name, a);
            if (!bareSubscriptResult.IsOk) return bareSubscriptResult;

<<<<<<< HEAD
            var keywordResult = CheckShellKeywords(name, checkIds);
=======
            var keywordResult = CheckShellKeywords(name);
>>>>>>> main
            if (!keywordResult.IsOk) return keywordResult;

            var hashResult = CheckNewlineHash(cmd);
            if (!hashResult.IsOk) return hashResult;

            var jqResult = CheckJqSecurity(name, a);
            if (!jqResult.IsOk) return jqResult;

<<<<<<< HEAD
            var zshResult = CheckZshDangerousBuiltins(name, checkIds);
            if (!zshResult.IsOk) return zshResult;

            var evalResult = CheckEvalLikeBuiltins(name, a, checkIds);
=======
            var zshResult = CheckZshDangerousBuiltins(name);
            if (!zshResult.IsOk) return zshResult;

            var evalResult = CheckEvalLikeBuiltins(name, a);
>>>>>>> main
            if (!evalResult.IsOk) return evalResult;

            var procResult = CheckProcEnvironAccess(cmd);
            if (!procResult.IsOk) return procResult;
        }

        return new BashSemanticCheckResult(true);
    }

<<<<<<< HEAD
    private static BashSemanticCheckResult CheckEvalLikeBuiltins(string name, string[] a, BashSemanticCheckIdMap checkIds)
=======
    private static BashSemanticCheckResult CheckEvalLikeBuiltins(string name, string[] a)
>>>>>>> main
    {
        if (!BashSecurityConstants.EvalLikeBuiltins.Contains(name)) return new BashSemanticCheckResult(true);

        if (name.Equals("command", StringComparison.OrdinalIgnoreCase))
        {
            if (a.Length > 1 && (a[1] is "-v" or "-V"))
                return new BashSemanticCheckResult(true);
            return new BashSemanticCheckResult(false,
                $"'{name}' 可绕过函数/别名查找执行命令",
<<<<<<< HEAD
                checkIds.EvalLikeBuiltins);
=======
                BashSecurityCheckId.EvalLikeBuiltins);
>>>>>>> main
        }

        if (name.Equals("fc", StringComparison.OrdinalIgnoreCase))
        {
            if (!BashSecurityConstants.HasExecFlag(a))
                return new BashSemanticCheckResult(true);
        }

        if (name.Equals("compgen", StringComparison.OrdinalIgnoreCase))
        {
            if (!BashSecurityConstants.HasCompgenDangerFlag(a))
                return new BashSemanticCheckResult(true);
        }

        if (name.Equals("builtin", StringComparison.OrdinalIgnoreCase))
        {
            if (a.Length > 1 && BashSecurityConstants.EvalLikeBuiltins.Contains(a[1]))
                return new BashSemanticCheckResult(false,
                    $"builtin {a[1]} 可绕过函数定义执行内置命令",
<<<<<<< HEAD
                    checkIds.EvalLikeBuiltins);
=======
                    BashSecurityCheckId.EvalLikeBuiltins);
>>>>>>> main
            return new BashSemanticCheckResult(true);
        }

        return new BashSemanticCheckResult(false,
            $"'{name}' 可将参数作为Shell代码执行",
<<<<<<< HEAD
            checkIds.EvalLikeBuiltins);
    }

    private static BashSemanticCheckResult CheckZshDangerousBuiltins(string name, BashSemanticCheckIdMap checkIds)
=======
            BashSecurityCheckId.EvalLikeBuiltins);
    }

    private static BashSemanticCheckResult CheckZshDangerousBuiltins(string name)
>>>>>>> main
    {
        if (BashSecurityConstants.ZshDangerousBuiltins.Contains(name))
            return new BashSemanticCheckResult(false,
                $"Zsh内置命令 '{name}' 可绕过安全检查",
<<<<<<< HEAD
                checkIds.ZshDangerousBuiltins);
=======
                BashSecurityCheckId.ZshDangerousBuiltins);
>>>>>>> main

        return new BashSemanticCheckResult(true);
    }

<<<<<<< HEAD
    private static BashSemanticCheckResult CheckShellKeywords(string name, BashSemanticCheckIdMap checkIds)
=======
    private static BashSemanticCheckResult CheckShellKeywords(string name)
>>>>>>> main
    {
        if (BashSecurityConstants.ShellKeywords.Contains(name))
            return new BashSemanticCheckResult(false,
                $"Shell关键字 '{name}' 作为命令名 — 可能是 tree-sitter 误解析",
<<<<<<< HEAD
                checkIds.ShellKeywords);
=======
                BashSecurityCheckId.ShellKeywords);
>>>>>>> main

        return new BashSemanticCheckResult(true);
    }

    private static BashSemanticCheckResult CheckSubscriptEvalFlags(string name, string[] a)
    {
        FrozenSet<string>? dangerFlags = name switch
        {
            "test" or "[" => BashSecurityConstants.SubscriptEvalFlagsTest,
            "printf" => BashSecurityConstants.SubscriptEvalFlagsPrintf,
            "wait" => BashSecurityConstants.SubscriptEvalFlagsWait,
            _ => null,
        };

        if (dangerFlags is null) return new BashSemanticCheckResult(true);

        for (var i = 1; i < a.Length; i++)
        {
            var arg = a[i];
            if (dangerFlags.Contains(arg) && i + 1 < a.Length && a[i + 1].Contains('['))
            {
                return new BashSemanticCheckResult(false,
                    $"'{name} {arg}' 操作数包含数组下标 — bash 会在下标中求值 $(cmd)",
                    BashSecurityCheckId.SubscriptEvalFlags);
            }
            foreach (var flag in dangerFlags)
            {
                if (arg.StartsWith(flag) && arg.Length > flag.Length && arg.Contains('['))
                {
                    return new BashSemanticCheckResult(false,
                        $"'{name} {flag}' (融合) 操作数包含数组下标 — bash 会在下标中求值 $(cmd)",
                        BashSecurityCheckId.SubscriptEvalFlags);
                }
            }
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

    private static BashSemanticCheckResult CheckArithmeticComparison(string name, string[] a)
    {
        if (!name.Equals("[[", StringComparison.Ordinal)) return new BashSemanticCheckResult(true);

        for (var i = 2; i < a.Length; i++)
        {
            if (!BashSecurityConstants.TestArithCmpOps.Contains(a[i])) continue;
            if ((i > 0 && a[i - 1].Contains('[')) || (i + 1 < a.Length && a[i + 1].Contains('[')))
            {
                return new BashSemanticCheckResult(false,
                    $"'[[ ... {a[i]} ... ]]' 操作数包含数组下标 — bash 会在下标中求值 $(cmd)",
                    BashSecurityCheckId.SubscriptEvalFlags);
            }
        }

        return new BashSemanticCheckResult(true);
    }

    private static BashSemanticCheckResult CheckBareSubscriptNameBuiltins(string name, string[] a)
    {
        if (!BashSecurityConstants.BareSubscriptNameBuiltins.Contains(name)) return new BashSemanticCheckResult(true);

        var skipNext = false;
        for (var i = 1; i < a.Length; i++)
        {
            var arg = a[i];
            if (skipNext) { skipNext = false; continue; }

            if (arg.StartsWith('-'))
            {
                if (name.Equals("read", StringComparison.OrdinalIgnoreCase))
                {
                    if (BashSecurityConstants.ReadDataFlags.Contains(arg))
                    {
                        skipNext = true;
                    }
                    else if (arg.Length > 2 && arg[1] != '-')
                    {
                        for (var j = 1; j < arg.Length; j++)
                        {
                            if (BashSecurityConstants.ReadDataFlags.Contains($"-{arg[j]}"))
                            {
                                if (j == arg.Length - 1) skipNext = true;
                                break;
                            }
                        }
                    }
                }
                continue;
            }

            if (arg.Contains('['))
            {
                return new BashSemanticCheckResult(false,
                    $"'{name}' 位置参数 '{arg}' 包含数组下标 — bash 会在下标中求值 $(cmd)",
                    BashSecurityCheckId.SubscriptEvalFlags);
            }
        }

        return new BashSemanticCheckResult(true);
    }

    private static BashSemanticCheckResult CheckNewlineHash(BashSimpleCommandInfo cmd)
    {
        foreach (var arg in cmd.Argv)
        {
            if (arg.Contains('\n') && BashSecurityRegex.NewlineHashRegex().IsMatch(arg))
            {
                return new BashSemanticCheckResult(false,
                    "引号参数中的换行+井号可对路径验证隐藏参数",
                    BashSecurityCheckId.MidWordHash);
            }
        }
        foreach (var ev in cmd.EnvVars)
        {
            if (ev.Value.Contains('\n') && BashSecurityRegex.NewlineHashRegex().IsMatch(ev.Value))
            {
                return new BashSemanticCheckResult(false,
                    "环境变量值中的换行+井号可对路径验证隐藏参数",
                    BashSecurityCheckId.MidWordHash);
            }
        }
        foreach (var r in cmd.Redirects)
        {
            if (r.Target.Contains('\n') && BashSecurityRegex.NewlineHashRegex().IsMatch(r.Target))
            {
                return new BashSemanticCheckResult(false,
                    "重定向目标中的换行+井号可对路径验证隐藏参数",
                    BashSecurityCheckId.MidWordHash);
            }
        }

        return new BashSemanticCheckResult(true);
    }

    private static BashSemanticCheckResult CheckJqSecurity(string name, string[] a)
    {
        if (!name.Equals("jq", StringComparison.OrdinalIgnoreCase))
            return new BashSemanticCheckResult(true);

        foreach (var arg in a)
        {
            if (BashSecurityRegex.JqSystemRegex().IsMatch(arg))
            {
                return new BashSemanticCheckResult(false,
                    "jq system() 函数可执行任意Shell命令",
                    BashSecurityCheckId.JqSystemFunction);
            }
        }

        foreach (var arg in a)
        {
            if (BashSecurityRegex.JqDangerousFlagsRegex().IsMatch(arg))
            {
                return new BashSemanticCheckResult(false,
                    "jq 危险标志可执行代码或读取任意文件",
                    BashSecurityCheckId.JqSystemFunction);
            }
        }

        return new BashSemanticCheckResult(true);
    }

    private static BashSemanticCheckResult CheckProcEnvironAccess(BashSimpleCommandInfo cmd)
    {
        foreach (var arg in cmd.Argv)
        {
            if (arg.Contains("/proc/") && BashSecurityRegex.ProcEnvironRegex().IsMatch(arg))
                return new BashSemanticCheckResult(false,
                    "访问 /proc/*/environ 可暴露敏感环境变量",
                    BashSecurityCheckId.ProcEnvironAccess);
        }

        foreach (var redirect in cmd.Redirects)
        {
            if (redirect.Target.Contains("/proc/") && BashSecurityRegex.ProcEnvironRegex().IsMatch(redirect.Target))
                return new BashSemanticCheckResult(false,
                    "重定向访问 /proc/*/environ 可暴露敏感环境变量",
                    BashSecurityCheckId.ProcEnvironAccess);
        }

        return new BashSemanticCheckResult(true);
    }
}
