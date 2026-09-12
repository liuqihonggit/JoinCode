namespace JoinCode.Abstractions.Security.Shell;

public static class BashSecurityConstants
{
    public static readonly FrozenSet<string> EvalLikeBuiltins = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "eval", "source", ".", "exec", "command", "builtin",
        "fc", "coproc", "noglob", "nocorrect", "trap",
        "enable", "mapfile", "readarray", "hash", "bind",
        "complete", "compgen", "alias", "let");

    public static readonly FrozenSet<string> ZshDangerousBuiltins = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "zmodload", "emulate", "sysopen", "sysread", "syswrite", "sysseek",
        "zpty", "ztcp", "zsocket",
        "zf_rm", "zf_mv", "zf_ln", "zf_chmod", "zf_chown", "zf_mkdir", "zf_rmdir", "zf_chgrp");

    public static readonly FrozenSet<string> ShellKeywords = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "if", "then", "else", "elif", "fi", "while", "until", "for",
        "do", "done", "case", "esac", "in", "function", "select", "time");

    public static readonly FrozenSet<string> SubscriptEvalFlagsTest = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "-v");

    public static readonly FrozenSet<string> SubscriptEvalFlagsPrintf = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "-v");

    public static readonly FrozenSet<string> SubscriptEvalFlagsWait = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "-n");

    public static readonly FrozenSet<string> TestArithCmpOps = FrozenSet.Create(
        StringComparer.Ordinal,
        "-eq", "-ne", "-lt", "-le", "-gt", "-ge");

    public static readonly FrozenSet<string> BareSubscriptNameBuiltins = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "read", "unset");

    public static readonly FrozenSet<string> ReadDataFlags = FrozenSet.Create(
        StringComparer.Ordinal, "-p", "-d", "-t", "-n", "-N", "-u");

    public static readonly FrozenSet<string> SafeEnvVars = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "HOME", "PWD", "OLDPWD", "USER", "LOGNAME", "SHELL", "PATH",
        "HOSTNAME", "UID", "EUID", "PPID", "RANDOM", "SECONDS", "LINENO",
        "TMPDIR", "BASH_VERSION", "BASHPID", "SHLVL", "HISTFILE", "IFS");

    public static readonly FrozenSet<string> SpecialVarNames = FrozenSet.Create(
        StringComparer.Ordinal,
        "?", "!", "#", "$", "0", "-", "@", "*");

    public static readonly FrozenSet<string> StructuralTypes = FrozenSet.Create(
        StringComparer.Ordinal,
        "program", "list", "pipeline", "redirected_statement");

    public static readonly FrozenSet<string> SeparatorTypes = FrozenSet.Create(
        StringComparer.Ordinal,
        "&&", "||", "|", "|&", "&", ";;", ";", ";;&", ";&", "\n");

    public static readonly FrozenSet<string> DeclarationCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "export", "local", "readonly", "declare", "typeset", "nameref");

    public static bool HasExecFlag(string[] a)
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

    public static bool HasCompgenDangerFlag(string[] a)
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
