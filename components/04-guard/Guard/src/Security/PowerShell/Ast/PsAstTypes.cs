namespace JoinCode.Guard.Security.PowerShell;

/// <summary>
/// PS AST 解析结果 — 通过 spawn pwsh 子进程解析，JSON 反序列化填充
/// 对齐 TS: ParsedPowerShellCommand
/// </summary>
public sealed class PsParsedCommand
{
    /// <summary>是否解析成功（无语法错误）</summary>
    public bool Valid { get; init; }

    /// <summary>原始命令文本</summary>
    public string OriginalCommand { get; init; } = string.Empty;

    /// <summary>解析错误</summary>
    public PsParseError[] Errors { get; init; } = [];

    /// <summary>是否包含 stop-parsing token (--%)</summary>
    public bool HasStopParsing { get; init; }

    /// <summary>所有 .NET 类型字面量（TypeExpressionAst + TypeConstraintAst）</summary>
    public string[] TypeLiterals { get; init; } = [];

    /// <summary>是否包含 using 语句（using module/assembly）</summary>
    public bool HasUsingStatements { get; init; }

    /// <summary>是否包含 #Requires 指令</summary>
    public bool HasScriptRequirements { get; init; }

    /// <summary>所有语句</summary>
    public PsStatement[] Statements { get; init; } = [];

    /// <summary>所有变量引用</summary>
    public PsVariable[] Variables { get; init; } = [];

    /// <summary>安全标志（从 AST 推导）</summary>
    public PsSecurityFlags? SecurityFlags { get; init; }
}

/// <summary>
/// PS 解析错误 — 对齐 TS ParseError
/// </summary>
public sealed class PsParseError
{
    public string Message { get; init; } = string.Empty;
    public string ErrorId { get; init; } = string.Empty;
}

/// <summary>
/// PS 语句 — 对应一个 pipeline（可含多个管道段命令）
/// </summary>
public sealed class PsStatement
{
    /// <summary>语句类型（PipelineAst, IfStatementAst 等）</summary>
    public string StatementType { get; init; } = string.Empty;

    /// <summary>主命令列表（管道段）</summary>
    public PsCommandElement[] Commands { get; init; } = [];

    /// <summary>嵌套命令列表（控制流 if/foreach/while 等内部的命令）</summary>
    public PsCommandElement[] NestedCommands { get; init; } = [];

    /// <summary>语句级重定向</summary>
    public PsRedirection[] Redirections { get; init; } = [];

    /// <summary>完整文本</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>安全模式（从 AST FindAll 推导）</summary>
    public PsSecurityPatterns? SecurityPatterns { get; init; }
}

/// <summary>
/// 安全模式 — 对齐 TS securityPatterns
/// </summary>
public sealed class PsSecurityPatterns
{
    public bool HasMemberInvocations { get; init; }
    public bool HasSubExpressions { get; init; }
    public bool HasExpandableStrings { get; init; }
    public bool HasScriptBlocks { get; init; }
}

/// <summary>
/// PS 命令元素 — 对应 CommandAst 的一个命令
/// </summary>
public sealed class PsCommandElement
{
    /// <summary>命令名（已去除模块前缀和引号）</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>命令名类型：cmdlet / application / unknown</summary>
    public PsCommandNameType NameType { get; init; }

    /// <summary>所有参数（包含 -Flag 形式）</summary>
    public string[] Args { get; init; } = [];

    /// <summary>每个元素的 AST 类型（Name + Args，索引 0 = Name）</summary>
    public PsElementType[] ElementTypes { get; init; } = [];

    /// <summary>完整文本</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>重定向列表</summary>
    public PsRedirection[] Redirections { get; init; } = [];
}

/// <summary>
/// 命令名分类
/// </summary>
public enum PsCommandNameType
{
    /// <summary>PS cmdlet（Verb-Noun 格式）</summary>
    [EnumValue("cmdlet")] Cmdlet,
    /// <summary>外部可执行文件（含路径分隔符）</summary>
    [EnumValue("application")] Application,
    /// <summary>未知</summary>
    [EnumValue("unknown")] Unknown,
}

/// <summary>
/// AST 元素类型 — 对应 TS 的 CommandElementType
/// </summary>
public enum PsElementType
{
    /// <summary>脚本块 { ... }</summary>
    [EnumValue("scriptBlock")] ScriptBlock,
    /// <summary>子表达式 $(...) / @(...) / (...)</summary>
    [EnumValue("subExpression")] SubExpression,
    /// <summary>可展开字符串 "..."</summary>
    [EnumValue("expandableString")] ExpandableString,
    /// <summary>.NET 方法调用</summary>
    [EnumValue("memberInvocation")] MemberInvocation,
    /// <summary>变量引用</summary>
    [EnumValue("variable")] Variable,
    /// <summary>字符串常量</summary>
    StringConstant,
    /// <summary>参数 -Xxx</summary>
    Parameter,
    /// <summary>其他</summary>
    Other,
}

/// <summary>
/// 重定向信息
/// </summary>
public sealed record PsRedirection(string Operator, string Target, bool IsMerging);

/// <summary>
/// 安全标志 — 从 AST 推导的安全相关标记
/// </summary>
public sealed class PsSecurityFlags
{
    public bool HasSubExpressions { get; init; }
    public bool HasScriptBlocks { get; init; }
    public bool HasSplatting { get; init; }
    public bool HasExpandableStrings { get; init; }
    public bool HasMemberInvocations { get; init; }
    public bool HasAssignments { get; init; }
    public bool HasStopParsing { get; init; }
}

/// <summary>
/// 变量引用
/// </summary>
public sealed record PsVariable(string Path, bool IsSplatted);
