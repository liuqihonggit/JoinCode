
namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Bash安全验证器 — 对齐 TS bashSecurity.ts 的核心安全检查
/// 检测命令注入、混淆、绕过等安全威胁
/// </summary>
public interface IBashSecurityValidator
{
    /// <summary>
    /// 验证命令安全性，返回验证结果
    /// </summary>
    BashSecurityResult Validate(string command);
}

/// <summary>
/// Bash安全验证结果
/// </summary>
public sealed record BashSecurityResult(
    bool IsSafe,
    BashSecurityCheckId? CheckId = null,
    string? Message = null,
    bool IsMisparsing = false);

/// <summary>
/// 安全检查ID（对齐 TS BASH_SECURITY_CHECK_IDS）
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
}
