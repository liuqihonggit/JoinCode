namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 语义搜索选项 — AI 通过 MCP 工具动态控制召回策略。
/// <para>默认只返回元数据（文件路径+行号+符号名+相似度分数），与符号索引行为一致。</para>
/// <para>IncludeSourceText=true 时召回结果携带块原文（函数源码）。</para>
/// <para>IncludeParentDocument=true 时召回结果携带父文档原文（类/文件完整源码）。</para>
/// <para>FileType 非空时只返回指定扩展名的文件结果（如 "cs" 只搜 C# 代码，"md" 只搜 Markdown 文档）。</para>
/// </summary>
public sealed record SearchOptions {
    /// <summary>是否返回块原文（函数源码）— 默认 false（省内存，AI 可用 read 工具按行号读取）。</summary>
    public bool IncludeSourceText { get; init; }
    /// <summary>是否返回父文档原文（类/文件完整源码）— 默认 false（省内存，AI 可用 read 工具按行号读取）。</summary>
    public bool IncludeParentDocument { get; init; }
    /// <summary>文件类型过滤 — 传 "cs" 只返回 .cs 文件结果，传 "md" 只返回 .md 文档结果，null（默认）返回全部。</summary>
    public string? FileType { get; init; }
    /// <summary>命名空间过滤 — 传 "JoinCode.CodeIndex" 只返回该命名空间下的结果，null（默认）返回全部。支持前缀匹配。</summary>
    public string? Namespace { get; init; }
    /// <summary>符号类型过滤 — 传 "Method" 只返回方法符号，传 "Class" 只返回类符号，null（默认）返回全部。</summary>
    public string? SymbolKind { get; init; }
}
