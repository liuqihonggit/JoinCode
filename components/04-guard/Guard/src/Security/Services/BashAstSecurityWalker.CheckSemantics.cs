namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker
{
    public BashSemanticCheckResult CheckSemantics(BashSimpleCommandInfo[] commands)
    {
        foreach (var cmd in commands)
        {
            if (cmd.Argv.Length == 0) continue;

            var a = StripSafeWrappers(cmd.Argv);
            var name = a.Length > 0 ? a[0] : "";

            if (string.IsNullOrEmpty(name)) continue;

            if (ContainsAnyPlaceholder(name))
            {
                return new BashSemanticCheckResult(false,
                    $"命令名包含占位符: {name}", BashSecurityCheckId.DangerousVariables);
            }

            if (name.Length == 0)
                return new BashSemanticCheckResult(false, "空命令名", BashSecurityCheckId.IncompleteCommands);

            if (name.StartsWith('-') || name.StartsWith('|') || name.StartsWith('&'))
                return new BashSemanticCheckResult(false, $"命令 '{name}' 似乎是不完整片段", BashSecurityCheckId.IncompleteCommands);

            if (ShellKeywordsSet.Contains(name))
                return new BashSemanticCheckResult(false, $"Shell关键字 '{name}' 作为命令名", BashSecurityCheckId.ObfuscatedFlags);

            if (ZshDangerousBuiltinsSet.Contains(name))
                return new BashSemanticCheckResult(false, $"Zsh内置命令 '{name}' 可绕过安全检查", BashSecurityCheckId.ZshDangerousCommands);

            if (name.Equals("jq", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var arg in a)
                {
                    if (JqSystemRegex().IsMatch(arg))
                        return new BashSemanticCheckResult(false, "jq system() 函数可执行任意Shell命令", BashSecurityCheckId.JqSystemFunction);
                }
                foreach (var arg in a)
                {
                    if (JqDangerousFlagsRegex().IsMatch(arg))
                        return new BashSemanticCheckResult(false, "jq 危险标志可执行代码或读取任意文件", BashSecurityCheckId.JqSystemFunction);
                }
            }

            if (EvalLikeBuiltinsSet.Contains(name))
            {
                if (name.Equals("command", StringComparison.OrdinalIgnoreCase) && a.Length > 1 && (a[1] is "-v" or "-V"))
                { /* 安全 */ }
                else if (name.Equals("fc", StringComparison.OrdinalIgnoreCase) && !HasExecFlag(a))
                { /* fc -l 安全 */ }
                else if (name.Equals("compgen", StringComparison.OrdinalIgnoreCase) && !HasCompgenDangerFlag(a))
                { /* compgen -c 安全 */ }
                else if (name.Equals("builtin", StringComparison.OrdinalIgnoreCase))
                {
                    if (a.Length > 1 && EvalLikeBuiltinsSet.Contains(a[1]))
                        return new BashSemanticCheckResult(false, $"builtin {a[1]} 可绕过函数定义执行内置命令", BashSecurityCheckId.DangerousVariables);
                }
                else
                {
                    return new BashSemanticCheckResult(false, $"'{name}' 可将参数作为Shell代码执行", BashSecurityCheckId.DangerousVariables);
                }
            }

            foreach (var arg in cmd.Argv)
            {
                if (arg.Contains("/proc/") && ProcEnvironRegex().IsMatch(arg))
                    return new BashSemanticCheckResult(false, "访问 /proc/*/environ 可暴露敏感环境变量", BashSecurityCheckId.ProcEnvironAccess);
            }
            foreach (var redirect in cmd.Redirects)
            {
                if (redirect.Target.Contains("/proc/") && ProcEnvironRegex().IsMatch(redirect.Target))
                    return new BashSemanticCheckResult(false, "重定向访问 /proc/*/environ 可暴露敏感环境变量", BashSecurityCheckId.ProcEnvironAccess);
            }
        }

        return new BashSemanticCheckResult(true);
    }

    private static readonly FrozenSet<string> EvalLikeBuiltinsSet = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "eval", "source", ".", "exec", "command", "builtin",
        "fc", "coproc", "noglob", "nocorrect", "trap",
        "enable", "mapfile", "readarray", "hash", "bind",
        "complete", "compgen", "alias", "let");

    private static readonly FrozenSet<string> ZshDangerousBuiltinsSet = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "zmodload", "emulate", "sysopen", "sysread", "syswrite", "sysseek",
        "zpty", "ztcp", "zsocket",
        "zf_rm", "zf_mv", "zf_ln", "zf_chmod", "zf_chown", "zf_mkdir", "zf_rmdir", "zf_chgrp");

    private static readonly FrozenSet<string> ShellKeywordsSet = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "if", "then", "else", "elif", "fi", "while", "until", "for",
        "do", "done", "case", "esac", "in", "function", "select", "time");

    [GeneratedRegex(@"\bsystem\s*\(")]
    private static partial Regex JqSystemRegex();

    [GeneratedRegex(@"^(?:-[fL](?:$|[^A-Za-z])|--(?:from-file|rawfile|slurpfile|library-path)(?:$|=))")]
    private static partial Regex JqDangerousFlagsRegex();

    [GeneratedRegex(@"/proc/\S+/environ")]
    private static partial Regex ProcEnvironRegex();

    [GeneratedRegex(@"^\d+(\.\d+)?[smhd]?$")]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"^-?\d+$")]
    private static partial Regex NiceNumRegex();

    [GeneratedRegex(@"^-\d+$")]
    private static partial Regex NiceLegacyRegex();

    [GeneratedRegex(@"^-[oei]$")]
    private static partial Regex StdbufShortSepRegex();

    [GeneratedRegex(@"^-[oei][0Ll]$")]
    private static partial Regex StdbufShortFusedRegex();

    [GeneratedRegex(@"^--(?:output|error|input)=[0Ll]$")]
    private static partial Regex StdbufLongRegex();

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
                        var i = 1;
                        while (i < a.Length)
                        {
                            var arg = a[i];
                            if (arg is "--foreground" or "--preserve-status" or "--verbose") i++;
                            else if (arg.StartsWith("--kill-after=") || arg.StartsWith("--signal=")) i++;
                            else if ((arg is "--kill-after" or "--signal") && i + 1 < a.Length) i += 2;
                            else if (arg.StartsWith("--")) return a;
                            else if (arg == "-v") i++;
                            else if ((arg is "-k" or "-s") && i + 1 < a.Length) i += 2;
                            else if (arg.StartsWith("-k") || arg.StartsWith("-s")) i++;
                            else if (arg.StartsWith('-')) return a;
                            else break;
                        }
                        if (i < a.Length && DurationRegex().IsMatch(a[i])) a = a[(i + 1)..];
                        else if (i < a.Length) return a;
                        else break;
                    }
                    break;
                case "nice":
                    {
                        if (a.Length > 2 && a[1] == "-n" && NiceNumRegex().IsMatch(a[2])) a = a[3..];
                        else if (a.Length > 1 && NiceLegacyRegex().IsMatch(a[1])) a = a[2..];
                        else if (a.Length > 1 && a[1].Contains('$')) return a;
                        else if (a.Length > 1) a = a[1..];
                        else break;
                    }
                    break;
                case "env":
                    {
                        var i = 1;
                        while (i < a.Length)
                        {
                            var arg = a[i];
                            if (arg.Contains('=') && !arg.StartsWith('-')) i++;
                            else if (arg is "-i" or "-0" or "-v") i++;
                            else if (arg == "-u" && i + 1 < a.Length) i += 2;
                            else if (arg.StartsWith('-')) return a;
                            else break;
                        }
                        if (i < a.Length) a = a[i..];
                        else break;
                    }
                    break;
                case "stdbuf":
                    {
                        var i = 1;
                        while (i < a.Length)
                        {
                            var arg = a[i];
                            if (StdbufShortSepRegex().IsMatch(arg) && i + 1 < a.Length) i += 2;
                            else if (StdbufShortFusedRegex().IsMatch(arg)) i++;
                            else if (StdbufLongRegex().IsMatch(arg)) i++;
                            else if (arg.StartsWith('-')) return a;
                            else break;
                        }
                        if (i > 1 && i < a.Length) a = a[i..];
                        else break;
                    }
                    break;
                default:
                    goto Done;
            }
        }
    Done:
        return a;
    }

    private static bool HasExecFlag(string[] a)
    {
        for (var i = 1; i < a.Length; i++)
        {
            if (a[i].StartsWith('-') && a[i].Length > 1)
            {
                for (var j = 1; j < a[i].Length; j++)
                {
                    if (a[i][j] is 'e' or 's') return true;
                }
            }
        }
        return false;
    }

    private static bool HasCompgenDangerFlag(string[] a)
    {
        for (var i = 1; i < a.Length; i++)
        {
            if (a[i].StartsWith('-') && a[i].Length > 1 && a[i][1] != '-')
            {
                for (var j = 1; j < a[i].Length; j++)
                {
                    if (a[i][j] is 'C' or 'F' or 'W') return true;
                }
            }
        }
        return false;
    }
}
