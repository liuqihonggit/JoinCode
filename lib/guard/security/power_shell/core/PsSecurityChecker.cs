namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PS 安全检查器 — 23 个 AST 安全检查，与 TS powershellSecurity.ts 1:1 对齐
/// 所有检查基于 AST，如果解析失败（Valid=false），返回 'ask' 作为安全默认值
/// </summary>
public static partial class PsSecurityChecker {
    /// <summary>检查上下文 — 缓存 GetAllCommands/DeriveSecurityFlags 结果，消除循环内重复求值</summary>
    private sealed record PsCheckContext(
        PsParsedCommand Parsed,
        IReadOnlyList<PsCommandElement> AllCommands,
        PsSecurityFlags Flags);

    /// <summary>
    /// 主入口：对 PS 命令执行全部安全检查
    /// </summary>
    public static PsSecurityResult CommandIsSafe(string command) {
        var parsed = PsAstParser.Parse(command);

        // 解析失败时无法确定安全性，默认询问用户
        if (!parsed.Valid) {
            return PsSecurityResult.Ask("Could not parse command for security analysis");
        }

        // 缓存耗时求值结果 — GetAllCommands + DeriveSecurityFlags 在入口一次求值，传入各检查器复用
        var ctx = new PsCheckContext(
            parsed,
            PsAstParser.GetAllCommands(parsed),
            PsAstParser.DeriveSecurityFlags(parsed));

        // 按顺序执行所有检查器
        var validators = new Func<PsCheckContext, PsSecurityResult>[]
        {
            CheckInvokeExpression,
            CheckDynamicCommandName,
            CheckEncodedCommand,
            CheckPwshCommandOrFile,
            CheckDownloadCradles,
            CheckDownloadUtilities,
            CheckAddType,
            CheckComObject,
            CheckDangerousFilePathExecution,
            CheckInvokeItem,
            CheckScheduledTask,
            CheckForEachMemberName,
            CheckStartProcess,
            CheckScriptBlockInjection,
            CheckSubExpressions,
            CheckExpandableStrings,
            CheckSplatting,
            CheckStopParsing,
            CheckMemberInvocations,
            CheckTypeLiterals,
            CheckEnvVarManipulation,
            CheckModuleLoading,
            CheckRuntimeStateManipulation,
            CheckWmiProcessSpawn,
        };

        foreach (var validator in validators) {
            var result = validator(ctx);
            if (result.Behavior == PermissionBehavior.Ask) {
                return result;
            }
        }

        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 1: Invoke-Expression ────────────────────────────────

    private static PsSecurityResult CheckInvokeExpression(PsCheckContext ctx) {
        if (PsAstParser.HasCommandNamed(ctx.Parsed, "Invoke-Expression")) {
            return PsSecurityResult.Ask("Command uses Invoke-Expression which can execute arbitrary code");
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 2: 动态命令名 ──────────────────────────────────────

    private static PsSecurityResult CheckDynamicCommandName(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            if (cmd.ElementTypes.Length == 0) continue;

            if (cmd.ElementTypes[0] != PsElementType.StringConstant) {
                return PsSecurityResult.Ask("Command name is a dynamic expression which cannot be statically validated");
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 3: 编码命令 ────────────────────────────────────────

    private static PsSecurityResult CheckEncodedCommand(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            if (PsAstParser.IsPowerShellExecutable(cmd.Name)) {
                if (PsAstParser.PsHasParamAbbreviation(cmd, "-encodedcommand", "-e")) {
                    return PsSecurityResult.Ask("Command uses encoded parameters which obscure intent");
                }
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 4: 嵌套 pwsh 进程 ──────────────────────────────────

    private static PsSecurityResult CheckPwshCommandOrFile(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            if (PsAstParser.IsPowerShellExecutable(cmd.Name)) {
                return PsSecurityResult.Ask("Command spawns a nested PowerShell process which cannot be validated");
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 5: 下载摇篮 ────────────────────────────────────────

    private static PsSecurityResult CheckDownloadCradles(PsCheckContext ctx) {
        // 跨命令检测：下载器 + IEX
        var hasDownloader = ctx.AllCommands.Any(c =>
            PsDangerousCmdlets.DownloaderNames.Contains(c.Name.ToLowerInvariant()));
        var hasIex = ctx.AllCommands.Any(c => {
            var lower = c.Name.ToLowerInvariant();
            return lower == "invoke-expression" || lower == "iex";
        });

        if (hasDownloader && hasIex) {
            return PsSecurityResult.Ask("Command downloads and executes remote code");
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 6: 下载工具 ────────────────────────────────────────

    private static PsSecurityResult CheckDownloadUtilities(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            var lower = cmd.Name.ToLowerInvariant();

            if (lower == "start-bitstransfer") {
                return PsSecurityResult.Ask("Command downloads files via BITS transfer");
            }

            if (lower is "certutil" or "certutil.exe") {
                if (cmd.Args.Any(a => {
                    var la = a.ToLowerInvariant();
                    return la == "-urlcache" || la == "/urlcache";
                })) {
                    return PsSecurityResult.Ask("Command uses certutil to download from a URL");
                }
            }

            if (lower is "bitsadmin" or "bitsadmin.exe") {
                if (cmd.Args.Any(a => a.ToLowerInvariant() == "/transfer")) {
                    return PsSecurityResult.Ask("Command downloads files via BITS transfer");
                }
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 7: Add-Type ────────────────────────────────────────

    private static PsSecurityResult CheckAddType(PsCheckContext ctx) {
        if (PsAstParser.HasCommandNamed(ctx.Parsed, "Add-Type")) {
            return PsSecurityResult.Ask("Command compiles and loads .NET code");
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 8: COM 对象 ────────────────────────────────────────

    private static PsSecurityResult CheckComObject(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            if (!cmd.Name.Equals("New-Object", StringComparison.OrdinalIgnoreCase)) continue;

            // -ComObject 缩写 -com
            if (PsAstParser.PsHasParamAbbreviation(cmd, "-comobject", "-com")) {
                return PsSecurityResult.Ask("Command instantiates a COM object which may have execution capabilities");
            }

            // 检查 -TypeName 参数的 CLM 合规性
            var typeName = ExtractTypeName(cmd);
            if (typeName is not null && !ClmAllowedTypes.IsClmAllowedType(typeName)) {
                return PsSecurityResult.Ask(
                    $"New-Object instantiates .NET type '{typeName}' outside the ConstrainedLanguage allowlist");
            }
        }
        return PsSecurityResult.Passthrough;
    }

    /// <summary>
    /// 从 New-Object 命令提取 -TypeName 值
    /// </summary>
    private static string? ExtractTypeName(PsCommandElement cmd) {
        // 命名参数 -TypeName
        for (var i = 0; i < cmd.Args.Length; i++) {
            var a = cmd.Args[i];
            var lower = a.ToLowerInvariant();

            // 冒号绑定形式：-TypeName:Foo.Bar
            if (lower.StartsWith("-t") && lower.Contains(':')) {
                var colonIdx = a.IndexOf(':');
                var paramPart = lower[..colonIdx];
                if ("-typename".StartsWith(paramPart)) {
                    return a[(colonIdx + 1)..];
                }
            }

            // 空格分隔形式：-TypeName Foo.Bar
            if (lower.StartsWith("-t") && "-typename".StartsWith(lower) &&
                i + 1 < cmd.Args.Length) {
                return cmd.Args[i + 1];
            }
        }

        // 位置参数 0 绑定到 -TypeName
        var valueParams = FrozenSet.ToFrozenSet<string>(
            ["-argumentlist", "-comobject", "-property"], StringComparer.OrdinalIgnoreCase);
        var switchParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "-strict" };

        for (var i = 0; i < cmd.Args.Length; i++) {
            var a = cmd.Args[i];
            if (a.StartsWith('-')) {
                var lower = a.ToLowerInvariant();
                if (lower.StartsWith("-t") && "-typename".StartsWith(lower)) { i++; continue; }
                if (lower.Contains(':')) continue;
                if (switchParams.Contains(lower)) continue;
                if (valueParams.Contains(lower)) { i++; continue; }
                continue;
            }
            return a; // 第一个非 dash 参数是位置 TypeName
        }
        return null;
    }

    // ─── 检查器 9: 危险文件路径执行 ────────────────────────────────

    private static PsSecurityResult CheckDangerousFilePathExecution(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            var lower = cmd.Name.ToLowerInvariant();
            var resolved = PsAliases.ResolveToCanonical(lower);

            if (!PsDangerousCmdlets.FilePathExecution.Contains(resolved)) continue;

            if (PsAstParser.PsHasParamAbbreviation(cmd, "-filepath", "-f") ||
                PsAstParser.PsHasParamAbbreviation(cmd, "-literalpath", "-l")) {
                return PsSecurityResult.Ask($"{cmd.Name} -FilePath executes an arbitrary script file");
            }

            // 位置参数绑定到 -FilePath
            for (var i = 0; i < cmd.Args.Length; i++) {
                if (i + 1 < cmd.ElementTypes.Length &&
                    cmd.ElementTypes[i + 1] == PsElementType.StringConstant &&
                    !cmd.Args[i].StartsWith('-')) {
                    return PsSecurityResult.Ask(
                        $"{cmd.Name} with positional string argument binds to -FilePath and executes a script file");
                }
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 10: Invoke-Item ────────────────────────────────────

    private static PsSecurityResult CheckInvokeItem(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            var lower = cmd.Name.ToLowerInvariant();
            if (lower is "invoke-item" or "ii") {
                return PsSecurityResult.Ask(
                    "Invoke-Item opens files with the default handler (ShellExecute). On executable files this runs arbitrary code.");
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 11: 计划任务 ────────────────────────────────────────

    private static PsSecurityResult CheckScheduledTask(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            var lower = cmd.Name.ToLowerInvariant();
            if (PsDangerousCmdlets.ScheduledTask.Contains(lower)) {
                return PsSecurityResult.Ask($"{cmd.Name} creates or modifies a scheduled task (persistence primitive)");
            }

            if (lower is "schtasks" or "schtasks.exe") {
                if (cmd.Args.Any(a => {
                    var la = a.ToLowerInvariant();
                    return la is "/create" or "/change" or "-create" or "-change";
                })) {
                    return PsSecurityResult.Ask(
                        "schtasks with create/change modifies scheduled tasks (persistence primitive)");
                }
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 12: ForEach-Object -MemberName ─────────────────────

    private static PsSecurityResult CheckForEachMemberName(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            var lower = cmd.Name.ToLowerInvariant();
            var resolved = PsAliases.ResolveToCanonical(lower);
            if (resolved != "foreach-object") continue;

            if (PsAstParser.PsHasParamAbbreviation(cmd, "-membername", "-m")) {
                return PsSecurityResult.Ask(
                    "ForEach-Object -MemberName invokes methods by string name which cannot be validated");
            }

            // 位置参数绑定到 -MemberName
            for (var i = 0; i < cmd.Args.Length; i++) {
                if (i + 1 < cmd.ElementTypes.Length &&
                    cmd.ElementTypes[i + 1] == PsElementType.StringConstant &&
                    !cmd.Args[i].StartsWith('-')) {
                    return PsSecurityResult.Ask(
                        "ForEach-Object with positional string argument binds to -MemberName and invokes methods by name");
                }
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 13: Start-Process ──────────────────────────────────

    private static PsSecurityResult CheckStartProcess(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            var lower = cmd.Name.ToLowerInvariant();
            if (lower is not ("start-process" or "saps" or "start")) continue;

            // 向量 1: -Verb RunAs（提权）
            if (PsAstParser.PsHasParamAbbreviation(cmd, "-verb", "-v") &&
                cmd.Args.Any(a => a.Equals("runas", StringComparison.OrdinalIgnoreCase))) {
                return PsSecurityResult.Ask("Command requests elevated privileges");
            }

            // 冒号绑定形式：-Verb:RunAs
            if (cmd.Args.Any(a => {
                var clean = a.Replace("`", "");
                return System.Text.RegularExpressions.Regex.IsMatch(
                    clean, @"^[-\u2013\u2014\u2015/]v[a-z]*:['""` ]*runas['""` ]*$", RegexOptions.IgnoreCase);
            })) {
                return PsSecurityResult.Ask("Command requests elevated privileges");
            }

            // 向量 2: Start-Process 目标是 PS 可执行文件
            foreach (var arg in cmd.Args) {
                var stripped = arg.Trim('\'', '"');
                if (PsAstParser.IsPowerShellExecutable(stripped)) {
                    return PsSecurityResult.Ask(
                        "Start-Process launches a nested PowerShell process which cannot be validated");
                }
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 14: 脚本块注入 ────────────────────────────────────

    private static PsSecurityResult CheckScriptBlockInjection(PsCheckContext ctx) {
        if (!ctx.Flags.HasScriptBlocks) return PsSecurityResult.Passthrough;

        // 检查是否有危险脚本块 cmdlet
        foreach (var cmd in ctx.AllCommands) {
            var lower = cmd.Name.ToLowerInvariant();
            if (PsDangerousCmdlets.DangerousScriptBlock.Contains(lower)) {
                return PsSecurityResult.Ask(
                    "Command contains script block with dangerous cmdlet that may execute arbitrary code");
            }
        }

        // 检查所有命令是否都是安全的脚本块消费者
        var allSafe = ctx.AllCommands.All(cmd => {
            var lower = cmd.Name.ToLowerInvariant();
            if (PsDangerousCmdlets.SafeScriptBlock.Contains(lower)) return true;
            if (PsAliases.TryResolve(lower, out var canonical) &&
                PsDangerousCmdlets.SafeScriptBlock.Contains(canonical.ToLowerInvariant())) {
                return true;
            }
            return false;
        });

        if (allSafe) return PsSecurityResult.Passthrough;

        return PsSecurityResult.Ask("Command contains script block that may execute arbitrary code");
    }

    // ─── 检查器 15: 子表达式 ────────────────────────────────────────

    private static PsSecurityResult CheckSubExpressions(PsCheckContext ctx) {
        if (ctx.Flags.HasSubExpressions) {
            return PsSecurityResult.Ask("Command contains subexpressions $()");
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 16: 可展开字符串 ───────────────────────────────────

    private static PsSecurityResult CheckExpandableStrings(PsCheckContext ctx) {
        if (ctx.Flags.HasExpandableStrings) {
            return PsSecurityResult.Ask("Command contains expandable strings with embedded expressions");
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 17: Splatting ──────────────────────────────────────

    private static PsSecurityResult CheckSplatting(PsCheckContext ctx) {
        if (ctx.Flags.HasSplatting) {
            return PsSecurityResult.Ask("Command uses splatting (@variable)");
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 18: Stop-parsing token ─────────────────────────────

    private static PsSecurityResult CheckStopParsing(PsCheckContext ctx) {
        if (ctx.Flags.HasStopParsing) {
            return PsSecurityResult.Ask("Command uses stop-parsing token (--%)");
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 19: .NET 方法调用 ──────────────────────────────────

    private static PsSecurityResult CheckMemberInvocations(PsCheckContext ctx) {
        if (ctx.Flags.HasMemberInvocations) {
            return PsSecurityResult.Ask("Command invokes .NET methods");
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 20: 类型字面量（CLM 白名单检查）──────────────────

    private static PsSecurityResult CheckTypeLiterals(PsCheckContext ctx) {
        foreach (var t in ctx.Parsed.TypeLiterals) {
            if (!ClmAllowedTypes.IsClmAllowedType(t)) {
                return PsSecurityResult.Ask(
                    $"Command uses .NET type [{t}] outside the ConstrainedLanguage allowlist");
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 21: 环境变量操纵 ──────────────────────────────────

    private static PsSecurityResult CheckEnvVarManipulation(PsCheckContext ctx) {
        var envVars = PsAstParser.GetVariablesByScope(ctx.Parsed, "env");
        if (envVars.Count == 0) return PsSecurityResult.Passthrough;

        foreach (var cmd in ctx.AllCommands) {
            if (PsDangerousCmdlets.EnvWrite.Contains(cmd.Name.ToLowerInvariant())) {
                return PsSecurityResult.Ask("Command modifies environment variables");
            }
        }

        if (ctx.Flags.HasAssignments) {
            return PsSecurityResult.Ask("Command modifies environment variables");
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 22: 模块加载 ──────────────────────────────────────

    private static PsSecurityResult CheckModuleLoading(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            if (PsDangerousCmdlets.ModuleLoading.Contains(cmd.Name.ToLowerInvariant())) {
                return PsSecurityResult.Ask(
                    "Command loads, installs, or downloads a PowerShell module or script, which can execute arbitrary code");
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 23: 运行时状态操纵 ────────────────────────────────

    private static PsSecurityResult CheckRuntimeStateManipulation(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            // 去除模块限定符
            var raw = cmd.Name.ToLowerInvariant();
            var lower = raw.Contains('\\') ? raw[(raw.LastIndexOf('\\') + 1)..] : raw;

            if (PsDangerousCmdlets.RuntimeState.Contains(lower)) {
                return PsSecurityResult.Ask(
                    "Command creates or modifies an alias or variable that can affect future command resolution");
            }
        }
        return PsSecurityResult.Passthrough;
    }

    // ─── 检查器 24: WMI 进程生成 ──────────────────────────────────

    private static PsSecurityResult CheckWmiProcessSpawn(PsCheckContext ctx) {
        foreach (var cmd in ctx.AllCommands) {
            if (PsDangerousCmdlets.WmiCim.Contains(cmd.Name.ToLowerInvariant())) {
                return PsSecurityResult.Ask(
                    $"{cmd.Name} can spawn arbitrary processes via WMI/CIM (Win32_Process Create)");
            }
        }
        return PsSecurityResult.Passthrough;
    }
}
