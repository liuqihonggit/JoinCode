namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 命令目录 — 单一数据源: CommandAllowlist + 5个FrozenSet + GitInternalPatterns
/// <para>管理所有命令配置数据，含 Build*SafeFlags 工厂方法。</para>
/// </summary>
internal interface ICommandCatalog {
    /// <summary>尝试获取命令配置（支持1/2/3-token键）</summary>
    bool TryGetConfig(List<string> tokens, [MaybeNullWhen(false)] out CommandConfig config);

    /// <summary>是否为简单只读命令</summary>
    bool IsSimpleReadOnly(string commandName);

    /// <summary>是否为 xargs 安全目标命令</summary>
    bool IsSafeXargsTarget(string commandName);

    /// <summary>是否为非创建型写入命令</summary>
    bool IsNonCreatingWrite(string commandName);

    /// <summary>是否为安全 Git 子命令</summary>
    bool IsSafeGitSubcommand(string subcommand);

    /// <summary>是否为危险 Git 子命令</summary>
    bool IsDangerousGitSubcommand(string subcommand);

    /// <summary>Git 内部路径模式</summary>
    IReadOnlyList<Regex> GitInternalPatterns { get; }
}

/// <summary>
/// Shell 元字符检测器 — 单一数据源: ShellMetaLowMask/HighMask 位掩码
/// </summary>
internal interface IShellMetacharacterDetector {
    /// <summary>判断字符是否为 Shell 元字符 — O(1) 位运算</summary>
    bool IsShellMetacharacter(char c);

    /// <summary>检查是否包含 shell 元字符</summary>
    bool ContainsShellMetacharacters(string command);

    /// <summary>检查是否包含 shell 操作符</summary>
    bool ContainsShellOperators(string command);
}

/// <summary>
/// 标志验证器 — 无独立数据源，纯逻辑验证
/// </summary>
internal interface IFlagValidator {
    /// <summary>验证标志合法性 — 对齐 TS validateFlags</summary>
    bool ValidateFlags(IReadOnlyList<string> args, FrozenDictionary<string, FlagArgType> safeFlags, bool respectsDoubleDash);
}

/// <summary>
/// 正则验证器 — 依赖 ICommandCatalog + IShellMetacharacterDetector
/// </summary>
internal interface IRegexValidator {
    /// <summary>正则验证 — 对齐 TS READONLY_COMMAND_REGEXES</summary>
    bool MatchesReadOnlyRegex(string command);

    /// <summary>检查 git 命令的危险标志</summary>
    bool ContainsGitDangerousFlags(string command);
}

/// <summary>
/// 扩展检测器 — 无独立数据源，纯字符串分析
/// </summary>
internal interface IExpansionDetector {
    /// <summary>检查未引用的变量扩展 — 对齐 TS containsUnquotedExpansion</summary>
    bool ContainsUnquotedExpansion(string command);

    /// <summary>分割命令为 token</summary>
    List<string> SplitCommandTokens(string command);
}
