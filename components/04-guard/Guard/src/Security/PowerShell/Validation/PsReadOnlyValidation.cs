namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PS 只读命令验证 — 与 TS readOnlyValidation.ts 核心逻辑对齐
/// 判断 PowerShell 命令是否可以自动放行（只读），不需要用户确认
/// 核心原则：fail-closed，任何不确定情况默认拒绝
/// </summary>
public static partial class PsReadOnlyValidation
{
    /// <summary>
    /// 判断整条命令是否为只读命令
    /// </summary>
    public static bool IsReadOnlyCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        var parsed = PsAstParser.Parse(command);
        return IsReadOnlyCommand(command, parsed);
    }

    /// <summary>
    /// 判断整条命令是否为只读命令（带预解析结果）
    /// </summary>
    public static bool IsReadOnlyCommand(string command, PsParsedCommand parsed)
    {
        if (!parsed.Valid) return false;

        // 安全标志检查 — 含脚本块、子表达式、可展开字符串、splatting、成员调用、赋值、stop-parsing → 拒绝
        var flags = PsAstParser.DeriveSecurityFlags(parsed);
        if (flags.HasScriptBlocks || flags.HasSubExpressions || flags.HasExpandableStrings
            || flags.HasSplatting || flags.HasMemberInvocations || flags.HasAssignments
            || flags.HasStopParsing)
        {
            return false;
        }

        // 逐语句验证
        foreach (var statement in parsed.Statements)
        {
            if (!IsStatementReadOnly(statement, command))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 判断单个语句是否为只读
    /// </summary>
    private static bool IsStatementReadOnly(PsStatement statement, string originalCommand)
    {
        // 拒绝含嵌套命令的语句
        if (statement.NestedCommands.Length > 0) return false;

        // 文件重定向检查
        foreach (var redir in statement.Redirections)
        {
            // > $null 允许，其他文件重定向拒绝
            if (!redir.IsMerging && !string.IsNullOrEmpty(redir.Target))
            {
                var target = redir.Target.Trim();
                if (!target.Equals("$null", StringComparison.OrdinalIgnoreCase)
                    && !target.Equals("[NullString]::Value", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // 管道段验证
        var commands = statement.Commands;
        if (commands.Length == 0) return false;

        for (var i = 0; i < commands.Length; i++)
        {
            var cmd = commands[i];

            if (i == 0)
            {
                // 管道首命令必须通过白名单验证
                if (!IsAllowlistedCommand(cmd, originalCommand)) return false;
            }
            else
            {
                // 管道后续命令：安全输出 cmdlet（无参数）或通过白名单验证
                if (IsSafeOutputCommand(cmd.Name))
                {
                    // 安全输出命令无参数时直接放行
                    if (cmd.Args.Length == 0) continue;
                }

                if (!IsAllowlistedCommand(cmd, originalCommand)) return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 判断单个命令是否在白名单中且通过标志验证
    /// </summary>
    private static bool IsAllowlistedCommand(PsCommandElement cmd, string originalCommand)
    {
        // nameType 门控 — application 类型（含路径字符）默认拒绝
        if (cmd.NameType == PsCommandNameType.Application)
        {
            return IsSafeExternalExe(cmd.Name);
        }

        // 白名单查找
        var config = LookupAllowlist(cmd.Name);
        if (config is null) return false;

        // 正则约束检查
        if (config.RegexPattern is not null)
        {
            try
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(
                    originalCommand, config.RegexPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return false;
                }
            }
            catch (System.Text.RegularExpressions.RegexParseException)
            {
                return false;
            }
        }

        // 额外危险回调
        if (config.AdditionalCheck is not null && config.AdditionalCheck(originalCommand, cmd))
        {
            return false;
        }

        // 参数元素类型白名单 — 仅允许 StringConstant 和 Parameter
        foreach (var elementType in cmd.ElementTypes)
        {
            if (elementType != PsElementType.StringConstant
                && elementType != PsElementType.Parameter)
            {
                return false;
            }
        }

        // argLeaksValue 守卫 — 输出类命令需检查参数是否泄露敏感值
        if (config.CheckArgLeaks && ArgLeaksValue(cmd))
        {
            return false;
        }

        // 标志验证
        if (!ValidateFlags(cmd, config))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 检测命令参数是否可能泄露敏感值
    /// </summary>
    private static bool ArgLeaksValue(PsCommandElement cmd)
    {
        for (var i = 0; i < cmd.Args.Length; i++)
        {
            var arg = cmd.Args[i];
            var elementType = i + 1 < cmd.ElementTypes.Length
                ? cmd.ElementTypes[i + 1]
                : PsElementType.Other;

            // 非安全类型直接拒绝
            if (elementType != PsElementType.StringConstant && elementType != PsElementType.Parameter)
            {
                return true;
            }

            // 冒号绑定参数值检查（-InputObject:$env:SECRET）
            if (arg.StartsWith('-') && arg.Contains(':'))
            {
                var colonIdx = arg.IndexOf(':', 1);
                if (colonIdx > 0 && colonIdx + 1 < arg.Length)
                {
                    var valuePart = arg[(colonIdx + 1)..];
                    if (ContainsMetaChars(valuePart)) return true;
                }
            }

            // 文本元字符检测
            if (elementType == PsElementType.StringConstant && ContainsMetaChars(arg))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查文本是否含元字符（$、(、@、{、[）
    /// </summary>
    private static bool ContainsMetaChars(string text)
    {
        return text.Contains('$') || text.Contains('(') || text.Contains('@')
            || text.Contains('{') || text.Contains('[');
    }

    /// <summary>
    /// 验证命令标志是否在安全列表中
    /// </summary>
    private static bool ValidateFlags(PsCommandElement cmd, PsAllowlistConfig config)
    {
        if (config.AllowAllFlags) return true;

        // 无 safeFlags 则拒绝所有标志
        if (config.SafeFlags is null || config.SafeFlags.Count == 0)
        {
            foreach (var arg in cmd.Args)
            {
                if (arg.StartsWith('-')) return false;
            }
            return true;
        }

        // 逐个验证标志
        foreach (var arg in cmd.Args)
        {
            if (!arg.StartsWith('-')) continue;

            // 去除冒号绑定的值
            var colonIdx = arg.IndexOf(':', 1);
            var flagPart = colonIdx > 0 ? arg[..colonIdx] : arg;

            // 检查是否在安全列表或通用参数中
            if (!config.SafeFlags.Contains(flagPart.ToLowerInvariant())
                && !CommonParameters.Contains(flagPart.ToLowerInvariant()))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 查找白名单配置
    /// </summary>
    private static PsAllowlistConfig? LookupAllowlist(string name)
    {
        var lower = name.ToLowerInvariant();

        // 直接查找
        if (CmdletAllowlist.TryGetValue(lower, out var config)) return config;

        // 别名解析后查找
        var canonical = PsAliases.ResolveToCanonical(lower);
        if (CmdletAllowlist.TryGetValue(canonical, out config)) return config;

        return null;
    }

    /// <summary>
    /// 判断是否为安全输出命令
    /// </summary>
    private static bool IsSafeOutputCommand(string name)
    {
        return SafeOutputCmdlets.Contains(name.ToLowerInvariant())
            || SafeOutputCmdlets.Contains(PsAliases.ResolveToCanonical(name.ToLowerInvariant()));
    }

    /// <summary>
    /// 判断外部 exe 是否安全（仅 where.exe 豁免）
    /// </summary>
    private static bool IsSafeExternalExe(string name)
    {
        return name.Equals("where.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断命令是否改变工作目录
    /// </summary>
    public static bool IsCwdChangingCmdlet(string name)
    {
        return CwdChangingCmdlets.Contains(name.ToLowerInvariant())
            || CwdChangingCmdlets.Contains(PsAliases.ResolveToCanonical(name.ToLowerInvariant()));
    }

    #region 常量

    /// <summary>
    /// 安全输出 cmdlet（仅 Out-Null）
    /// </summary>
    private static readonly FrozenSet<string> SafeOutputCmdlets = FrozenSet.ToFrozenSet(
        ["out-null"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 改变工作目录的 cmdlet
    /// </summary>
    private static readonly FrozenSet<string> CwdChangingCmdlets = FrozenSet.ToFrozenSet(
        ["set-location", "push-location", "pop-location", "new-psdrive"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// PS 通用参数 — 所有 cmdlet 共有，标志验证时自动豁免
    /// </summary>
    private static readonly FrozenSet<string> CommonParameters = FrozenSet.ToFrozenSet(
        [
            "-erroraction", "-ea", "-warningaction", "-wa",
            "-informationaction", "-infa", "-verbose", "-vb",
            "-debug", "-db", "-errorvariable", "-ev",
            "-warningvariable", "-wv", "-informationvariable", "-iv",
            "-outvariable", "-ov", "-outbuffer", "-ob",
            "-pipelinevariable", "-pv",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 只读 cmdlet 白名单 — 与 TS CMDLET_ALLOWLIST 对齐
    /// </summary>
    private static readonly FrozenDictionary<string, PsAllowlistConfig> CmdletAllowlist = BuildAllowlist();

    #endregion

    #region 白名单构建

    private static FrozenDictionary<string, PsAllowlistConfig> BuildAllowlist()
    {
        var dict = new Dictionary<string, PsAllowlistConfig>(StringComparer.OrdinalIgnoreCase);

        // ─── 文件系统只读 ──────────────────────────────────────
        dict["get-childitem"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath", "-filter", "-include", "-exclude", "-recurse", "-depth", "-name", "-force", "-attributes"],
        };
        dict["get-content"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath", "-readcount", "-totalcount", "-tail", "-force", "-delimiter", "-wait", "-raw", "-encoding", "-stream", "-nonewline", "-asbytestream"],
        };
        dict["get-item"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath", "-force", "-filter", "-include", "-exclude"],
        };
        dict["test-path"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath", "-isvalid", "-pathtype", "-filter", "-include", "-exclude"],
        };
        dict["resolve-path"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath", "-relative"],
        };
        dict["convert-path"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath"],
        };
        dict["join-path"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-childpath", "-resolve"],
        };
        dict["split-path"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath", "-parent", "-leaf", "-qualifier", "-noqualifier", "-extension", "-isabsolute"],
        };

        // ─── 导航 ──────────────────────────────────────────────
        dict["set-location"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath", "-passthru"],
        };
        dict["push-location"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath", "-passthru"],
        };
        dict["pop-location"] = new PsAllowlistConfig
        {
            SafeFlags = ["-passthru"],
        };

        // ─── 文本搜索 ──────────────────────────────────────────
        dict["select-string"] = new PsAllowlistConfig
        {
            SafeFlags = ["-path", "-literalpath", "-pattern", "-simplematch", "-casesensitive", "-quiet", "-list", "-include", "-exclude", "-encoding", "-context"],
        };

        // ─── 数据转换 ──────────────────────────────────────────
        dict["convertto-json"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["convertfrom-json"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["convertto-csv"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["convertfrom-csv"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["convertto-html"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["convertto-xml"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };

        // ─── 对象检查 ──────────────────────────────────────────
        dict["get-member"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["compare-object"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["measure-object"] = new PsAllowlistConfig { AllowAllFlags = true };

        // ─── 系统信息 ──────────────────────────────────────────
        dict["get-process"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-service"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-date"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-random"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-host"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-culture"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-uiculture"] = new PsAllowlistConfig { AllowAllFlags = true };

        // ─── 输出/格式化 ───────────────────────────────────────
        dict["write-output"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["write-host"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["write-verbose"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["write-debug"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["write-warning"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["out-string"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["out-host"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["out-null"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["format-list"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["format-table"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["format-wide"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["format-custom"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };

        // ─── 对象操作 ──────────────────────────────────────────
        dict["select-object"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["sort-object"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["where-object"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["foreach-object"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["group-object"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["get-unique"] = new PsAllowlistConfig { CheckArgLeaks = true, AllowAllFlags = true };
        dict["tee-object"] = new PsAllowlistConfig { CheckArgLeaks = true, SafeFlags = ["-variable"] };

        // ─── 变量/命令信息 ─────────────────────────────────────
        dict["get-variable"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-command"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-help"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-alias"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-history"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-location"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-psdrive"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-psprovider"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-module"] = new PsAllowlistConfig { AllowAllFlags = true };

        // ─── 网络信息 ──────────────────────────────────────────
        dict["get-netadapter"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-netipaddress"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-netroute"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["test-connection"] = new PsAllowlistConfig { AllowAllFlags = true };

        // ─── 事件日志 ──────────────────────────────────────────
        dict["get-eventlog"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["get-winevent"] = new PsAllowlistConfig { AllowAllFlags = true };

        // ─── 外部命令 ──────────────────────────────────────────
        dict["git"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["gh"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["docker"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["dotnet"] = new PsAllowlistConfig
        {
            SafeFlags = ["--version", "--info", "--list-runtimes", "--list-sdks"],
        };

        // ─── Windows 命令 ─────────────────────────────────────
        dict["hostname"] = new PsAllowlistConfig { SafeFlags = [] }; // 拒绝所有标志
        dict["whoami"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["ipconfig"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["netstat"] = new PsAllowlistConfig { AllowAllFlags = true };
        dict["systeminfo"] = new PsAllowlistConfig { AllowAllFlags = true };

        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}

/// <summary>
/// 白名单命令配置 — 对应 TS 的 CommandConfig
/// </summary>
public sealed class PsAllowlistConfig
{
    /// <summary>允许的安全标志列表</summary>
    public FrozenSet<string>? SafeFlags { get; init; }

    /// <summary>是否允许所有标志</summary>
    public bool AllowAllFlags { get; init; }

    /// <summary>正则约束模式</summary>
    public string? RegexPattern { get; init; }

    /// <summary>额外危险检查回调</summary>
    public Func<string, PsCommandElement, bool>? AdditionalCheck { get; init; }

    /// <summary>是否检查参数泄露值</summary>
    public bool CheckArgLeaks { get; init; }
}
