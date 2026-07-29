namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 简单命令信息 — 对齐 TS ast.ts SimpleCommand
/// 统一 CodeIndex (BashSimpleCommand) 和 Guard (BashSimpleCommandInfo) 的重复定义
/// </summary>
public sealed record BashSimpleCommandInfo(
    string[] Argv,
    BashEnvVarInfo[] EnvVars,
    BashRedirectInfo[] Redirects,
    string Text);

/// <summary>
/// 环境变量赋值 — 统一 BashEnvVar / BashEnvVarInfo
/// </summary>
public sealed record BashEnvVarInfo(string Name, string Value);

/// <summary>
/// 重定向信息 — 统一 BashRedirect / BashRedirectInfo / RedirectResult
/// </summary>
public sealed record BashRedirectInfo(string Op, string Target, int? Fd = null);

/// <summary>
/// AST 安全解析结果 — 对齐 TS ParseForSecurityResult
/// 统一 CodeIndex 和 Guard 的重复定义
/// </summary>
public abstract record BashAstSecurityResult
{
    /// <summary>命令可静态分析，提取出简单命令列表</summary>
    public sealed record Simple(BashSimpleCommandInfo[] Commands) : BashAstSecurityResult;

    /// <summary>命令过于复杂，无法静态分析 — 需要用户手动审批</summary>
    public sealed record TooComplex(string Reason, string? NodeType = null) : BashAstSecurityResult;

    /// <summary>解析器不可用 — 回退到保守行为</summary>
    public sealed record ParseUnavailable(string? Reason = null) : BashAstSecurityResult;
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
/// 统一 BashSecurityValidator (23值) + CodeIndex (9值) + BashSemanticCheckId 的重复定义
/// </summary>
public enum BashSecurityCheckId
{
    /// <summary>不完整命令</summary>
    [EnumValue("incompleteCommands")]
    IncompleteCommands = 1,
    /// <summary>jq system()函数</summary>
    [EnumValue("jqSystemFunction")]
    JqSystemFunction = 2,
    /// <summary>jq文件参数</summary>
    [EnumValue("jqFileArguments")]
    JqFileArguments = 3,
    /// <summary>混淆标志</summary>
    [EnumValue("obfuscatedFlags")]
    ObfuscatedFlags = 4,
    /// <summary>Shell元字符</summary>
    [EnumValue("shellMetacharacters")]
    ShellMetacharacters = 5,
    /// <summary>危险变量</summary>
    [EnumValue("dangerousVariables")]
    DangerousVariables = 6,
    /// <summary>换行符</summary>
    [EnumValue("newlines")]
    Newlines = 7,
    /// <summary>命令替换</summary>
    [EnumValue("commandSubstitution")]
    CommandSubstitution = 8,
    /// <summary>输入重定向</summary>
    [EnumValue("inputRedirection")]
    InputRedirection = 9,
    /// <summary>输出重定向</summary>
    [EnumValue("outputRedirection")]
    OutputRedirection = 10,
    /// <summary>IFS注入</summary>
    [EnumValue("ifsInjection")]
    IfsInjection = 11,
    /// <summary>Git commit替换</summary>
    [EnumValue("gitCommitSubstitution")]
    GitCommitSubstitution = 12,
    /// <summary>/proc/environ访问</summary>
    [EnumValue("procEnvironAccess")]
    ProcEnvironAccess = 13,
    /// <summary>畸形Token注入</summary>
    [EnumValue("malformedTokenInjection")]
    MalformedTokenInjection = 14,
    /// <summary>反斜杠转义空白</summary>
    [EnumValue("backslashEscapedWhitespace")]
    BackslashEscapedWhitespace = 15,
    /// <summary>花括号展开</summary>
    [EnumValue("braceExpansion")]
    BraceExpansion = 16,
    /// <summary>控制字符</summary>
    [EnumValue("controlCharacters")]
    ControlCharacters = 17,
    /// <summary>Unicode空白</summary>
    [EnumValue("unicodeWhitespace")]
    UnicodeWhitespace = 18,
    /// <summary>词中井号</summary>
    [EnumValue("midWordHash")]
    MidWordHash = 19,
    /// <summary>Zsh危险命令</summary>
    [EnumValue("zshDangerousCommands")]
    ZshDangerousCommands = 20,
    /// <summary>反斜杠转义操作符</summary>
    [EnumValue("backslashEscapedOperators")]
    BackslashEscapedOperators = 21,
    /// <summary>注释引号失同步</summary>
    [EnumValue("commentQuoteDesync")]
    CommentQuoteDesync = 22,
    /// <summary>引号内换行</summary>
    [EnumValue("quotedNewline")]
    QuotedNewline = 23,
    /// <summary>eval类内置命令(eval/source/exec等)</summary>
    [EnumValue("evalLikeBuiltins")]
    EvalLikeBuiltins = 24,
    /// <summary>危险下标标志(test -v/printf -v等)</summary>
    [EnumValue("subscriptEvalFlags")]
    SubscriptEvalFlags = 25,
    /// <summary>Shell关键字(if/while/for等)</summary>
    [EnumValue("shellKeywords")]
    ShellKeywords = 26,
    /// <summary>空命令名</summary>
    [EnumValue("emptyCommandName")]
    EmptyCommandName = 27,
    /// <summary>不完整片段(argv[0]以-开头)</summary>
    [EnumValue("incompleteFragment")]
    IncompleteFragment = 28,
    /// <summary>Zsh危险内置命令(zmodload/emulate等)</summary>
    [EnumValue("zshDangerousBuiltins")]
    ZshDangerousBuiltins = 29,
}
