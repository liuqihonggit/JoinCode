namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker
{
    public BashSemanticCheckResult CheckSemantics(BashSimpleCommandInfo[] commands)
    {
        foreach (var cmd in commands)
        {
            if (cmd.Argv.Length == 0) continue;

            var a = BashSafeWrapperStripper.StripSafeWrappers(cmd.Argv);
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

            if (BashSecurityConstants.ShellKeywords.Contains(name))
                return new BashSemanticCheckResult(false, $"Shell关键字 '{name}' 作为命令名", BashSecurityCheckId.ObfuscatedFlags);

            if (BashSecurityConstants.ZshDangerousBuiltins.Contains(name))
                return new BashSemanticCheckResult(false, $"Zsh内置命令 '{name}' 可绕过安全检查", BashSecurityCheckId.ZshDangerousCommands);

            if (name.Equals("jq", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var arg in a)
                {
                    if (BashSecurityRegex.JqSystemRegex().IsMatch(arg))
                        return new BashSemanticCheckResult(false, "jq system() 函数可执行任意Shell命令", BashSecurityCheckId.JqSystemFunction);
                }
                foreach (var arg in a)
                {
                    if (BashSecurityRegex.JqDangerousFlagsRegex().IsMatch(arg))
                        return new BashSemanticCheckResult(false, "jq 危险标志可执行代码或读取任意文件", BashSecurityCheckId.JqSystemFunction);
                }
            }

            if (BashSecurityConstants.EvalLikeBuiltins.Contains(name))
            {
                if (name.Equals("command", StringComparison.OrdinalIgnoreCase) && a.Length > 1 && (a[1] is "-v" or "-V"))
                { /* 安全 */ }
                else if (name.Equals("fc", StringComparison.OrdinalIgnoreCase) && !BashSecurityConstants.HasExecFlag(a))
                { /* fc -l 安全 */ }
                else if (name.Equals("compgen", StringComparison.OrdinalIgnoreCase) && !BashSecurityConstants.HasCompgenDangerFlag(a))
                { /* compgen -c 安全 */ }
                else if (name.Equals("builtin", StringComparison.OrdinalIgnoreCase))
                {
                    if (a.Length > 1 && BashSecurityConstants.EvalLikeBuiltins.Contains(a[1]))
                        return new BashSemanticCheckResult(false, $"builtin {a[1]} 可绕过函数定义执行内置命令", BashSecurityCheckId.DangerousVariables);
                }
                else
                {
                    return new BashSemanticCheckResult(false, $"'{name}' 可将参数作为Shell代码执行", BashSecurityCheckId.DangerousVariables);
                }
            }

            foreach (var arg in cmd.Argv)
            {
                if (arg.Contains("/proc/") && BashSecurityRegex.ProcEnvironRegex().IsMatch(arg))
                    return new BashSemanticCheckResult(false, "访问 /proc/*/environ 可暴露敏感环境变量", BashSecurityCheckId.ProcEnvironAccess);
            }
            foreach (var redirect in cmd.Redirects)
            {
                if (redirect.Target.Contains("/proc/") && BashSecurityRegex.ProcEnvironRegex().IsMatch(redirect.Target))
                    return new BashSemanticCheckResult(false, "重定向访问 /proc/*/environ 可暴露敏感环境变量", BashSecurityCheckId.ProcEnvironAccess);
            }
        }

        return new BashSemanticCheckResult(true);
    }
}
