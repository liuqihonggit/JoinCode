namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 只读命令检测器实现 — 深度对齐 TS readOnlyValidation.ts
/// 核心功能: 白名单标志验证 + 正则验证 + 变量扩展检测 + git 沙箱逃逸防护
/// <para>TASK031-D2: 拆分为 5 个单一数据源服务 + 编排器</para>
/// <para>ICommandCatalog: 命令配置数据 | IShellMetacharacterDetector: 元字符检测</para>
/// <para>IFlagValidator: 标志验证 | IRegexValidator: 正则验证 | IExpansionDetector: 扩展检测</para>
/// </summary>
[Register(typeof(IReadOnlyCommandDetector), ServiceLifetime.Singleton)]
public sealed class ReadOnlyCommandDetector : ServiceEntity, IReadOnlyCommandDetector {
    private readonly ICommandCatalog _catalog;
    private readonly IShellMetacharacterDetector _metacharDetector;
    private readonly IFlagValidator _flagValidator;
    private readonly IRegexValidator _regexValidator;
    private readonly IExpansionDetector _expansionDetector;

    /// <summary>构造只读命令检测器，注入 5 个单一职责服务</summary>
    internal ReadOnlyCommandDetector(
        ICommandCatalog catalog,
        IShellMetacharacterDetector metacharDetector,
        IFlagValidator flagValidator,
        IRegexValidator regexValidator,
        IExpansionDetector expansionDetector) {
        _catalog = catalog;
        _metacharDetector = metacharDetector;
        _flagValidator = flagValidator;
        _regexValidator = regexValidator;
        _expansionDetector = expansionDetector;
    }

    /// <summary>
    /// 检查命令是否为只读命令 — 委托给 <see cref="CheckReadOnlyConstraints"/> 并判断结果是否为 Allow
    /// </summary>
    public bool IsReadOnly(ShellCommand command) {
        var result = CheckReadOnlyConstraints(command.RawCommand);
        return result.Behavior == PermissionBehavior.Allow;
    }

    /// <summary>
    /// 检查原始命令字符串是否只读 — 对齐 TS checkReadOnlyConstraints
    /// </summary>
    public ShellPermissionCheckResult CheckReadOnlyConstraints(string command, bool compoundCommandHasCd = false) {
        if (string.IsNullOrWhiteSpace(command)) {
            return new ShellPermissionCheckResult(PermissionBehavior.Passthrough);
        }

        var trimmed = command.Trim();

        // 1. 去除尾部 2>&1 重定向
        if (trimmed.EndsWith("2>&1", StringComparison.Ordinal)) {
            trimmed = trimmed[..^4].TrimEnd();
        }

        // 2. 检查 Windows UNC 路径
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)) {
            return new ShellPermissionCheckResult(PermissionBehavior.Ask, "UNC path detected");
        }

        // 3. 检查未引用的变量扩展
        if (_expansionDetector.ContainsUnquotedExpansion(trimmed)) {
            return new ShellPermissionCheckResult(PermissionBehavior.Passthrough);
        }

        // 4. 白名单标志验证
        if (IsCommandSafeViaFlagParsing(trimmed)) {
            return new ShellPermissionCheckResult(PermissionBehavior.Allow);
        }

        // 5. 正则验证
        if (_regexValidator.MatchesReadOnlyRegex(trimmed)) {
            // 额外检查 git 命令的危险标志
            if (_regexValidator.ContainsGitDangerousFlags(trimmed)) {
                return new ShellPermissionCheckResult(PermissionBehavior.Passthrough);
            }

            return new ShellPermissionCheckResult(PermissionBehavior.Allow);
        }

        return new ShellPermissionCheckResult(PermissionBehavior.Passthrough);
    }

    /// <summary>
    /// 白名单标志验证 — 对齐 TS isCommandSafeViaFlagParsing
    /// <para>编排 IExpansionDetector + IShellMetacharacterDetector + ICommandCatalog + IFlagValidator</para>
    /// </summary>
    private bool IsCommandSafeViaFlagParsing(string command) {
        var tokens = _expansionDetector.SplitCommandTokens(command);
        if (tokens.Count == 0) {
            return false;
        }

        // 存在操作符（管道/重定向等）→ 不安全
        if (_metacharDetector.ContainsShellOperators(command)) {
            return false;
        }

        var baseCommand = tokens[0];

        // 在白名单中查找匹配的命令配置（支持1/2/3-token键，如 "git config --get"）
        if (!_catalog.TryGetConfig(tokens, out var config)) {
            return false;
        }

        var args = tokens.Skip(1).ToList();

        // $ 变量扩展检测
        if (args.Any(arg => arg.Contains('$'))) {
            return false;
        }

        // 花括号扩展检测
        if (args.Any(arg => arg.Contains('{') && (arg.Contains(',') || arg.Contains("..")))) {
            return false;
        }

        // 验证标志合法性
        if (!_flagValidator.ValidateFlags(args, config.SafeFlags, config.RespectsDoubleDash)) {
            return false;
        }

        // 检查正则
        if (config.Regex is not null && !config.Regex.IsMatch(command)) {
            return false;
        }

        // 无正则时阻止反引号
        if (config.Regex is null && command.Contains('`')) {
            return false;
        }

        // 无正则时阻止 grep/rg 中的换行符
        if (config.Regex is null
            && (baseCommand.Equals("grep", StringComparison.OrdinalIgnoreCase)
                || baseCommand.Equals("rg", StringComparison.OrdinalIgnoreCase))
            && command.Contains('\n')) {
            return false;
        }

        // 额外危险回调
        if (config.AdditionalDangerousCallback is not null
            && config.AdditionalDangerousCallback(command, args)) {
            return false;
        }

        return true;
    }
}
