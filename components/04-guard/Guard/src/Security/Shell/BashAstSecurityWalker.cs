using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Bash AST 安全步行器 — 对齐 TS ast.ts parseForSecurity
/// 基于 TreeSitter.DotNet 解析结果，实现 FAIL-CLOSED 安全检查
///
/// 核心设计：任何无法静态分析的结构 → too-complex → 需要用户手动审批
/// 这不是沙箱，它只回答一个问题："我们能否为每个简单命令生成可信的 argv[]？"
/// </summary>
public interface IBashAstSecurityWalker
{
    /// <summary>
    /// 解析命令并提取安全信息 — 对齐 TS parseForSecurity
    /// </summary>
    BashAstSecurityResult ParseForSecurity(string command);

    /// <summary>
    /// 语义安全检查 — 对齐 TS ast.ts checkSemantics
    /// 对提取的命令列表进行安全规则检查
    /// </summary>
    BashSemanticCheckResult CheckSemantics(BashSimpleCommandInfo[] commands);
}

/// <summary>
/// AST 安全解析结果 — 对齐 TS ParseForSecurityResult
/// </summary>
public abstract record BashAstSecurityResult
{
    /// <summary>命令可静态分析，提取出简单命令列表</summary>
    public sealed record Simple(BashSimpleCommandInfo[] Commands) : BashAstSecurityResult;

    /// <summary>命令过于复杂，无法静态分析 — 需要用户手动审批</summary>
    public sealed record TooComplex(string Reason, string? NodeType = null) : BashAstSecurityResult;

    /// <summary>解析器不可用 — 回退到保守行为</summary>
    public sealed record ParseUnavailable : BashAstSecurityResult;
}

/// <summary>
/// 简单命令信息 — 对齐 TS ast.ts SimpleCommand
/// </summary>
public sealed record BashSimpleCommandInfo(
    string[] Argv,
    BashEnvVarInfo[] EnvVars,
    BashRedirectInfo[] Redirects,
    string Text);

/// <summary>环境变量赋值</summary>
public sealed record BashEnvVarInfo(string Name, string Value);

/// <summary>重定向信息</summary>
public sealed record BashRedirectInfo(string Op, string Target);

/// <summary>
/// 语义检查结果 — 对齐 TS ast.ts SemanticCheckResult
/// </summary>
public sealed record BashSemanticCheckResult(
    bool IsOk,
    string? Reason = null,
    BashSecurityCheckId? CheckId = null);

/// <summary>
/// 安全检查ID — 对齐 TS BASH_SECURITY_CHECK_IDS（语义检查子集）
/// </summary>
public enum BashSemanticCheckId
{
    /// <summary>eval类内置命令(eval/source/exec等)</summary>
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
    /// <summary>jq system()函数或危险标志</summary>
    [EnumValue("jqSystemFunction")]
    JqSystemFunction = 6,
    /// <summary>换行+井号(潜在注释注入)</summary>
    [EnumValue("midWordHash")]
    MidWordHash = 7,
    /// <summary>空命令名</summary>
    [EnumValue("emptyCommandName")]
    EmptyCommandName = 8,
    /// <summary>不完整片段(argv[0]以-开头)</summary>
    [EnumValue("incompleteFragment")]
    IncompleteFragment = 9,
}
